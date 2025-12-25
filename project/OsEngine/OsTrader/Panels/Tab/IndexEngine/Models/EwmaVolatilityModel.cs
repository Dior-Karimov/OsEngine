/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.Models
{
    public class EwmaVolatilityModel : IVolatilityModel
    {
        public EwmaVolatilityModel(double lambda)
        {
            _lambda = lambda;
        }

        public void Reset()
        {
            _varianceBySymbol.Clear();
        }

        public double Update(string symbol, double value)
        {
            double squared = value * value;

            if (_varianceBySymbol.TryGetValue(symbol, out double variance) == false)
            {
                variance = squared;
            }
            else
            {
                variance = (_lambda * variance) + ((1 - _lambda) * squared);
            }

            _varianceBySymbol[symbol] = variance;
            return Math.Sqrt(variance);
        }

        public bool IsReady(string symbol)
        {
            return _varianceBySymbol.ContainsKey(symbol);
        }

        private readonly double _lambda;
        private readonly Dictionary<string, double> _varianceBySymbol = new Dictionary<string, double>();
    }
}
