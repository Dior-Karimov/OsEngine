/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using OsEngine.OsTrader.Panels.Tab.IndexEngine.Calculator;
using OsEngine.OsTrader.Panels.Tab.IndexEngine.DataFeed;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine
{
    public class IndexEngine : IIndexEngine
    {
        public event Action<IndexSnapshot> IndexUpdatedEvent;
        public event Action<string> LogMessageEvent;

        public IndexSettings Settings => _settings;

        public IndexState State => _state;

        public void Initialize(IndexSettings settings, IIndexDataFeed dataFeed)
        {
            _settings = settings;
            _dataFeed = dataFeed;
            _calculator = new LogReturnIndexCalculator(settings);
            _state = new IndexState();

            if (_dataFeed != null)
            {
                _dataFeed.DataFrameReadyEvent += DataFeedOnDataFrameReadyEvent;
            }
        }

        public void Start()
        {
            _dataFeed?.Start();
        }

        public void Stop()
        {
            _dataFeed?.Stop();
        }

        public void Reset()
        {
            _state = new IndexState();
            _calculator?.Reset();
        }

        public void ReloadSettings()
        {
            _calculator = new LogReturnIndexCalculator(_settings);
        }

        private void DataFeedOnDataFrameReadyEvent(IndexDataFrame frame)
        {
            try
            {
                IndexSnapshot snapshot = _calculator.Update(frame, _settings, _state);

                if (snapshot != null)
                {
                    IndexUpdatedEvent?.Invoke(snapshot);
                }
            }
            catch (Exception ex)
            {
                LogMessageEvent?.Invoke(ex.ToString());
            }
        }

        private IndexSettings _settings;
        private IndexState _state;
        private IIndexDataFeed _dataFeed;
        private IIndexCalculator _calculator;
    }
}
