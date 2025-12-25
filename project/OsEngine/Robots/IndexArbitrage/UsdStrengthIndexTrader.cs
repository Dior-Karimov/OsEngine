/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Servers;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Tab.IndexEngine;
using OsEngine.OsTrader.Panels.Tab.IndexEngine.Models;

namespace OsEngine.Robots.IndexArbitrage
{
    [Bot("UsdStrengthIndexTrader")]
    public class UsdStrengthIndexTrader : BotPanel
    {
        private readonly BotTabCustomIndex _indexTab;
        private readonly BotTabSimple _tradeTab;

        private readonly StrategyParameterString _regime;
        private readonly StrategyParameterString _tradeMode;
        private readonly StrategyParameterString _trendFilterMode;

        private readonly StrategyParameterInt _slopeLookback;
        private readonly StrategyParameterDecimal _slopeThreshold;
        private readonly StrategyParameterInt _atrLength;
        private readonly StrategyParameterDecimal _atrMultiplier;

        private readonly StrategyParameterInt _zLookback;
        private readonly StrategyParameterDecimal _zEntry;
        private readonly StrategyParameterDecimal _zExit;

        private readonly StrategyParameterString _degradedAction;
        private readonly StrategyParameterDecimal _degradedVolumeMultiplier;
        private readonly StrategyParameterString _badAction;
        private readonly StrategyParameterDecimal _badVolumeMultiplier;

        private readonly StrategyParameterTimeOfDay _tradeStart;
        private readonly StrategyParameterTimeOfDay _tradeEnd;
        private readonly StrategyParameterTimeOfDay _fridayEnd;
        private readonly StrategyParameterBool _tradeMonday;
        private readonly StrategyParameterBool _tradeTuesday;
        private readonly StrategyParameterBool _tradeWednesday;
        private readonly StrategyParameterBool _tradeThursday;
        private readonly StrategyParameterBool _tradeFriday;

        private readonly StrategyParameterString _volumeType;
        private readonly StrategyParameterDecimal _volume;
        private readonly StrategyParameterString _tradeAssetInPortfolio;

        private readonly StrategyParameterString _indexVolatilityMode;
        private readonly StrategyParameterInt _indexVolLookback;
        private readonly StrategyParameterDecimal _indexSigmaMin;
        private readonly StrategyParameterString _indexUpdateMode;
        private readonly StrategyParameterInt _indexMinActiveComponents;
        private readonly StrategyParameterString _indexStalenessMode;
        private readonly StrategyParameterDecimal _indexAutoStalenessMultiplier;
        private readonly StrategyParameterInt _indexMaxStalenessBars;
        private readonly StrategyParameterInt _indexMaxStalenessSeconds;

        private RollingStatistics _deviationStats;
        private Aindicator _atrIndicator;

        public UsdStrengthIndexTrader(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.CustomIndex);
            _indexTab = TabsCustomIndex[0];

            TabCreate(BotTabType.Simple);
            _tradeTab = TabsSimple[0];
            _tradeTab.CandleFinishedEvent += TradeTabOnCandleFinishedEvent;

            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
            _tradeMode = CreateParameter("Trade mode", "Auto", new[] { "Auto", "MeanReversion", "Trend" });
            _trendFilterMode = CreateParameter("Trend filter", "Slope", new[] { "Slope", "Atr" });

            _slopeLookback = CreateParameter("Slope lookback", 30, 5, 300, 5);
            _slopeThreshold = CreateParameter("Slope threshold", 0.005m, 0.0001m, 0.2m, 0.0001m);
            _atrLength = CreateParameter("Atr length", 14, 3, 200, 1);
            _atrMultiplier = CreateParameter("Atr multiplier", 1.5m, 0.1m, 10m, 0.1m);

            _zLookback = CreateParameter("Z lookback", 50, 10, 400, 10);
            _zEntry = CreateParameter("Z entry", 2.0m, 0.5m, 10m, 0.1m);
            _zExit = CreateParameter("Z exit", 0.5m, 0.1m, 5m, 0.1m);

