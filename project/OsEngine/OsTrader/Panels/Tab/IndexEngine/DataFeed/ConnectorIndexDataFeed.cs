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
            _lastClosedCandles.Clear();
            _lastEmittedTime = DateTime.MinValue;

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

            Candle closedCandle = GetLastClosedCandle(candles);

            if (closedCandle == null)
            {
                return;
            }

            _lastClosedCandles[connector.UniqueName] = new ClosedCandleInfo
            {
                TimeStart = closedCandle.TimeStart,
                Close = closedCandle.Close
            };

            if (TryBuildFrame(out IndexDataFrame frame) == false)
            {
                return;
            }

            DataFrameReadyEvent?.Invoke(frame);
        }

        private bool _isStarted;
        private List<ConnectorCandles> _connectors;
        private readonly Dictionary<ConnectorCandles, Action<List<Candle>>> _connectorHandlers =
            new Dictionary<ConnectorCandles, Action<List<Candle>>>();
        private readonly Dictionary<string, ClosedCandleInfo> _lastClosedCandles =
            new Dictionary<string, ClosedCandleInfo>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastEmittedTime = DateTime.MinValue;

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

        private bool TryBuildFrame(out IndexDataFrame frame)
        {
            frame = null;

            if (_connectors == null || _connectors.Count == 0)
            {
                return false;
            }

            DateTime commonTime = DateTime.MaxValue;

            for (int i = 0; i < _connectors.Count; i++)
            {
                ConnectorCandles connector = _connectors[i];

                if (_lastClosedCandles.TryGetValue(connector.UniqueName, out ClosedCandleInfo info) == false)
                {
                    return false;
                }

                if (info.TimeStart < commonTime)
                {
                    commonTime = info.TimeStart;
                }
            }

            if (commonTime <= _lastEmittedTime)
            {
                return false;
            }

            IndexDataFrame newFrame = new IndexDataFrame
            {
                Time = commonTime
            };

            for (int i = 0; i < _connectors.Count; i++)
            {
                ConnectorCandles connector = _connectors[i];

                if (_lastClosedCandles.TryGetValue(connector.UniqueName, out ClosedCandleInfo info) == false)
                {
                    return false;
                }

                if (info.TimeStart != commonTime)
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
                    Time = commonTime,
                    TimeFrameTimeSpan = connector.TimeFrameTimeSpan,
                    IsCandleClosed = true
                };

                newFrame.Components.Add(componentFrame);
            }

            _lastEmittedTime = commonTime;
            frame = newFrame;
            return true;
        }

        private struct ClosedCandleInfo
        {
            public DateTime TimeStart;
            public decimal Close;
        }
    }
}
