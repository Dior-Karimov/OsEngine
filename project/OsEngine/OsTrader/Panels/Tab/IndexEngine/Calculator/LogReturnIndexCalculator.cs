/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;
using System.Collections.Generic;
using OsEngine.OsTrader.Panels.Tab.IndexEngine.DataFeed;
using OsEngine.OsTrader.Panels.Tab.IndexEngine.Models;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.Calculator
{
    public class LogReturnIndexCalculator : IIndexCalculator
    {
        public LogReturnIndexCalculator(IndexSettings settings)
        {
            _settings = settings;
            _weightsModel = new InverseVolWeightsModel();
            BuildVolatilityModel();
        }

        public void Reset()
        {
            BuildVolatilityModel();
        }

        public IndexSnapshot Update(IndexDataFrame frame, IndexSettings settings, IndexState state)
        {
            if (frame == null || frame.Components.Count == 0)
            {
                return null;
            }

            if (settings.UpdateMode == IndexUpdateMode.OnClosedCandle && FrameHasClosedCandles(frame, settings) == false)
            {
                return null;
            }

            Dictionary<string, IndexComponentFrame> framesByName = new Dictionary<string, IndexComponentFrame>();

            for (int i = 0; i < frame.Components.Count; i++)
            {
                IndexComponentFrame componentFrame = frame.Components[i];
                framesByName[componentFrame.UniqueName] = componentFrame;
            }

            int enabledCount = 0;
            int activeEnabledCount = 0;
            int activeIndexCount = 0;

            Dictionary<string, double> returnsBySymbol = new Dictionary<string, double>();
            Dictionary<string, double> volatilityBySymbol = new Dictionary<string, double>();

            for (int i = 0; i < settings.Components.Count; i++)
            {
                IndexComponentSettings component = settings.Components[i];

                if (component.Enabled == false)
                {
                    continue;
                }

                enabledCount++;

                if (framesByName.TryGetValue(component.UniqueName, out IndexComponentFrame componentFrame) == false)
                {
                    continue;
                }

                if (IsStale(componentFrame, settings, frame.Time))
                {
                    continue;
                }

                activeEnabledCount++;

                if (component.UseInIndex == false)
                {
                    continue;
                }

                if (componentFrame.Price <= 0)
                {
                    continue;
                }

                decimal lastPrice;

                if (state.LastPrices.TryGetValue(component.UniqueName, out lastPrice) == false || lastPrice <= 0)
                {
                    state.LastPrices[component.UniqueName] = componentFrame.Price;
                    state.LastTimes[component.UniqueName] = componentFrame.Time;
                    continue;
                }

                double logReturn = Math.Log((double)(componentFrame.Price / lastPrice));

                if (component.UsdDirection != 0)
                {
                    logReturn *= component.UsdDirection;
                }

                returnsBySymbol[component.UniqueName] = logReturn;

                double sigma = _volatilityModel.Update(component.UniqueName, logReturn);

                if (_volatilityModel.IsReady(component.UniqueName))
                {
                    volatilityBySymbol[component.UniqueName] = sigma;
                    activeIndexCount++;
                }

                state.LastPrices[component.UniqueName] = componentFrame.Price;
                state.LastTimes[component.UniqueName] = componentFrame.Time;
            }

            IndexQuality quality = BuildQuality(settings, enabledCount, activeEnabledCount, activeIndexCount);

            double sigmaMin = settings.SigmaMin <= 0 ? CalculateAutoSigmaMin(volatilityBySymbol) : (double)settings.SigmaMin;

            Dictionary<string, double> weights = _weightsModel.ComputeWeights(volatilityBySymbol, sigmaMin);

            double indexReturn = 0;

            foreach (KeyValuePair<string, double> weightPair in weights)
            {
                if (returnsBySymbol.TryGetValue(weightPair.Key, out double logReturn) == false)
                {
                    continue;
                }

                indexReturn += weightPair.Value * logReturn;
            }

            if (weights.Count > 0)
            {
                double newValue = (double)state.IndexValue * Math.Exp(indexReturn);
                state.IndexReturn = (decimal)indexReturn;
                state.IndexValue = (decimal)newValue;
                state.LastUpdateTime = frame.Time;
            }

            state.Quality = quality;

            IndexSnapshot snapshot = new IndexSnapshot
            {
                IndexValue = state.IndexValue,
                IndexReturn = state.IndexReturn,
                Quality = state.Quality,
                Time = frame.Time,
                ActiveComponents = activeIndexCount
            };

            foreach (KeyValuePair<string, double> weightPair in weights)
            {
                snapshot.ActiveComponentNames.Add(weightPair.Key);
            }

            return snapshot;
        }

        private void BuildVolatilityModel()
        {
            if (_settings.VolatilityMode == IndexVolatilityMode.Ewma)
            {
                _volatilityModel = new EwmaVolatilityModel((double)_settings.EwmaLambda);
            }
            else
            {
                _volatilityModel = new StdVolatilityModel(_settings.VolLookbackBars);
            }
        }

        private static IndexQuality BuildQuality(IndexSettings settings, int enabledCount, int activeEnabledCount, int activeIndexCount)
        {
            if (activeIndexCount < settings.MinActiveComponents)
            {
                return IndexQuality.Bad;
            }

            if (enabledCount == activeEnabledCount)
            {
                return IndexQuality.Good;
            }

            return IndexQuality.Degraded;
        }

        private static bool FrameHasClosedCandles(IndexDataFrame frame, IndexSettings settings)
        {
            if (frame == null || frame.Components == null)
            {
                return false;
            }

            int closedCount = 0;

            for (int i = 0; i < frame.Components.Count; i++)
            {
                IndexComponentFrame componentFrame = frame.Components[i];

                if (componentFrame.IsCandleClosed && componentFrame.Time == frame.Time)
                {
                    closedCount++;
                }
            }

            return closedCount >= settings.MinActiveComponents;
        }

        private static bool IsStale(IndexComponentFrame componentFrame, IndexSettings settings, DateTime frameTime)
        {
            TimeSpan diff = frameTime - componentFrame.Time;

            if (diff < TimeSpan.Zero)
            {
                diff = TimeSpan.Zero;
            }

            if (settings.StalenessMode == IndexStalenessMode.Bars)
            {
                if (componentFrame.TimeFrameTimeSpan == TimeSpan.Zero)
                {
                    return diff.TotalSeconds > settings.MaxStalenessSeconds;
                }

                TimeSpan barLimit = TimeSpan.FromTicks(componentFrame.TimeFrameTimeSpan.Ticks * settings.MaxStalenessBars);
                return diff > barLimit;
            }

            if (settings.StalenessMode == IndexStalenessMode.Seconds)
            {
                return diff.TotalSeconds > settings.MaxStalenessSeconds;
            }

            TimeSpan autoSpan = componentFrame.TimeFrameTimeSpan;

            if (autoSpan == TimeSpan.Zero)
            {
                autoSpan = TimeSpan.FromSeconds(settings.MaxStalenessSeconds);
            }

            double multiplier = (double)settings.AutoStalenessMultiplier;
            if (multiplier <= 0)
            {
                multiplier = 2;
            }

            TimeSpan autoLimit = TimeSpan.FromTicks((long)(autoSpan.Ticks * multiplier));
            return diff > autoLimit;
        }

        private static double CalculateAutoSigmaMin(Dictionary<string, double> volatilityBySymbol)
        {
            if (volatilityBySymbol.Count == 0)
            {
                return 0;
            }

            List<double> values = new List<double>(volatilityBySymbol.Values);
            values.Sort();

            int index = (int)Math.Floor((values.Count - 1) * 0.1);
            if (index < 0)
            {
                index = 0;
            }

            double sigmaMin = values[index];

            if (sigmaMin <= 0)
            {
                sigmaMin = values[values.Count - 1] * 0.1;
            }

            return sigmaMin;
        }

        private readonly IndexSettings _settings;
        private readonly IWeightsModel _weightsModel;
        private IVolatilityModel _volatilityModel;
    }
}