            _degradedAction = CreateParameter("Degraded action", "ReduceVolume", new[] { "ReduceVolume", "CloseOnly" });
            _degradedVolumeMultiplier = CreateParameter("Degraded volume mult", 0.5m, 0.05m, 1m, 0.05m);
            _badAction = CreateParameter("Bad action", "ReduceVolume", new[] { "ReduceVolume", "CloseOnly" });
            _badVolumeMultiplier = CreateParameter("Bad volume mult", 0.25m, 0.01m, 1m, 0.05m);

            _tradeStart = CreateParameterTimeOfDay("Trade start", 10, 0, 0, 0);
            _tradeEnd = CreateParameterTimeOfDay("Trade end", 18, 0, 0, 0);
            _fridayEnd = CreateParameterTimeOfDay("Friday end", 17, 0, 0, 0);

            _tradeMonday = CreateParameter("Trade Monday", true);
            _tradeTuesday = CreateParameter("Trade Tuesday", true);
            _tradeWednesday = CreateParameter("Trade Wednesday", true);
            _tradeThursday = CreateParameter("Trade Thursday", true);
            _tradeFriday = CreateParameter("Trade Friday", true);

            _volumeType = CreateParameter("Volume type", "Deposit percent", new[] { "Contracts", "Contract currency", "Deposit percent" });
            _volume = CreateParameter("Volume", 10m, 0.1m, 50m, 0.1m);
            _tradeAssetInPortfolio = CreateParameter("Asset in portfolio", "Prime");

            _indexVolatilityMode = CreateParameter("Index volatility", IndexVolatilityMode.Std.ToString(), new[] { IndexVolatilityMode.Std.ToString(), IndexVolatilityMode.Ewma.ToString() });
            _indexVolLookback = CreateParameter("Index vol lookback", 120, 10, 500, 10);
            _indexSigmaMin = CreateParameter("Index sigma min", 0m, 0m, 0.1m, 0.0001m);
            _indexUpdateMode = CreateParameter("Index update", IndexUpdateMode.OnEveryUpdate.ToString(), new[] { IndexUpdateMode.OnEveryUpdate.ToString(), IndexUpdateMode.OnClosedCandle.ToString() });
            _indexMinActiveComponents = CreateParameter("Index min active", 3, 1, 20, 1);
            _indexStalenessMode = CreateParameter("Index staleness", IndexStalenessMode.Auto.ToString(), new[] { IndexStalenessMode.Auto.ToString(), IndexStalenessMode.Bars.ToString(), IndexStalenessMode.Seconds.ToString() });
            _indexAutoStalenessMultiplier = CreateParameter("Index staleness x", 2m, 1m, 10m, 0.1m);
            _indexMaxStalenessBars = CreateParameter("Index staleness bars", 3, 1, 30, 1);
            _indexMaxStalenessSeconds = CreateParameter("Index staleness sec", 30, 1, 600, 1);

            _deviationStats = new RollingStatistics(_zLookback.ValueInt);
            BuildAtrIndicator();
            ApplyIndexSettings();

            ParametrsChangeByUser += OnParametersChanged;

            Description = OsLocalization.Description.DescriptionLabel45;
        }

        public override string GetNameStrategyType()
        {
            return "UsdStrengthIndexTrader";
        }

        public override void ShowIndividualSettingsDialog()
        {
        }

        private void TradeTabOnCandleFinishedEvent(List<Candle> candles)
        {
            if (candles == null || candles.Count < 2)
            {
                return;
            }

            if (_regime.ValueString == "Off")
            {
                return;
            }

            if (_indexTab.Candles == null || _indexTab.Candles.Count < 2)
            {
                return;
            }

            DateTime time = candles[candles.Count - 1].TimeStart;

            if (IsTradeTime(time) == false)
            {
                CloseAllPositions("TimeFilter");
                return;
            }

            if (_indexTab.LastSnapshot == null)
            {
                return;
            }

            IndexQuality quality = _indexTab.LastSnapshot.Quality;

            if (quality == IndexQuality.Bad && _badAction.ValueString == "CloseOnly")
            {
                CloseAllPositions("IndexBad");
                return;
            }

            decimal deviation = CalculateDeviation(candles, _indexTab.Candles);

            _deviationStats.Add((double)deviation);

            if (_deviationStats.IsReady == false)
            {
                return;
            }

            double std = _deviationStats.StdDev;
            if (std <= 0)
            {
                return;
            }

            decimal zScore = (decimal)(((double)deviation - _deviationStats.Mean) / std);

            TradeBySignal(candles, zScore, quality);
        }

