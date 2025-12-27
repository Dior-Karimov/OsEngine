/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine
{
    public class IndexSnapshot
    {
        public decimal IndexValue { get; set; }

        public decimal IndexReturn { get; set; }

        public IndexQuality Quality { get; set; }

        public DateTime Time { get; set; }

        public int ActiveComponents { get; set; }

        public List<string> ActiveComponentNames { get; } = new List<string>();
    }
}
