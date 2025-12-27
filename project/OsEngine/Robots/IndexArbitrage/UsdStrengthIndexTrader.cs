/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using System.Linq;
using OsEngine.Entity;
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
        private readonly BotTabScreener _screener;

        private readonly StrategyParameterString _regime;
        private readonly StrategyParameterInt _maxPositions;
        private readonly StrategyParameterInt _zLookback;
        private readonly StrategyParameterDecimal _zEntry;
        private readonly StrategyParameterDecimal _zExit;
        private readonly StrategyParameterDecimal _scoreSpreadWeight;
        private readonly StrategyParameterDecimal _scoreVolWeight;
        private readonly StrategyParameterDecimal _stopLossPercent;
        private readonly StrategyParameterDecimal _takeProfitPercent;
        private readonly StrategyParameterInt _maxHoldMinutes;

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

        private readonly Dictionary<string, RollingStatistics> _deviationStatsBySecurity = new Dictionary<string, RollingStatistics>();
        private DateTime _lastIndexTime = DateTime.MinValue;

        public UsdStrengthIndexTrader(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.CustomIndex);
            _indexTab = TabsCustomIndex[0];
            _indexTab.IndexUpdatedEvent += IndexTabOnIndexUpdatedEvent;

            TabCreate(BotTabType.Screener);
            _screener = TabsScreener[0];
            _screener.CandleFinishedEvent += ScreenerOnCandleFinishedEvent;

            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" });
            _maxPositions = CreateParameter("Max positions", 5, 1, 50, 1);
            _zLookback = CreateParameter("Z lookback", 60, 10, 400, 10);
            _zEntry = CreateParameter("Z entry", 2.0m, 0.5m, 10m, 0.1m);
            _zExit = CreateParameter("Z exit", 0.5m, 0.1m, 5m, 0.1m);
            _scoreSpreadWeight = CreateParameter("Score spread weight", 1.0m, 0m, 10m, 0.1m);
            _scoreVolWeight = CreateParameter("Score vol weight", 1.0m, 0m, 10m, 0.1m);
            _stopLossPercent = CreateParameter("Stop loss %", 0.5m, 0.05m, 5m, 0.05m);
            _takeProfitPercent = CreateParameter("Take profit %", 0.8m, 0.05m, 10m, 0.05m);
            _maxHoldMinutes = CreateParameter("Max hold minutes", 30, 1, 600, 5);

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

        private void IndexTabOnIndexUpdatedEvent(IndexSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            _lastIndexTime = snapshot.Time;
            EvaluateSignals();
        }

        private void ScreenerOnCandleFinishedEvent(List<Candle> candles, BotTabSimple tab)
        {
            if (candles == null || candles.Count < 2)
            {
                return;
            }

            EvaluateClose(tab, candles);
        }

        private void EvaluateSignals()
        {
            if (_regime.ValueString == "Off")
            {
                return;
            }

            if (_indexTab.Candles == null || _indexTab.Candles.Count < 2)
            {
                return;
            }

            if (IsTradeTime(_lastIndexTime) == false)
            {
                CloseAllPositions("TimeFilter");
                return;
            }

            if (_indexTab.LastSnapshot == null || _indexTab.LastSnapshot.Quality != IndexQuality.Good)
            {
                return;
            }

            if (_screener.Tabs == null || _screener.Tabs.Count == 0)
            {
                return;
            }

            List<SignalCandidate> candidates = new List<SignalCandidate>();

            for (int i = 0; i < _screener.Tabs.Count; i++)
            {
                BotTabSimple tab = _screener.Tabs[i];

                if (tab.IsConnected == false || tab.IsReadyToTrade == false)
                {
                    continue;
                }

                if (tab.PositionsOpenAll.Count > 0)
                {
                    continue;
                }

                List<Candle> tradeCandles = tab.CandlesFinishedOnly;

                if (tradeCandles == null || tradeCandles.Count < 2)
                {
                    continue;
                }

                Candle lastTrade = tradeCandles[tradeCandles.Count - 1];
                Candle lastIndex = _indexTab.Candles[_indexTab.Candles.Count - 1];

                if (lastTrade.TimeStart != lastIndex.TimeStart)
                {
                    continue;
                }

                decimal deviation = CalculateDeviation(tradeCandles, _indexTab.Candles);

                RollingStatistics stats = GetDeviationStats(tab.Security.Name);
                stats.Add((double)deviation);

                if (stats.IsReady == false || stats.StdDev <= 0)
                {
                    continue;
                }

                decimal zScore = (decimal)(((double)deviation - stats.Mean) / stats.StdDev);

                if (Math.Abs(zScore) < _zEntry.ValueDecimal)
                {
                    continue;
                }

                decimal score = CalculateScore(tab, zScore, (decimal)stats.StdDev);

                candidates.Add(new SignalCandidate
                {
                    Tab = tab,
                    ZScore = zScore,
                    Score = score
                });
            }

            if (candidates.Count == 0)
            {
                return;
            }

            List<SignalCandidate> best = candidates
                .OrderByDescending(c => c.Score)
                .Take(_maxPositions.ValueInt)
                .ToList();

            for (int i = 0; i < best.Count; i++)
            {
                OpenPosition(best[i]);
            }
        }

        private void EvaluateClose(BotTabSimple tab, List<Candle> candles)
        {
            if (tab.PositionsOpenAll.Count == 0)
            {
                return;
            }

            if (_regime.ValueString == "Off")
            {
                ClosePosition(tab, "RegimeOff");
                return;
            }

            List<Position> positions = tab.PositionsOpenAll;
            if (positions.Count == 0)
            {
                return;
            }

            DateTime lastTradeTime = candles[candles.Count - 1].TimeStart;

            for (int i = 0; i < positions.Count; i++)
            {
                Position position = positions[i];
                if (position.State != PositionStateType.Open)
                {
                    continue;
                }

                if (ShouldExitByRisk(position, tab, lastTradeTime))
                {
                    tab.CloseAtMarket(position, position.OpenVolume, "RiskExit");
                }
            }

            if (_indexTab.Candles == null || _indexTab.Candles.Count < 2)
            {
                return;
            }

            if (_indexTab.LastSnapshot == null || _indexTab.LastSnapshot.Quality != IndexQuality.Good)
            {
                return;
            }

            Candle lastIndex = _indexTab.Candles[_indexTab.Candles.Count - 1];
            Candle lastTrade = candles[candles.Count - 1];

            if (lastIndex.TimeStart != lastTrade.TimeStart)
            {
                return;
            }

            decimal deviation = CalculateDeviation(candles, _indexTab.Candles);
            RollingStatistics stats = GetDeviationStats(tab.Security.Name);
            stats.Add((double)deviation);

            if (stats.IsReady == false || stats.StdDev <= 0)
            {
                return;
            }

            decimal zScore = (decimal)(((double)deviation - stats.Mean) / stats.StdDev);

            for (int i = 0; i < positions.Count; i++)
            {
                Position position = positions[i];
                if (position.State != PositionStateType.Open)
                {
                    continue;
                }

                if (Math.Abs(zScore) <= _zExit.ValueDecimal)
                {
                    tab.CloseAtMarket(position, position.OpenVolume, "IndexDeviationExit");
                }
            }
        }

        private void OpenPosition(SignalCandidate candidate)
        {
            BotTabSimple tab = candidate.Tab;

            if (tab.PositionsOpenAll.Count > 0)
            {
                return;
            }

            if (_regime.ValueString == "OnlyClosePosition")
            {
                return;
            }

            decimal volume = GetVolume(tab);

            if (volume <= 0)
            {
                return;
            }

            if (candidate.ZScore >= _zEntry.ValueDecimal && _regime.ValueString != "OnlyLong")
            {
                tab.SellAtMarket(volume, "IndexDeviation");
            }
            else if (candidate.ZScore <= -_zEntry.ValueDecimal && _regime.ValueString != "OnlyShort")
            {
                tab.BuyAtMarket(volume, "IndexDeviation");
            }
        }

        private void CloseAllPositions(string reason)
        {
            if (_screener.Tabs == null)
            {
                return;
            }

            for (int i = 0; i < _screener.Tabs.Count; i++)
            {
                ClosePosition(_screener.Tabs[i], reason);
            }
        }

        private void ClosePosition(BotTabSimple tab, string reason)
        {
            List<Position> openPositions = tab.PositionsOpenAll;

            if (openPositions == null || openPositions.Count == 0)
            {
                return;
            }

            for (int i = 0; i < openPositions.Count; i++)
            {
                Position position = openPositions[i];

                if (position.State == PositionStateType.Open)
                {
                    tab.CloseAtMarket(position, position.OpenVolume, reason);
                }
            }
        }

        private decimal CalculateDeviation(List<Candle> tradeCandles, List<Candle> indexCandles)
        {
            int lastTrade = tradeCandles.Count - 1;
            int lastIndex = indexCandles.Count - 1;

            decimal tradeReturn = (decimal)Math.Log((double)(tradeCandles[lastTrade].Close / tradeCandles[lastTrade - 1].Close));
            decimal indexReturn = (decimal)Math.Log((double)(indexCandles[lastIndex].Close / indexCandles[lastIndex - 1].Close));

            return tradeReturn - indexReturn;
        }

        private decimal CalculateScore(BotTabSimple tab, decimal zScore, decimal deviationStd)
        {
            decimal spreadPenalty = 0m;

            if (tab.PriceBestAsk > 0)
            {
                spreadPenalty = (tab.PriceBestAsk - tab.PriceBestBid) / tab.PriceBestAsk;
            }

            decimal score = Math.Abs(zScore);
            score -= spreadPenalty * _scoreSpreadWeight.ValueDecimal;
            score -= deviationStd * _scoreVolWeight.ValueDecimal;

            return score;
        }

        private bool ShouldExitByRisk(Position position, BotTabSimple tab, DateTime now)
        {
            decimal entry = position.EntryPrice;

            if (entry <= 0)
            {
                return false;
            }

            decimal currentPrice = position.Direction == Side.Buy ? tab.PriceBestBid : tab.PriceBestAsk;

            if (currentPrice <= 0)
            {
                return false;
            }

            decimal changePercent = (currentPrice - entry) / entry * 100m;

            if (position.Direction == Side.Sell)
            {
                changePercent = -changePercent;
            }

            if (changePercent <= -_stopLossPercent.ValueDecimal)
            {
                return true;
            }

            if (changePercent >= _takeProfitPercent.ValueDecimal)
            {
                return true;
            }

            if (_maxHoldMinutes.ValueInt > 0)
            {
                TimeSpan hold = now - position.TimeOpen;
                if (hold.TotalMinutes >= _maxHoldMinutes.ValueInt)
                {
                    return true;
                }
            }

            return false;
        }

        private RollingStatistics GetDeviationStats(string securityName)
        {
            if (_deviationStatsBySecurity.TryGetValue(securityName, out RollingStatistics stats))
            {
                return stats;
            }

            stats = new RollingStatistics(_zLookback.ValueInt);
            _deviationStatsBySecurity[securityName] = stats;
            return stats;
        }

        private void OnParametersChanged()
        {
            _deviationStatsBySecurity.Clear();
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

        private class SignalCandidate
        {
            public BotTabSimple Tab;
            public decimal ZScore;
            public decimal Score;
        }
    }
}
