/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

/* Description
USD strength adaptive mean reversion scalper.

Trades components against the USD strength index with correlation/cointegration,
includes momentum filter, partial take-profit, and trailing stop for the runner.
*/

namespace OsEngine.Robots.IndexArbitrage
{
    [Bot("UsdStrengthAdaptiveScalper")]
    public class UsdStrengthAdaptiveScalper : BotPanel
    {
        private BotTabIndex _index;
        private BotTabScreener _screener;

        private StrategyParameterString _regime;
        private StrategyParameterInt _maxPositionsCount;
        private StrategyParameterDecimal _moneyPercentFromDepoOnPosition;
        private StrategyParameterDecimal _slippagePercent;

        private StrategyParameterString _tradeAssetInPortfolio;

        private StrategyParameterInt _cointegrationCandlesLookBack;
        private StrategyParameterDecimal _cointegrationStandartDeviationMult;

        private StrategyParameterDecimal _correlationMinValue;
        private StrategyParameterInt _correlationCandlesLookBack;

        private StrategyParameterInt _indexMomentumLookback;
        private StrategyParameterDecimal _indexMomentumMaxPercent;

        private StrategyParameterDecimal _takeProfitFirstPercent;
        private StrategyParameterDecimal _takeProfitSecondPercent;
        private StrategyParameterDecimal _partialClosePercent;
        private StrategyParameterDecimal _stopLossPercent;
        private StrategyParameterDecimal _trailingStopPercent;
        private StrategyParameterInt _closeRetrySeconds;

        private readonly Dictionary<int, decimal> _maxFavorablePrice = new Dictionary<int, decimal>();
        private readonly Dictionary<int, decimal> _minFavorablePrice = new Dictionary<int, decimal>();
        private readonly HashSet<int> _tp1Done = new HashSet<int>();
        private readonly Dictionary<int, DateTime> _lastCloseAttemptTime = new Dictionary<int, DateTime>();

        public UsdStrengthAdaptiveScalper(string name, StartProgram startProgram)
            : base(name, startProgram)
        {
            TabCreate(BotTabType.Index);
            _index = TabsIndex[0];
            _index.SpreadChangeEvent += _index_SpreadChangeEvent;

            TabCreate(BotTabType.Screener);
            _screener = TabsScreener[0];
            _screener.CandleFinishedEvent += _screener_CandleFinishedEvent;

            _regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort" });
            _maxPositionsCount = CreateParameter("Max poses count", 3, 1, 50, 4);
            _slippagePercent = CreateParameter("Slippage percent", 0.1m, 0.1m, 5, 0.1m);
            _moneyPercentFromDepoOnPosition = CreateParameter("Percent depo on position", 25m, 0.1m, 50, 0.1m);

            _tradeAssetInPortfolio = CreateParameter("Asset in portfolio", "Prime");

            _cointegrationCandlesLookBack = CreateParameter("Cointegration candles look back", 100, 1, 500, 10);
            _cointegrationStandartDeviationMult = CreateParameter("Deviation mult", 1m, 0.1m, 10, 0.1m);

            _correlationMinValue = CreateParameter("Correlation min value", 0.8m, 0.1m, 1, 0.05m);
            _correlationCandlesLookBack = CreateParameter("Correlation candles look back", 100, 10, 500, 10);

            _indexMomentumLookback = CreateParameter("Index momentum lookback", 10, 2, 200, 1);
            _indexMomentumMaxPercent = CreateParameter("Index momentum max %", 0.6m, 0.1m, 5, 0.1m);

            _takeProfitFirstPercent = CreateParameter("Take profit 1 %", 0.4m, 0.1m, 5, 0.1m);
            _takeProfitSecondPercent = CreateParameter("Take profit 2 %", 1.0m, 0.1m, 10, 0.1m);
            _partialClosePercent = CreateParameter("Partial close %", 50m, 10m, 90m, 5m);
            _stopLossPercent = CreateParameter("Stop loss %", 0.6m, 0.1m, 5, 0.1m);
            _trailingStopPercent = CreateParameter("Trailing stop %", 0.4m, 0.1m, 5, 0.1m);
            _closeRetrySeconds = CreateParameter("Close retry seconds", 10, 1, 120, 1);

            Description = OsLocalization.Description.DescriptionLabel47;
        }

        public override string GetNameStrategyType()
        {
            return "UsdStrengthAdaptiveScalper";
        }

        public override void ShowIndividualSettingsDialog()
        {

        }

