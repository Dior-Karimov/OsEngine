/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.DataFeed
{
    public class IndexDataFrame
    {
        public DateTime Time { get; set; }

        public List<IndexComponentFrame> Components { get; } = new List<IndexComponentFrame>();
    }

    public class IndexComponentFrame
    {
        public string UniqueName { get; set; }

        public string SecurityName { get; set; }

        public decimal Price { get; set; }

        public DateTime Time { get; set; }

        public TimeSpan TimeFrameTimeSpan { get; set; }

        public bool IsCandleClosed { get; set; }
    }
}
