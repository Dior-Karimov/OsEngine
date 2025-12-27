/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.DataFeed
{
    public interface IIndexDataFeed
    {
        event Action<IndexDataFrame> DataFrameReadyEvent;

        void Start();

        void Stop();
    }
}
