/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.OsTrader.Panels.Tab.IndexEngine.DataFeed;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.Calculator
{
    public interface IIndexCalculator
    {
        void Reset();

        IndexSnapshot Update(IndexDataFrame frame, IndexSettings settings, IndexState state);
    }
}
