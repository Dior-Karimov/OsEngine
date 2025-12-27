/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Charts.CandleChart;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.Logging;
using OsEngine.Market.Connectors;
using OsEngine.OsTrader.Panels.Tab.IndexEngine;
using OsEngine.OsTrader.Panels.Tab.IndexEngine.DataFeed;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Shapes;

namespace OsEngine.OsTrader.Panels.Tab
{
    public class BotTabCustomIndex : IIBotTab
    {
        #region Service

        public BotTabCustomIndex(string name, StartProgram startProgram)
        {
            TabName = name;
            _startProgram = startProgram;

            _chartMaster = new ChartCandleMaster(TabName, _startProgram);

            Load();

            _dataFeed = new ConnectorIndexDataFeed(Tabs);
            _engine = new IndexEngine.IndexEngine();
            _engine.Initialize(Settings, _dataFeed);
            _engine.IndexUpdatedEvent += EngineOnIndexUpdatedEvent;
            _engine.LogMessageEvent += message => SendNewLogMessage(message, LogMessageType.Error);
            _engine.Start();
        }

        public BotTabType TabType => BotTabType.CustomIndex;

        public string TabName { get; set; }

        public int TabNum { get; set; }

        public bool EventsIsOn
        {
            get => _eventsIsOn;
            set
            {
                if (_eventsIsOn == value)
                {
                    return;
                }

                _eventsIsOn = value;
                Save();
            }
        }

        public bool EmulatorIsOn { get; set; }

        public DateTime LastTimeCandleUpdate { get; set; }

        public event Action TabDeletedEvent;
        public event Action<string, LogMessageType> LogMessageEvent;