        private void _index_SpreadChangeEvent(List<Candle> index)
        {
            if (_regime.ValueString == "Off")
            {
                return;
            }

            if (_screener.Tabs.Count == 0)
            {
                SendNewLogMessage("Screener tab is empty", Logging.LogMessageType.Error);
                return;
            }

            for (int i = 0; i < _screener.Tabs.Count; i++)
            {
                if (_screener.PositionsOpenAll.Count >= _maxPositionsCount.ValueInt)
                {
                    return;
                }

                LogicOpenPositions(index, _screener.Tabs[i]);
            }
        }

        private void LogicOpenPositions(List<Candle> candlesIndex, BotTabSimple tab)
        {
            if (tab.IsConnected == false || tab.IsReadyToTrade == false)
            {
                return;
            }

            if (tab.PositionsOpenAll.Count > 0)
            {
                return;
            }

            if (candlesIndex == null || candlesIndex.Count < _indexMomentumLookback.ValueInt + 1)
            {
                return;
            }

            if (IsIndexMomentumTooHigh(candlesIndex))
            {
                return;
            }

            List<Candle> candlesSecurity = tab.CandlesFinishedOnly;

            if (candlesSecurity == null || candlesSecurity.Count == 0)
            {
                return;
            }

            CorrelationBuilder correlationIndicator = new CorrelationBuilder();
            PairIndicatorValue correlation =
                correlationIndicator.ReloadCorrelationLast(candlesIndex, candlesSecurity, _correlationCandlesLookBack.ValueInt);

            if (correlation == null || correlation.Value < _correlationMinValue.ValueDecimal)
            {
                return;
            }

            CointegrationBuilder cointegrationIndicator = new CointegrationBuilder();
            cointegrationIndicator.CointegrationLookBack = _cointegrationCandlesLookBack.ValueInt;
            cointegrationIndicator.CointegrationDeviation = _cointegrationStandartDeviationMult.ValueDecimal;
            cointegrationIndicator.ReloadCointegration(candlesIndex, candlesSecurity, false);

            if (cointegrationIndicator.Cointegration == null || cointegrationIndicator.Cointegration.Count == 0)
            {
                return;
            }

            if (cointegrationIndicator.SideCointegrationValue == CointegrationLineSide.Up
                && _regime.ValueString != "OnlyLong")
            {
                SellSecurity(tab, CointegrationLineSide.Up.ToString());
            }

            if (cointegrationIndicator.SideCointegrationValue == CointegrationLineSide.Down
                && _regime.ValueString != "OnlyShort")
            {
                BuySecurity(tab, CointegrationLineSide.Down.ToString());
            }
        }

        private bool IsIndexMomentumTooHigh(List<Candle> candlesIndex)
        {
            int lookback = _indexMomentumLookback.ValueInt;
            int lastIndex = candlesIndex.Count - 1;
            int startIndex = lastIndex - lookback;

            if (startIndex < 0)
            {
                return false;
            }

            decimal startClose = candlesIndex[startIndex].Close;
            decimal lastClose = candlesIndex[lastIndex].Close;

            if (startClose == 0)
            {
                return false;
            }

            decimal movePercent = Math.Abs((lastClose - startClose) / (startClose / 100));

            return movePercent > _indexMomentumMaxPercent.ValueDecimal;
        }

        private void BuySecurity(BotTabSimple tab, string signal)
        {
            decimal price = tab.PriceBestAsk;

            if (_slippagePercent.ValueDecimal != 0)
            {
                price = price + price * (_slippagePercent.ValueDecimal / 100);
                price = Math.Round(price, tab.Security.Decimals);
            }

            decimal volume = GetVolume(tab);

            if (price == 0 || volume == 0)
            {
                return;
            }

            tab.BuyAtLimit(volume, price, signal);
        }

        private void SellSecurity(BotTabSimple tab, string signal)
        {
            decimal price = tab.PriceBestBid;

            if (_slippagePercent.ValueDecimal != 0)
            {
                price = price - price * (_slippagePercent.ValueDecimal / 100);
                price = Math.Round(price, tab.Security.Decimals);
            }

            decimal volume = GetVolume(tab);

            if (price == 0 || volume == 0)
            {
                return;
            }

            tab.SellAtLimit(volume, price, signal);
        }

        private void _screener_CandleFinishedEvent(List<Candle> candlesSecurity, BotTabSimple tab)
        {
            if (tab.IsConnected == false || tab.IsReadyToTrade == false)
            {
                return;
            }

            if (_regime.ValueString == "Off")
            {
                return;
            }

            List<Position> positions = tab.PositionsOpenAll;

            if (positions == null || positions.Count == 0)
            {
                return;
            }

            for (int i = positions.Count - 1; i >= 0; i--)
            {
                Position pos = positions[i];

                if (pos == null)
                {
                    continue;
                }

                if (pos.State == PositionStateType.Closing)
                {
                    EnsureClose(tab, pos);
                    continue;
                }

                if (pos.State != PositionStateType.Open)
                {
                    CleanupPosition(pos);
                    continue;
                }

                ManagePosition(tab, pos, candlesSecurity);
            }
        }

