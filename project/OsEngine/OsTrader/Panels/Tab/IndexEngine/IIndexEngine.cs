/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using OsEngine.OsTrader.Panels.Tab.IndexEngine.DataFeed;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine
{
    public interface IIndexEngine
    {
        event Action<IndexSnapshot> IndexUpdatedEvent;
        event Action<string> LogMessageEvent;

        IndexSettings Settings { get; }
        IndexState State { get; }

        void Initialize(IndexSettings settings, IIndexDataFeed dataFeed);
        void Start();
        void Stop();
        void Reset();
        void ReloadSettings();
    }
}
