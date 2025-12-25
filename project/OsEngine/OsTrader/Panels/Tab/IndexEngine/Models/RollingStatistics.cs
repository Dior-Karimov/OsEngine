/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using System;

namespace OsEngine.OsTrader.Panels.Tab.IndexEngine.Models
{
    public class RollingStatistics
    {
        public RollingStatistics(int size)
        {
            if (size < 2)
            {
                size = 2;
            }

            _size = size;
            _values = new double[_size];
        }

        public void Reset()
        {
            _index = 0;
            _count = 0;
            _sum = 0;
            _sumSq = 0;
        }

        public void Add(double value)
        {
            if (_count < _size)
            {
                _values[_index] = value;
                _sum += value;
                _sumSq += value * value;
                _count++;
                _index = (_index + 1) % _size;
                return;
            }

            double oldValue = _values[_index];
            _sum -= oldValue;
            _sumSq -= oldValue * oldValue;

            _values[_index] = value;
            _sum += value;
            _sumSq += value * value;

            _index = (_index + 1) % _size;
        }

        public bool IsReady => _count >= 2;

        public double StdDev
        {
            get
            {
                if (_count < 2)
                {
                    return 0;
                }

                double mean = _sum / _count;
                double variance = (_sumSq / _count) - (mean * mean);

                if (variance < 0)
                {
                    variance = 0;
                }

                return Math.Sqrt(variance);
            }
        }

        private readonly int _size;
        private readonly double[] _values;
        private int _index;
        private int _count;
        private double _sum;
        private double _sumSq;
    }
}
