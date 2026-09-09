using System;

namespace Tardigrade.Simulation
{
    public sealed class DeterministicNoise
    {
        Random random;
        bool hasSpare;
        double spare;

        public DeterministicNoise(uint seed) => Reset(seed);

        public void Reset(uint seed)
        {
            random = new Random(unchecked((int)seed));
            hasSpare = false;
        }

        public double Uniform() => random.NextDouble();

        public double Gaussian(double standardDeviation)
        {
            if (standardDeviation == 0.0)
                return 0.0;
            if (hasSpare)
            {
                hasSpare = false;
                return spare * standardDeviation;
            }
            double u;
            double v;
            double s;
            do
            {
                u = 2.0 * random.NextDouble() - 1.0;
                v = 2.0 * random.NextDouble() - 1.0;
                s = u * u + v * v;
            } while (s >= 1.0 || s == 0.0);
            double factor = Math.Sqrt(-2.0 * Math.Log(s) / s);
            spare = v * factor;
            hasSpare = true;
            return u * factor * standardDeviation;
        }
    }
}
