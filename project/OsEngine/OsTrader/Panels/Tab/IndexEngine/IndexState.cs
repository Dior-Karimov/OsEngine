/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine
{
    public class IndexState
    {
        public decimal IndexValue { get; set; } = 100m;

        public decimal IndexReturn { get; set; }

        public IndexQuality Quality { get; set; } = IndexQuality.Bad;

        public DateTime LastUpdateTime { get; set; } = DateTime.MinValue;

        public Dictionary<string, decimal> LastPrices { get; } = new Dictionary<string, decimal>();

        public Dictionary<string, DateTime> LastTimes { get; } = new Dictionary<string, DateTime>();
    }
}
