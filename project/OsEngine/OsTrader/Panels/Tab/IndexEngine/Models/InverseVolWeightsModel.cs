/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.Models
{
    public class InverseVolWeightsModel : IWeightsModel
    {
        public Dictionary<string, double> ComputeWeights(Dictionary<string, double> volatilityBySymbol, double sigmaMin)
        {
            Dictionary<string, double> raw = new Dictionary<string, double>();
            double sum = 0;

            foreach (KeyValuePair<string, double> pair in volatilityBySymbol)
            {
                double sigma = pair.Value;
                if (sigma < sigmaMin)
                {
                    sigma = sigmaMin;
                }

                if (sigma <= 0)
                {
                    continue;
                }

                double weight = 1.0 / sigma;
                raw[pair.Key] = weight;
                sum += weight;
            }

            Dictionary<string, double> weights = new Dictionary<string, double>();

            if (sum <= 0)
            {
                return weights;
            }

            foreach (KeyValuePair<string, double> pair in raw)
            {
                weights[pair.Key] = pair.Value / sum;
            }

            return weights;
        }
    }
}
