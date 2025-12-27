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
                    _connectors[i].NewCandlesChangeEvent -= ConnectorOnNewCandlesChangeEvent;
                }
            }

            _connectors = connectors;

            if (_connectors != null)
            {
                for (int i = 0; i < _connectors.Count; i++)
                {
                    _connectors[i].NewCandlesChangeEvent += ConnectorOnNewCandlesChangeEvent;
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

        private void ConnectorOnNewCandlesChangeEvent(List<Candle> candles)
        {
            if (_isStarted == false)
            {
                return;
            }

            if (candles == null || candles.Count == 0)
            {
                return;
            }

            DateTime frameTime = candles[candles.Count - 1].TimeStart;

            IndexDataFrame frame = new IndexDataFrame
            {
                Time = frameTime
            };

            for (int i = 0; _connectors != null && i < _connectors.Count; i++)
            {
                ConnectorCandles connector = _connectors[i];
                List<Candle> connectorCandles = connector.Candles(true);

                if (connectorCandles == null || connectorCandles.Count == 0)
                {
                    continue;
                }

                Candle lastCandle = connectorCandles[connectorCandles.Count - 1];

                string securityName = connector.SecurityName;
                if (string.IsNullOrEmpty(securityName))
                {
                    securityName = connector.UniqueName;
                }

                IndexComponentFrame componentFrame = new IndexComponentFrame
                {
                    UniqueName = connector.UniqueName,
                    SecurityName = securityName,
                    Price = lastCandle.Close,
                    Time = lastCandle.TimeStart,
                    TimeFrameTimeSpan = connector.TimeFrameTimeSpan,
                    IsCandleClosed = lastCandle.State == CandleState.Finished
                };

                frame.Components.Add(componentFrame);
            }

            DataFrameReadyEvent?.Invoke(frame);
        }

        private bool _isStarted;
        private List<ConnectorCandles> _connectors;
    }
}