        private decimal CalculateDeviation(List<Candle> tradeCandles, List<Candle> indexCandles)
        {
            int lastTrade = tradeCandles.Count - 1;
            int lastIndex = indexCandles.Count - 1;

            decimal tradeReturn = (decimal)Math.Log((double)(tradeCandles[lastTrade].Close / tradeCandles[lastTrade - 1].Close));
            decimal indexReturn = (decimal)Math.Log((double)(indexCandles[lastIndex].Close / indexCandles[lastIndex - 1].Close));

            return tradeReturn - indexReturn;
        }

        private void TradeBySignal(List<Candle> tradeCandles, decimal zScore, IndexQuality quality)
        {
            List<Position> openPositions = _tradeTab.PositionsOpenAll;

            if (openPositions != null && openPositions.Count > 0)
            {
                Position position = openPositions[0];
                TryClosePosition(position, zScore);
                return;
            }

            if (_regime.ValueString == "OnlyClosePosition")
            {
                return;
            }

            if (quality == IndexQuality.Degraded && _degradedAction.ValueString == "CloseOnly")
            {
                return;
            }

            if (quality == IndexQuality.Bad && _badAction.ValueString == "CloseOnly")
            {
                return;
            }

            string mode = ResolveTradeMode();
            decimal volumeMultiplier = GetVolumeMultiplier(quality);

            if (volumeMultiplier <= 0)
            {
                return;
            }

            decimal volume = GetVolume(_tradeTab) * volumeMultiplier;

            if (volume <= 0)
            {
                return;
            }

            if (mode == "MeanReversion")
            {
                if (zScore >= _zEntry.ValueDecimal && _regime.ValueString != "OnlyLong")
                {
                    _tradeTab.SellAtMarket(volume, "IndexDeviationMR");
                }
                else if (zScore <= -_zEntry.ValueDecimal && _regime.ValueString != "OnlyShort")
                {
                    _tradeTab.BuyAtMarket(volume, "IndexDeviationMR");
                }
            }
            else
            {
                if (zScore >= _zEntry.ValueDecimal && _regime.ValueString != "OnlyShort")
                {
                    _tradeTab.BuyAtMarket(volume, "IndexDeviationTrend");
                }
                else if (zScore <= -_zEntry.ValueDecimal && _regime.ValueString != "OnlyLong")
                {
                    _tradeTab.SellAtMarket(volume, "IndexDeviationTrend");
                }
            }
        }

        private void TryClosePosition(Position position, decimal zScore)
        {
            if (position.State != PositionStateType.Open)
            {
                return;
            }

            if (Math.Abs(zScore) <= _zExit.ValueDecimal)
            {
                _tradeTab.CloseAtMarket(position, position.OpenVolume, "IndexDeviationExit");
            }
        }

        private void CloseAllPositions(string reason)
        {
            List<Position> openPositions = _tradeTab.PositionsOpenAll;

            if (openPositions == null || openPositions.Count == 0)
            {
                return;
            }

            for (int i = 0; i < openPositions.Count; i++)
            {
                Position position = openPositions[i];

                if (position.State == PositionStateType.Open)
                {
                    _tradeTab.CloseAtMarket(position, position.OpenVolume, reason);
                }
            }
        }

        private string ResolveTradeMode()
        {
            if (_tradeMode.ValueString != "Auto")
            {
                return _tradeMode.ValueString;
            }

            if (_trendFilterMode.ValueString == "Atr")
            {
                return IsTrendByAtr() ? "Trend" : "MeanReversion";
            }

            return IsTrendBySlope() ? "Trend" : "MeanReversion";
        }