        public bool IsConnected
        {
            get
            {
                if (Tabs == null || Tabs.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < Tabs.Count; i++)
                {
                    if (Tabs[i].IsConnected == false)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public List<ConnectorCandles> Tabs { get; } = new List<ConnectorCandles>();

        public List<Candle> Candles { get; private set; } = new List<Candle>();

        public event Action<List<Candle>> IndexChangeEvent;
        public event Action<IndexSnapshot> IndexUpdatedEvent;
        public event Action SecuritiesSelectionClosedEvent;

        public IndexSettings Settings { get; } = new IndexSettings();

        public IndexSnapshot LastSnapshot { get; private set; }

        public void ApplySettings()
        {
            _engine?.ReloadSettings();
            Save();
        }

        public void Clear()
        {
            Candles = new List<Candle>();
            _engine?.Reset();
            _chartMaster?.Clear();
        }

        public void Delete()
        {
            if (_ui != null)
            {
                _ui.Close();
            }

            if (UiSecuritiesSelection != null)
            {
                UiSecuritiesSelection.Close();
            }

            if (_engine != null)
            {
                _engine.IndexUpdatedEvent -= EngineOnIndexUpdatedEvent;
                _engine.Stop();
            }

            if (_dataFeed != null)
            {
                _dataFeed.Stop();
            }

            for (int i = 0; i < Tabs.Count; i++)
            {
                Tabs[i].Delete();
            }

            Tabs.Clear();

            _chartMaster.Delete();

            if (File.Exists(GetConnectorFilePath()))
            {
                File.Delete(GetConnectorFilePath());
            }

            if (File.Exists(GetSettingsFilePath()))
            {
                File.Delete(GetSettingsFilePath());
            }

            TabDeletedEvent?.Invoke();
        }

        public void StartPaint(Grid grid, WindowsFormsHost host, Rectangle rectangle)
        {
            _chartMaster.StartPaint(grid, host, rectangle);
        }

        public void StopPaint()
        {
            _chartMaster.StopPaint();
        }

        public void ShowDialog()
        {
            if (_ui == null)
            {
                _ui = new BotTabCustomIndexUi(this);
                _ui.Closed += UiOnClosed;
                _ui.Show();
            }
            else
            {
                if (_ui.WindowState == System.Windows.WindowState.Minimized)
                {
                    _ui.WindowState = System.Windows.WindowState.Normal;
                }

                _ui.Activate();
            }
        }

        private void UiOnClosed(object sender, EventArgs e)
        {
            if (_ui == null)
            {
                return;
            }

            _ui.Closed -= UiOnClosed;
            _ui = null;
        }

        #endregion

        #region Connectors

        public bool ShowNewSecurityDialog()
        {
            if (UiSecuritiesSelection == null)
            {
                Creator = GetCurrentCreator();
                UiSecuritiesSelection = new MassSourcesCreateUi(Creator);
                UiSecuritiesSelection.LogMessageEvent += SendNewLogMessage;
                UiSecuritiesSelection.Closed += UiSecuritiesSelectionOnClosed;
                UiSecuritiesSelection.Show();
                return true;
            }

            UiSecuritiesSelection.Activate();
            return false;
        }

        public void SetNewSecuritiesList(List<ActivatedSecurity> securitiesList)
        {
            Dictionary<string, ComponentState> previousState = CaptureComponentState();

            bool isDeleteTab = false;

            ConnectorCandles[] connectors = Tabs.ToArray();

            for (int i = 0; i < connectors.Length; i++)
            {
                connectors[i].Delete();
                isDeleteTab = true;
            }

            if (isDeleteTab)
            {
                Save();
            }

            for (int i = 0; i < securitiesList.Count; i++)
            {
                TryRunSecurity(securitiesList[i], Creator);
            }

            RestoreComponentState(previousState);
            Save();
        }

        private void UiSecuritiesSelectionOnClosed(object sender, EventArgs e)
        {
            try
            {
                UiSecuritiesSelection.LogMessageEvent -= SendNewLogMessage;
                UiSecuritiesSelection.Closed -= UiSecuritiesSelectionOnClosed;

                if (UiSecuritiesSelection.IsAccepted == false)
                {
                    UiSecuritiesSelection = null;
                    return;
                }

                bool isDeleteTab = false;

                for (int i = 0; i < Tabs.Count; i++)
                {
                    if (Tabs[i].TimeFrame != Creator.TimeFrame)
                    {
                        Tabs[i].Delete();
                        Tabs.RemoveAt(i);
                        isDeleteTab = true;
                        i--;
                    }
                }

                if (isDeleteTab)
                {
                    Save();
                }

                Dictionary<string, ComponentState> previousState = CaptureComponentState();

                Creator = UiSecuritiesSelection.SourcesCreator;

                if (Creator.SecuritiesNames != null && Creator.SecuritiesNames.Count != 0)
                {
                    for (int i = 0; i < Creator.SecuritiesNames.Count; i++)
                    {
                        TryRunSecurity(Creator.SecuritiesNames[i], Creator);
                    }

                    RestoreComponentState(previousState);
                    Save();
                }

                UiSecuritiesSelection.SourcesCreator = null;
            }
            catch (Exception ex)
            {
                SendNewLogMessage(ex.ToString(), LogMessageType.Error);
            }

            UiSecuritiesSelection = null;
            SecuritiesSelectionClosedEvent?.Invoke();
        }

        public void AddSecurityTab()
        {
            ConnectorCandles connector = new ConnectorCandles(TabName + Tabs.Count, _startProgram, false);
            connector.SaveTradesInCandles = false;
            Tabs.Add(connector);
            SyncComponents();
            Save();
            ResetDataFeed();
        }

        public void DeleteSecurityTab(int index)
        {
            if (Tabs.Count <= index)
            {
                return;
            }

            Tabs[index].Delete();
            Tabs.RemoveAt(index);
            SyncComponents();
            Save();
            ResetDataFeed();
        }

        public void ShowConnectorDialog(int index)
        {
            if (index < 0 || index >= Tabs.Count)
            {
                return;
            }

            Tabs[index].ShowDialog(false);
            SyncComponents();
            Save();
            ResetDataFeed();
        }

        private void ResetDataFeed()
        {
            _dataFeed?.SetConnectors(Tabs);
        }

        private void SyncComponents()
        {
            foreach (ConnectorCandles connector in Tabs)
            {
                string uniqueName = connector.UniqueName;
                IndexComponentSettings existing = Settings.Components.FirstOrDefault(c => c.UniqueName == uniqueName);

                if (existing == null && string.IsNullOrEmpty(connector.SecurityName) == false)
                {
                    existing = Settings.Components.FirstOrDefault(c =>
                        string.Equals(c.SecurityName, connector.SecurityName, StringComparison.OrdinalIgnoreCase));
                }

                bool isNew = false;

                if (existing == null)
                {
                    existing = new IndexComponentSettings { UniqueName = uniqueName };
                    Settings.Components.Add(existing);
                    isNew = true;
                }
                else if (existing.UniqueName != uniqueName)
                {
                    existing.UniqueName = uniqueName;
                }

                existing.UpdateSecurity(connector.SecurityName);
                existing.TimeFrameTimeSpan = connector.TimeFrameTimeSpan;
                existing.SecurityName = connector.SecurityName;

                if (isNew)
                {
                    existing.UseInIndex = existing.IsCross == false;
                }
            }

            Settings.Components.RemoveAll(c => Tabs.All(t => t.UniqueName != c.UniqueName));
        }

        private Dictionary<string, ComponentState> CaptureComponentState()
        {
            Dictionary<string, ComponentState> state = new Dictionary<string, ComponentState>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Settings.Components.Count; i++)
            {
                IndexComponentSettings component = Settings.Components[i];

                if (string.IsNullOrEmpty(component.SecurityName))
                {
                    continue;
                }

                state[component.SecurityName] = new ComponentState
                {
                    Enabled = component.Enabled,
                    UseInIndex = component.UseInIndex
                };
            }

            return state;
        }

        private void RestoreComponentState(Dictionary<string, ComponentState> state)
        {
            if (state == null || state.Count == 0)
            {
                return;
            }

            for (int i = 0; i < Settings.Components.Count; i++)
            {
                IndexComponentSettings component = Settings.Components[i];

                if (string.IsNullOrEmpty(component.SecurityName))
                {
                    continue;
                }

                if (state.TryGetValue(component.SecurityName, out ComponentState saved))
                {
                    component.Enabled = saved.Enabled;
                    component.UseInIndex = saved.UseInIndex;
                }
                else if (component.UseInIndex == false)
                {
                    component.UseInIndex = true;
                }
            }
        }

        private struct ComponentState
        {
            public bool Enabled;
            public bool UseInIndex;
        }

        public MassSourcesCreateUi UiSecuritiesSelection;

        public MassSourcesCreator Creator;

        private MassSourcesCreator GetCurrentCreator()
        {
            MassSourcesCreator creator = new MassSourcesCreator(_startProgram);

            if (Tabs.Count == 0)
            {
                return creator;
            }

            if (Tabs.Count > 0)
            {
                ConnectorCandles connector = Tabs[0];
                creator.ServerType = connector.ServerType;
                creator.ServerName = connector.ServerFullName;
                creator.TimeFrame = connector.TimeFrame;
                creator.EmulatorIsOn = connector.EmulatorIsOn;
                creator.SecuritiesClass = connector.SecurityClass;
                creator.PortfolioName = connector.PortfolioName;
                creator.SaveTradesInCandles = connector.SaveTradesInCandles;
                creator.MarketDepthBuildMaxSpread = connector.TimeFrameBuilder.MarketDepthBuildMaxSpread;
                creator.MarketDepthBuildMaxSpreadIsOn = connector.TimeFrameBuilder.MarketDepthBuildMaxSpreadIsOn;

                creator.CandleCreateMethodType = connector.CandleCreateMethodType;
                creator.CandleMarketDataType = connector.CandleMarketDataType;
                creator.CommissionType = connector.CommissionType;
                creator.CommissionValue = connector.CommissionValue;
                creator.CandleSeriesRealization.SetSaveString(connector.TimeFrameBuilder.CandleSeriesRealization.GetSaveString());
            }

            for (int i = 0; i < Tabs.Count; i++)
            {
                ConnectorCandles connector = Tabs[i];

                if (string.IsNullOrEmpty(connector.SecurityName))
                {
                    continue;
                }

                ActivatedSecurity activatedSecurity = new ActivatedSecurity
                {
                    SecurityName = connector.SecurityName,
                    SecurityClass = connector.SecurityClass,
                    IsOn = true
                };

                creator.SecuritiesNames.Add(activatedSecurity);
            }

            return creator;
        }

        private void TryRunSecurity(ActivatedSecurity security, MassSourcesCreator creator)
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                if (Tabs[i].SecurityName == security.SecurityName &&
                    Tabs[i].ServerType == creator.ServerType &&
                    Tabs[i].ServerFullName == creator.ServerName &&
                    Tabs[i].TimeFrame == creator.TimeFrame &&
                    Tabs[i].CandleMarketDataType == creator.CandleMarketDataType)
                {
                    return;
                }

                if (Tabs[i].SecurityName == security.SecurityName &&
                    Tabs[i].ServerType == creator.ServerType &&
                    (Tabs[i].TimeFrame != creator.TimeFrame ||
                     Tabs[i].CandleMarketDataType != creator.CandleMarketDataType))
                {
                    Tabs[i].Delete();
                    Tabs.RemoveAt(i);
                }
            }

            int num = Tabs.Count;

            while (true)
            {
                bool isNotInArray = true;

                for (int i = 0; i < Tabs.Count; i++)
                {
                    if (Tabs[i].UniqueName == TabName + num)
                    {
                        num++;
                        isNotInArray = false;
                        break;
                    }
                }

                if (isNotInArray)
                {
                    break;
                }
            }

            ConnectorCandles connector = new ConnectorCandles(TabName + num, _startProgram, false)
            {
                ServerType = creator.ServerType,
                ServerFullName = creator.ServerName,
                SecurityName = security.SecurityName,
                SecurityClass = security.SecurityClass,
                TimeFrame = creator.TimeFrame,
                EmulatorIsOn = creator.EmulatorIsOn,
                PortfolioName = creator.PortfolioName,
                SaveTradesInCandles = creator.SaveTradesInCandles,
                CandleMarketDataType = creator.CandleMarketDataType,
                CandleCreateMethodType = creator.CandleCreateMethodType,
                CommissionType = creator.CommissionType,
                CommissionValue = creator.CommissionValue
            };

            connector.TimeFrameBuilder.MarketDepthBuildMaxSpread = creator.MarketDepthBuildMaxSpread;
            connector.TimeFrameBuilder.MarketDepthBuildMaxSpreadIsOn = creator.MarketDepthBuildMaxSpreadIsOn;

            connector.TimeFrameBuilder.CandleSeriesRealization.SetSaveString(
                creator.CandleSeriesRealization.GetSaveString());

            Tabs.Add(connector);
            SyncComponents();
            ResetDataFeed();
        }

