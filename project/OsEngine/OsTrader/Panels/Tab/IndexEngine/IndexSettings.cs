/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine
{
    public class IndexSettings
    {
        public List<IndexComponentSettings> Components { get; } = new List<IndexComponentSettings>();

        public int VolLookbackBars { get; set; } = 120;

        public decimal SigmaMin { get; set; } = 0m;

        public IndexVolatilityMode VolatilityMode { get; set; } = IndexVolatilityMode.Std;

        public decimal EwmaLambda { get; set; } = 0.94m;

        public IndexStalenessMode StalenessMode { get; set; } = IndexStalenessMode.Auto;

        public int MaxStalenessBars { get; set; } = 3;

        public int MaxStalenessSeconds { get; set; } = 30;

        public decimal AutoStalenessMultiplier { get; set; } = 2m;

        public int MinActiveComponents { get; set; } = 3;

        public IndexUpdateMode UpdateMode { get; set; } = IndexUpdateMode.OnEveryUpdate;

        public int CalculationDepth { get; set; } = 1500;
    }

    public class IndexComponentSettings
    {
        public string UniqueName { get; set; }

        public string SecurityName { get; set; }

        public bool Enabled { get; set; } = true;

        public bool UseInIndex { get; set; } = true;

        public bool IsCross { get; set; }

        public int UsdDirection { get; set; }

        public TimeSpan TimeFrameTimeSpan { get; set; }

        public void UpdateSecurity(string securityName)
        {
            SecurityName = securityName;
            if (string.IsNullOrEmpty(SecurityName))
            {
                UsdDirection = 0;
                IsCross = false;
                return;
            }

            string upperName = SecurityName.ToUpperInvariant();
            bool hasUsd = upperName.Contains("USD");

            if (hasUsd && upperName.StartsWith("USD"))
            {
                UsdDirection = 1;
                IsCross = false;
            }
            else if (hasUsd && upperName.EndsWith("USD"))
            {
                UsdDirection = -1;
                IsCross = false;
            }
            else
            {
                UsdDirection = 0;
                IsCross = true;
            }
        }
    }

    public enum IndexQuality
    {
        Good,
        Degraded,
        Bad
    }

    public enum IndexVolatilityMode
    {
        Std,
        Ewma
    }

    public enum IndexStalenessMode
    {
        Auto,
        Bars,
        Seconds
    }

    public enum IndexUpdateMode
    {
        OnEveryUpdate,
        OnClosedCandle
    }
}