        private bool IsTrendBySlope()
        {
            List<Candle> indexCandles = _indexTab.Candles;

            if (indexCandles == null || indexCandles.Count <= _slopeLookback.ValueInt)
            {
                return false;
            }

            int last = indexCandles.Count - 1;
            int first = last - _slopeLookback.ValueInt;

            decimal firstClose = indexCandles[first].Close;
            decimal lastClose = indexCandles[last].Close;

            if (firstClose == 0)
            {
                return false;
            }

            decimal slope = (lastClose - firstClose) / firstClose;

            return Math.Abs(slope) >= _slopeThreshold.ValueDecimal;
        }

        private bool IsTrendByAtr()
        {
            List<Candle> indexCandles = _indexTab.Candles;

            if (indexCandles == null || indexCandles.Count <= _atrLength.ValueInt)
            {
                return false;
            }

            if (_atrIndicator?.DataSeries == null || _atrIndicator.DataSeries.Count == 0)
            {
                return false;
            }

            List<decimal> atrValues = _atrIndicator.DataSeries[0].Values;

            if (atrValues == null || atrValues.Count == 0)
            {
                return false;
            }

            decimal atr = atrValues[atrValues.Count - 1];

            if (atr <= 0)
            {
                return false;
            }

            int last = indexCandles.Count - 1;
            int first = Math.Max(0, last - _slopeLookback.ValueInt);

            decimal move = Math.Abs(indexCandles[last].Close - indexCandles[first].Close);

            return move >= atr * _atrMultiplier.ValueDecimal;
        }

        private decimal GetVolumeMultiplier(IndexQuality quality)
        {
            if (quality == IndexQuality.Degraded)
            {
                if (_degradedAction.ValueString == "ReduceVolume")
                {
                    return _degradedVolumeMultiplier.ValueDecimal;
                }

                return 0m;
            }

            if (quality == IndexQuality.Bad)
            {
                if (_badAction.ValueString == "ReduceVolume")
                {
                    return _badVolumeMultiplier.ValueDecimal;
                }

                return 0m;
            }

            return 1m;
        }

        private bool IsTradeTime(DateTime time)
        {
            if (time.DayOfWeek == DayOfWeek.Monday && _tradeMonday.ValueBool == false)
            {
                return false;
            }
            if (time.DayOfWeek == DayOfWeek.Tuesday && _tradeTuesday.ValueBool == false)
            {
                return false;
            }
            if (time.DayOfWeek == DayOfWeek.Wednesday && _tradeWednesday.ValueBool == false)
            {
                return false;
            }
            if (time.DayOfWeek == DayOfWeek.Thursday && _tradeThursday.ValueBool == false)
            {
                return false;
            }
            if (time.DayOfWeek == DayOfWeek.Friday && _tradeFriday.ValueBool == false)
            {
                return false;
            }

            TimeSpan current = time.TimeOfDay;

            if (current < _tradeStart.TimeSpan || current > _tradeEnd.TimeSpan)
            {
                return false;
            }

            if (time.DayOfWeek == DayOfWeek.Friday && current > _fridayEnd.TimeSpan)
            {
                return false;
            }

            return true;
        }

        private void BuildAtrIndicator()
        {
            _atrIndicator = IndicatorsFactory.CreateIndicatorByName("ATR", NameStrategyUniq + "IndexAtr", false);
            _atrIndicator.ParametersDigit[0].Value = _atrLength.ValueInt;
            _atrIndicator = (Aindicator)_indexTab.CreateCandleIndicator(_atrIndicator, "IndexAtr");
            _atrIndicator.Save();
        }

        private void OnParametersChanged()
        {
            _deviationStats = new RollingStatistics(_zLookback.ValueInt);

            if (_atrIndicator != null && _atrIndicator.ParametersDigit[0].Value != _atrLength.ValueInt)
            {
                _atrIndicator.ParametersDigit[0].Value = _atrLength.ValueInt;
                _atrIndicator.Reload();
                _atrIndicator.Save();
            }

            ApplyIndexSettings();
        }