        #endregion

        #region Persistence

        public void Save()
        {
            try
            {
                SaveConnectors();
                SaveSettings();
            }
            catch
            {
                // ignore
            }
        }

        public void Load()
        {
            LoadConnectors();
            LoadSettings();
            SyncComponents();
        }

        private void SaveConnectors()
        {
            using (StreamWriter writer = new StreamWriter(GetConnectorFilePath(), false))
            {
                string save = string.Empty;
                for (int i = 0; i < Tabs.Count; i++)
                {
                    save += Tabs[i].UniqueName + "#";
                }

                writer.WriteLine(save);
            }
        }

        private void LoadConnectors()
        {
            if (!File.Exists(GetConnectorFilePath()))
            {
                return;
            }

            using (StreamReader reader = new StreamReader(GetConnectorFilePath()))
            {
                string line = reader.ReadLine();
                if (string.IsNullOrEmpty(line))
                {
                    return;
                }

                string[] saved = line.Split('#');
                for (int i = 0; i < saved.Length - 1; i++)
                {
                    ConnectorCandles newConnector = new ConnectorCandles(saved[i], _startProgram, false);
                    newConnector.SaveTradesInCandles = false;

                    if (newConnector.CandleMarketDataType != CandleMarketDataType.MarketDepth)
                    {
                        newConnector.NeedToLoadServerData = false;
                    }

                    Tabs.Add(newConnector);
                }
            }
        }

