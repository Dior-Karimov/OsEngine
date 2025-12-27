/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System.Collections.Generic;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.Models
{
    public class StdVolatilityModel : IVolatilityModel
    {
        public StdVolatilityModel(int lookback)
        {
            _lookback = lookback;
        }

        public void Reset()
        {
            _statsBySymbol.Clear();
        }

        public double Update(string symbol, double value)
        {
            if (_statsBySymbol.TryGetValue(symbol, out RollingStatistics stats) == false)
            {
                stats = new RollingStatistics(_lookback);
                _statsBySymbol[symbol] = stats;
            }

            stats.Add(value);
            return stats.StdDev;
        }

        public bool IsReady(string symbol)
        {
            return _statsBySymbol.TryGetValue(symbol, out RollingStatistics stats) && stats.IsReady;
        }

        private readonly int _lookback;
        private readonly Dictionary<string, RollingStatistics> _statsBySymbol = new Dictionary<string, RollingStatistics>();
    }
}