        private void ApplyIndexSettings()
        {
            if (Enum.TryParse(_indexVolatilityMode.ValueString, out IndexVolatilityMode volMode))
            {
                _indexTab.Settings.VolatilityMode = volMode;
            }

            _indexTab.Settings.VolLookbackBars = _indexVolLookback.ValueInt;
            _indexTab.Settings.SigmaMin = _indexSigmaMin.ValueDecimal;
            _indexTab.Settings.MinActiveComponents = _indexMinActiveComponents.ValueInt;

            if (Enum.TryParse(_indexUpdateMode.ValueString, out IndexUpdateMode updateMode))
            {
                _indexTab.Settings.UpdateMode = updateMode;
            }

            if (Enum.TryParse(_indexStalenessMode.ValueString, out IndexStalenessMode stalenessMode))
            {
                _indexTab.Settings.StalenessMode = stalenessMode;
            }

            _indexTab.Settings.AutoStalenessMultiplier = _indexAutoStalenessMultiplier.ValueDecimal;
            _indexTab.Settings.MaxStalenessBars = _indexMaxStalenessBars.ValueInt;
            _indexTab.Settings.MaxStalenessSeconds = _indexMaxStalenessSeconds.ValueInt;

            _indexTab.ApplySettings();
        }

        private decimal GetVolume(BotTabSimple tab)
        {
            decimal volume = 0;

            if (_volumeType.ValueString == "Contracts")
            {
                volume = _volume.ValueDecimal;
            }
            else if (_volumeType.ValueString == "Contract currency")
            {
                decimal contractPrice = tab.PriceBestAsk;
                volume = _volume.ValueDecimal / contractPrice;

                if (StartProgram == StartProgram.IsOsTrader)
                {
                    IServerPermission serverPermission = ServerMaster.GetServerPermission(tab.Connector.ServerType);

                    if (serverPermission != null &&
                        serverPermission.IsUseLotToCalculateProfit &&
                        tab.Security.Lot != 0 &&
                        tab.Security.Lot > 1)
                    {
                        volume = _volume.ValueDecimal / (contractPrice * tab.Security.Lot);
                    }

                    volume = Math.Round(volume, tab.Security.DecimalsVolume);
                }
                else
                {
                    volume = Math.Round(volume, 6);
                }
            }
            else if (_volumeType.ValueString == "Deposit percent")
            {
                Portfolio portfolio = tab.Portfolio;

                if (portfolio == null)
                {
                    return 0;
                }

                decimal portfolioPrimeAsset = 0;

                if (_tradeAssetInPortfolio.ValueString == "Prime")
                {
                    portfolioPrimeAsset = portfolio.ValueCurrent;
                }
                else
                {
                    List<PositionOnBoard> positionsOnBoard = portfolio.GetPositionOnBoard();

                    if (positionsOnBoard == null)
                    {
                        return 0;
                    }

                    for (int i = 0; i < positionsOnBoard.Count; i++)
                    {
                        if (positionsOnBoard[i].SecurityNameCode == _tradeAssetInPortfolio.ValueString)
                        {
                            portfolioPrimeAsset = positionsOnBoard[i].ValueCurrent;
                            break;
                        }
                    }
                }

                if (portfolioPrimeAsset == 0)
                {
                    SendNewLogMessage("Can`t find portfolio " + _tradeAssetInPortfolio.ValueString, LogMessageType.Error);
                    return 0;
                }

                decimal moneyOnPosition = portfolioPrimeAsset / 100 * _volume.ValueDecimal;
                decimal contractPrice = tab.PriceBestAsk;

                if (contractPrice == 0)
                {
                    return 0;
                }

                if (StartProgram == StartProgram.IsOsTrader)
                {
                    IServerPermission serverPermission = ServerMaster.GetServerPermission(tab.Connector.ServerType);

                    if (serverPermission != null &&
                        serverPermission.IsUseLotToCalculateProfit &&
                        tab.Security.Lot != 0 &&
                        tab.Security.Lot > 1)
                    {
                        volume = moneyOnPosition / (contractPrice * tab.Security.Lot);
                    }
                    else
                    {
                        volume = moneyOnPosition / contractPrice;
                    }

                    volume = Math.Round(volume, tab.Security.DecimalsVolume);
                }
                else
                {
                    volume = Math.Round(moneyOnPosition / contractPrice, 6);
                }
            }

            return volume;
        }
    }
}