        private void SaveSettings()
        {
            using (StreamWriter writer = new StreamWriter(GetSettingsFilePath(), false))
            {
                writer.WriteLine(Settings.VolLookbackBars);
                writer.WriteLine(Settings.SigmaMin);
                writer.WriteLine(Settings.VolatilityMode);
                writer.WriteLine(Settings.EwmaLambda);
                writer.WriteLine(Settings.StalenessMode);
                writer.WriteLine(Settings.MaxStalenessBars);
                writer.WriteLine(Settings.MaxStalenessSeconds);
                writer.WriteLine(Settings.AutoStalenessMultiplier);
                writer.WriteLine(Settings.MinActiveComponents);
                writer.WriteLine(Settings.UpdateMode);
                writer.WriteLine(Settings.CalculationDepth);

                writer.WriteLine(Settings.Components.Count);

                foreach (IndexComponentSettings component in Settings.Components)
                {
                    writer.WriteLine(string.Join("|",
                        component.UniqueName,
                        component.Enabled,
                        component.UseInIndex,
                        component.SecurityName ?? string.Empty,
                        component.UsdDirection));
                }
            }
        }

        private void LoadSettings()
        {
            if (!File.Exists(GetSettingsFilePath()))
            {
                return;
            }

            using (StreamReader reader = new StreamReader(GetSettingsFilePath()))
            {
                Settings.VolLookbackBars = Convert.ToInt32(reader.ReadLine());
                Settings.SigmaMin = Convert.ToDecimal(reader.ReadLine());
                Enum.TryParse(reader.ReadLine(), out IndexVolatilityMode volMode);
                Settings.VolatilityMode = volMode;
                Settings.EwmaLambda = Convert.ToDecimal(reader.ReadLine());
                Enum.TryParse(reader.ReadLine(), out IndexStalenessMode stalenessMode);
                Settings.StalenessMode = stalenessMode;
                Settings.MaxStalenessBars = Convert.ToInt32(reader.ReadLine());
                Settings.MaxStalenessSeconds = Convert.ToInt32(reader.ReadLine());
                Settings.AutoStalenessMultiplier = Convert.ToDecimal(reader.ReadLine());
                Settings.MinActiveComponents = Convert.ToInt32(reader.ReadLine());
                Enum.TryParse(reader.ReadLine(), out IndexUpdateMode updateMode);
                Settings.UpdateMode = updateMode;
                Settings.CalculationDepth = Convert.ToInt32(reader.ReadLine());

                int count = Convert.ToInt32(reader.ReadLine());
                Settings.Components.Clear();

                for (int i = 0; i < count; i++)
                {
                    string line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    string[] parts = line.Split('|');
                    if (parts.Length < 5)
                    {
                        continue;
                    }

                    IndexComponentSettings component = new IndexComponentSettings
                    {
                        UniqueName = parts[0],
                        Enabled = Convert.ToBoolean(parts[1]),
                        UseInIndex = Convert.ToBoolean(parts[2]),
                        SecurityName = parts[3],
                        UsdDirection = Convert.ToInt32(parts[4])
                    };

                    component.UpdateSecurity(component.SecurityName);

                    Settings.Components.Add(component);
                }
            }
        }

