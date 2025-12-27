/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using OsEngine.Candles;
using OsEngine.Entity;
using OsEngine.Market.Connectors;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.DataFeed
{
    public class ConnectorIndexDataFeed : IIndexDataFeed
    {
        public ConnectorIndexDataFeed(List<ConnectorCandles> connectors)
        {
            SetConnectors(connectors);
        }

        public event Action<IndexDataFrame> DataFrameReadyEvent;

        public void SetConnectors(List<ConnectorCandles> connectors)
        {
            if (_connectors != null)
            {
                for (int i = 0; i < _connectors.Count; i++)
                {
                    ConnectorCandles existingConnector = _connectors[i];
                    if (_connectorHandlers.TryGetValue(existingConnector, out Action<List<Candle>> handler))
                    {
                        existingConnector.NewCandlesChangeEvent -= handler;
                    }
                }
            }

            _connectors = connectors;
            _connectorHandlers.Clear();
            _lastCandles.Clear();
            _lastClosedCandles.Clear();
            _lastEmittedClosedTime = DateTime.MinValue;

            if (_connectors != null)
            {
                for (int i = 0; i < _connectors.Count; i++)
                {
                    ConnectorCandles connector = _connectors[i];
                    Action<List<Candle>> handler = candles => ConnectorOnNewCandlesChangeEvent(connector, candles);
                    _connectorHandlers[connector] = handler;
                    connector.NewCandlesChangeEvent += handler;
                }
            }
        }

        public void Start()
        {
            _isStarted = true;
        }

        public void Stop()
        {
            _isStarted = false;
        }

        private void ConnectorOnNewCandlesChangeEvent(ConnectorCandles connector, List<Candle> candles)
        {
            if (_isStarted == false)
            {
                return;
            }

            if (candles == null || candles.Count == 0)
            {
                return;
            }

            if (TryUpdateConnectorSnapshots(connector) == false)
            {
                return;
            }

            if (TryBuildClosedFrame(out IndexDataFrame closedFrame))
            {
                DataFrameReadyEvent?.Invoke(closedFrame);
                return;
            }

            if (TryBuildLiveFrame(out IndexDataFrame liveFrame) == false)
            {
                return;
            }

            DataFrameReadyEvent?.Invoke(liveFrame);
        }

        private bool _isStarted;
        private List<ConnectorCandles> _connectors;
        private readonly Dictionary<ConnectorCandles, Action<List<Candle>>> _connectorHandlers =
            new Dictionary<ConnectorCandles, Action<List<Candle>>>();
        private readonly Dictionary<string, CandleSnapshot> _lastCandles =
            new Dictionary<string, CandleSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ClosedCandleInfo> _lastClosedCandles =
            new Dictionary<string, ClosedCandleInfo>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastEmittedClosedTime = DateTime.MinValue;

        private static Candle GetLastClosedCandle(List<Candle> candles)
        {
            for (int i = candles.Count - 1; i >= 0; i--)
            {
                Candle candle = candles[i];
                if (candle.State == CandleState.Finished)
                {
                    return candle;
                }
            }

            return null;
        }

        private bool TryUpdateConnectorSnapshots(ConnectorCandles connector)
        {
            if (connector == null)
            {
                return false;
            }

            List<Candle> connectorCandles = connector.Candles(true);

            if (connectorCandles == null || connectorCandles.Count == 0)
            {
                return false;
            }

            Candle lastCandle = connectorCandles[connectorCandles.Count - 1];

            _lastCandles[connector.UniqueName] = new CandleSnapshot
            {
                TimeStart = lastCandle.TimeStart,
                Close = lastCandle.Close,
                IsClosed = lastCandle.State == CandleState.Finished
            };

            Candle closedCandle = GetLastClosedCandle(connectorCandles);

            if (closedCandle != null)
            {
                _lastClosedCandles[connector.UniqueName] = new ClosedCandleInfo
                {
                    TimeStart = closedCandle.TimeStart,
                    Close = closedCandle.Close
                };
            }

            return true;
        }

        private bool TryBuildLiveFrame(out IndexDataFrame frame)
        {
            frame = null;

            if (_connectors == null || _connectors.Count == 0)
            {
                return false;
            }

            DateTime maxTime = DateTime.MinValue;

            for (int i = 0; i < _connectors.Count; i++)
            {
                ConnectorCandles connector = _connectors[i];

                if (_lastCandles.TryGetValue(connector.UniqueName, out CandleSnapshot info) == false)
                {
                    return false;
                }

                if (info.TimeStart > maxTime)
                {
                    maxTime = info.TimeStart;
                }
            }

            IndexDataFrame newFrame = new IndexDataFrame
            {
                Time = maxTime
            };

            for (int i = 0; i < _connectors.Count; i++)
            {
                ConnectorCandles connector = _connectors[i];

                if (_lastCandles.TryGetValue(connector.UniqueName, out CandleSnapshot info) == false)
                {
                    return false;
                }

                string securityName = connector.SecurityName;
                if (string.IsNullOrEmpty(securityName))
                {
                    securityName = connector.UniqueName;
                }

                IndexComponentFrame componentFrame = new IndexComponentFrame
                {
                    UniqueName = connector.UniqueName,
                    SecurityName = securityName,
                    Price = info.Close,
                    Time = info.TimeStart,
                    TimeFrameTimeSpan = connector.TimeFrameTimeSpan,
                    IsCandleClosed = info.IsClosed
                };

                newFrame.Components.Add(componentFrame);
            }

            frame = newFrame;
            return true;
        }

        private bool TryBuildClosedFrame(out IndexDataFrame frame)
        {
            frame = null;

            if (_connectors == null || _connectors.Count == 0)
            {
                return false;
            }

            DateTime minTime = DateTime.MaxValue;
            DateTime maxTime = DateTime.MinValue;

            for (int i = 0; i < _connectors.Count; i++)
            {
                ConnectorCandles connector = _connectors[i];

                if (_lastClosedCandles.TryGetValue(connector.UniqueName, out ClosedCandleInfo info) == false)
                {
                    return false;
                }

                if (info.TimeStart < minTime)
                {
                    minTime = info.TimeStart;
                }

                if (info.TimeStart > maxTime)
                {
                    maxTime = info.TimeStart;
                }
            }

            if (minTime != maxTime)
            {
                return false;
            }

            if (minTime <= _lastEmittedClosedTime)
            {
                return false;
            }

            IndexDataFrame newFrame = new IndexDataFrame
            {
                Time = minTime
            };

            for (int i = 0; i < _connectors.Count; i++)
            {
                ConnectorCandles connector = _connectors[i];

                if (_lastClosedCandles.TryGetValue(connector.UniqueName, out ClosedCandleInfo info) == false)
                {
                    return false;
                }

                string securityName = connector.SecurityName;
                if (string.IsNullOrEmpty(securityName))
                {
                    securityName = connector.UniqueName;
                }

                IndexComponentFrame componentFrame = new IndexComponentFrame
                {
                    UniqueName = connector.UniqueName,
                    SecurityName = securityName,
                    Price = info.Close,
                    Time = minTime,
                    TimeFrameTimeSpan = connector.TimeFrameTimeSpan,
                    IsCandleClosed = true
                };

                newFrame.Components.Add(componentFrame);
            }

            _lastEmittedClosedTime = minTime;
            frame = newFrame;
            return true;
        }

        private struct CandleSnapshot
        {
            public DateTime TimeStart;
            public decimal Close;
            public bool IsClosed;
        }

        private struct ClosedCandleInfo
        {
            public DateTime TimeStart;
            public decimal Close;
        }
    }
}