        private void ManagePosition(BotTabSimple tab, Position pos, List<Candle> candlesSecurity)
        {
            decimal currentPrice = pos.Direction == Side.Buy ? tab.PriceBestBid : tab.PriceBestAsk;

            if (currentPrice == 0 || pos.EntryPrice == 0)
            {
                return;
            }

            decimal profitPercent = GetProfitPercent(pos, currentPrice);

            if (profitPercent <= -_stopLossPercent.ValueDecimal)
            {
                ClosePosition(tab, pos);
                CleanupPosition(pos);
                return;
            }

            bool tp1Done = _tp1Done.Contains(pos.Number);

            if (tp1Done == false && profitPercent >= _takeProfitFirstPercent.ValueDecimal)
            {
                decimal partialVolume = pos.OpenVolume * (_partialClosePercent.ValueDecimal / 100);
                partialVolume = Math.Round(partialVolume, tab.Security.DecimalsVolume);

                if (partialVolume > 0)
                {
                    ClosePosition(tab, pos, partialVolume);
                    _tp1Done.Add(pos.Number);
                }
            }

            UpdateFavorablePrice(pos, currentPrice);

            if (_tp1Done.Contains(pos.Number))
            {
                if (_takeProfitSecondPercent.ValueDecimal > 0
                    && profitPercent >= _takeProfitSecondPercent.ValueDecimal)
                {
                    ClosePosition(tab, pos);
                    CleanupPosition(pos);
                    return;
                }

                if (_trailingStopPercent.ValueDecimal > 0
                    && IsTrailingStopHit(pos, currentPrice))
                {
                    ClosePosition(tab, pos);
                    CleanupPosition(pos);
                    return;
                }
            }

            if (ShouldCloseBySignal(candlesSecurity, pos))
            {
                ClosePosition(tab, pos);
                CleanupPosition(pos);
            }
        }

        private bool ShouldCloseBySignal(List<Candle> candlesSecurity, Position pos)
        {
            List<Candle> candlesIndex = _index.Candles;

            if (candlesIndex == null || candlesIndex.Count == 0)
            {
                return false;
            }

            CointegrationBuilder cointegrationIndicator = new CointegrationBuilder();
            cointegrationIndicator.CointegrationLookBack = _cointegrationCandlesLookBack.ValueInt;
            cointegrationIndicator.CointegrationDeviation = _cointegrationStandartDeviationMult.ValueDecimal;
            cointegrationIndicator.ReloadCointegration(candlesIndex, candlesSecurity, false);

            if (cointegrationIndicator.Cointegration == null || cointegrationIndicator.Cointegration.Count == 0)
            {
                return false;
            }

            if (pos.Direction == Side.Buy
                && cointegrationIndicator.SideCointegrationValue == CointegrationLineSide.Up)
            {
                return true;
            }

            if (pos.Direction == Side.Sell
                && cointegrationIndicator.SideCointegrationValue == CointegrationLineSide.Down)
            {
                return true;
            }

            return false;
        }

        private decimal GetProfitPercent(Position pos, decimal currentPrice)
        {
            if (pos.Direction == Side.Buy)
            {
                return (currentPrice - pos.EntryPrice) / (pos.EntryPrice / 100);
            }

            return (pos.EntryPrice - currentPrice) / (pos.EntryPrice / 100);
        }

        private void UpdateFavorablePrice(Position pos, decimal currentPrice)
        {
            if (pos.Direction == Side.Buy)
            {
                if (_maxFavorablePrice.TryGetValue(pos.Number, out decimal maxPrice))
                {
                    if (currentPrice > maxPrice)
                    {
                        _maxFavorablePrice[pos.Number] = currentPrice;
                    }
                }
                else
                {
                    _maxFavorablePrice[pos.Number] = currentPrice;
                }
            }
            else
            {
                if (_minFavorablePrice.TryGetValue(pos.Number, out decimal minPrice))
                {
                    if (currentPrice < minPrice)
                    {
                        _minFavorablePrice[pos.Number] = currentPrice;
                    }
                }
                else
                {
                    _minFavorablePrice[pos.Number] = currentPrice;
                }
            }
        }