        private string GetConnectorFilePath()
        {
            return $"Engine\\{TabName}CustomIndexSet.txt";
        }

        private string GetSettingsFilePath()
        {
            return $"Engine\\{TabName}CustomIndexSettings.txt";
        }

        #endregion

        #region Index Update

        private void EngineOnIndexUpdatedEvent(IndexSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            LastTimeCandleUpdate = snapshot.Time;
            LastSnapshot = snapshot;

            UpdateCandles(snapshot);

            _chartMaster.SetCandles(Candles);

            if (_startProgram == StartProgram.IsOsTrader)
            {
                Thread.Sleep(1);
            }

            _chartMaster.SetCandles(Candles);

            if (EventsIsOn && IndexChangeEvent != null)
            {
                IndexChangeEvent(Candles);
            }

            IndexUpdatedEvent?.Invoke(snapshot);
        }

        private void UpdateCandles(IndexSnapshot snapshot)
        {
            if (Candles.Count == 0)
            {
                Candle first = new Candle
                {
                    TimeStart = snapshot.Time,
                    Open = snapshot.IndexValue,
                    Close = snapshot.IndexValue,
                    High = snapshot.IndexValue,
                    Low = snapshot.IndexValue,
                    State = CandleState.Finished
                };

                Candles.Add(first);
                TrimCandles();
                return;
            }

            Candle last = Candles[Candles.Count - 1];

            if (last.TimeStart == snapshot.Time)
            {
                last.Close = snapshot.IndexValue;
                last.High = Math.Max(last.High, snapshot.IndexValue);
                last.Low = Math.Min(last.Low, snapshot.IndexValue);
                last.State = CandleState.Finished;
            }
            else
            {
                Candle next = new Candle
                {
                    TimeStart = snapshot.Time,
                    Open = last.Close,
                    Close = snapshot.IndexValue,
                    High = Math.Max(last.Close, snapshot.IndexValue),
                    Low = Math.Min(last.Close, snapshot.IndexValue),
                    State = CandleState.Finished
                };

                Candles.Add(next);
            }

            TrimCandles();
        }

        private void TrimCandles()
        {
            while (Candles.Count > Settings.CalculationDepth)
            {
                Candles.RemoveAt(0);
            }
        }

        #endregion

        #region Indicators

        public IIndicator CreateCandleIndicator(IIndicator indicator, string nameArea)
        {
            return _chartMaster.CreateIndicator(indicator, nameArea);
        }

        public void DeleteCandleIndicator(IIndicator indicator)
        {
            _chartMaster.DeleteIndicator(indicator);
        }

        public List<IIndicator> Indicators
        {
            get { return _chartMaster.Indicators; }
        }

        #endregion

        #region Log

        private void SendNewLogMessage(string message, LogMessageType type)
        {
            if (LogMessageEvent != null)
            {
                LogMessageEvent(message, type);
            }
            else if (type == LogMessageType.Error)
            {
                System.Windows.MessageBox.Show(message);
            }
        }

        #endregion

        private readonly StartProgram _startProgram;
        private readonly ChartCandleMaster _chartMaster;
        private readonly ConnectorIndexDataFeed _dataFeed;
        private readonly IndexEngine.IndexEngine _engine;
        private bool _eventsIsOn = true;
        private BotTabCustomIndexUi _ui;
    }
}
