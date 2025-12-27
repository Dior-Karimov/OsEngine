/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.Models
{
    public interface IVolatilityModel
    {
        void Reset();

        double Update(string symbol, double value);

        bool IsReady(string symbol);
    }
}