        private bool IsTrailingStopHit(Position pos, decimal currentPrice)
        {
            decimal trailingPercent = _trailingStopPercent.ValueDecimal / 100;

            if (pos.Direction == Side.Buy)
            {
                if (_maxFavorablePrice.TryGetValue(pos.Number, out decimal maxPrice) == false)
                {
                    return false;
                }

                decimal stopPrice = maxPrice * (1 - trailingPercent);
                return currentPrice <= stopPrice;
            }

            if (_minFavorablePrice.TryGetValue(pos.Number, out decimal minPrice) == false)
            {
                return false;
            }

            decimal stopPriceShort = minPrice * (1 + trailingPercent);
            return currentPrice >= stopPriceShort;
        }

        private void ClosePosition(BotTabSimple tab, Position pos)
        {
            ClosePosition(tab, pos, pos.OpenVolume);
        }

        private void ClosePosition(BotTabSimple tab, Position pos, decimal volume)
        {
            if (pos.State != PositionStateType.Open)
            {
                return;
            }

            if (volume <= 0)
            {
                return;
            }

            RegisterCloseAttempt(pos, tab);
            tab.CloseAtMarket(pos, volume);
        }

        private void EnsureClose(BotTabSimple tab, Position pos)
        {
            if (pos == null)
            {
                return;
            }

            if (pos.State != PositionStateType.Closing)
            {
                return;
            }

            if (ShouldRetryClose(tab, pos) == false)
            {
                return;
            }

            if (pos.CloseActive)
            {
                tab.CloseAllOrderToPosition(pos);
            }

            if (pos.CloseActive)
            {
                return;
            }

            if (pos.OpenVolume <= 0)
            {
                CleanupPosition(pos);
                return;
            }

            RegisterCloseAttempt(pos, tab);
            tab.CloseAtMarket(pos, pos.OpenVolume);
        }

        private bool ShouldRetryClose(BotTabSimple tab, Position pos)
        {
            if (pos == null)
            {
                return false;
            }

            DateTime nowTime = tab.TimeServerCurrent;

            if (nowTime == DateTime.MinValue)
            {
                nowTime = DateTime.Now;
            }

            if (_lastCloseAttemptTime.TryGetValue(pos.Number, out DateTime lastAttempt))
            {
                return (nowTime - lastAttempt).TotalSeconds >= _closeRetrySeconds.ValueInt;
            }

            return true;
        }

        private void RegisterCloseAttempt(Position pos, BotTabSimple tab)
        {
            DateTime nowTime = tab.TimeServerCurrent;

            if (nowTime == DateTime.MinValue)
            {
                nowTime = DateTime.Now;
            }

            _lastCloseAttemptTime[pos.Number] = nowTime;
        }

        private void CleanupPosition(Position pos)
        {
            if (pos == null)
            {
                return;
            }

            _tp1Done.Remove(pos.Number);
            _maxFavorablePrice.Remove(pos.Number);
            _minFavorablePrice.Remove(pos.Number);
            _lastCloseAttemptTime.Remove(pos.Number);
        }

        private decimal GetVolume(BotTabSimple tab)
        {
            Portfolio myPortfolio = tab.Portfolio;

            if (myPortfolio == null)
            {
                return 0;
            }

            decimal portfolioPrimeAsset = 0;

            if (_tradeAssetInPortfolio.ValueString == "Prime")
            {
                portfolioPrimeAsset = myPortfolio.ValueCurrent;
            }
            else
            {
                List<PositionOnBoard> positionOnBoard = myPortfolio.GetPositionOnBoard();

                if (positionOnBoard == null)
                {
                    return 0;
                }

                for (int i = 0; i < positionOnBoard.Count; i++)
                {
                    if (positionOnBoard[i].SecurityNameCode == _tradeAssetInPortfolio.ValueString)
                    {
                        portfolioPrimeAsset = positionOnBoard[i].ValueCurrent;
                        break;
                    }
                }
            }

            if (portfolioPrimeAsset == 0)
            {
                SendNewLogMessage("Can`t found portfolio " + _tradeAssetInPortfolio.ValueString, Logging.LogMessageType.Error);
                return 0;
            }

            decimal moneyOnPosition = portfolioPrimeAsset * (_moneyPercentFromDepoOnPosition.ValueDecimal / 100);

            decimal qty = moneyOnPosition / tab.PriceBestAsk;

            if (tab.StartProgram == StartProgram.IsOsTrader)
            {
                qty = Math.Round(qty, tab.Security.DecimalsVolume);
            }
            else
            {
                qty = Math.Round(qty, 7);
            }

            return qty;
        }
    }
}
