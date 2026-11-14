using System;
using System.Drawing;

namespace DriftClock
{
    /// <summary>
    /// Normalized drift positions in [-1, 1]. The renderer scales them to the free space that is actually left on
    /// the screen, so the clock can roam over the whole panel instead of a fixed pixel box.
    /// </summary>
    public struct DriftSample
    {
        public PointF Clock;
        public PointF TopLeft;
        public PointF TopRight;
        public PointF BottomLeft;
        public PointF BottomRight;
    }

    /// <summary>
    /// Smooth, non-repeating drift path. Each axis is a sine sweep whose period is modulated by a second, much
    /// slower sine, plus a little value noise. The sweep gives the constant, visible movement a burn-in guard
    /// needs, the modulation stops the clock from ever following the same route twice, and the noise keeps the
    /// motion from looking mechanical.
    /// </summary>
    public static class DriftPath
    {
        private const double SweepWeight = 0.85;   // how much of the movement is the regular sweep
        private const double Modulation = 0.3;      // how much the sweep period wobbles

        /// <summary>
        /// Drift positions for the given moment (minutes since an arbitrary epoch). <paramref name="speed"/> scales
        /// how fast the path is walked through: 1 is the normal drift, 0 keeps the picture still.
        /// </summary>
        public static DriftSample Sample(double minutes, float speed)
        {
            var s = new DriftSample();
            if (speed <= 0) return s;

            double t = minutes * speed;
            s.Clock = new PointF((float)Axis(t, 2.4, 0), (float)Axis(t, 1.8, 1));

            // The corner blocks get their own periods, so they slide along the edges independently of the clock
            // instead of following it off the screen.
            s.TopLeft = new PointF((float)Axis(t, 2.9, 2), (float)Axis(t, 2.3, 3));
            s.TopRight = new PointF((float)Axis(t, 2.2, 4), (float)Axis(t, 3.1, 5));
            s.BottomLeft = new PointF((float)Axis(t, 3.4, 6), (float)Axis(t, 1.9, 7));
            s.BottomRight = new PointF((float)Axis(t, 1.8, 8), (float)Axis(t, 3.7, 9));
            return s;
        }

        /// <summary>
        /// One axis of the path: a swept sine with a modulated period, plus a little noise, all in [-1, 1].
        /// <paramref name="periodMinutes"/> is the nominal time of one full sweep from edge to edge and back.
        /// </summary>
        private static double Axis(double t, double periodMinutes, int channel)
        {
            double phase = Phase(channel);
            // The period is wobbled by a slow sine, so consecutive sweeps take slightly different times and the
            // route never repeats exactly.
            double theta = 2.0 * Math.PI * (t / periodMinutes
                          + Modulation * Math.Sin(2.0 * Math.PI * t / (periodMinutes * 4.7) + phase));
            double sweep = Math.Sin(theta);
            double wander = Noise(t / (periodMinutes * 3.0), channel);
            return sweep * SweepWeight + wander * (1.0 - SweepWeight);
        }

        /// <summary>Fixed random phase per channel, so the different axes do not move in lockstep.</summary>
        private static double Phase(int channel)
        {
            unchecked
            {
                ulong h = (ulong)(channel + 1) * 0xD6E8FEB86659FD93UL;
                h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 27; h *= 0x94D049BB133111EBUL;
                h ^= h >> 31;
                return (h / (double)ulong.MaxValue) * 2.0 * Math.PI;
            }
        }

        /// <summary>Interpolated value noise in [-1, 1]: smoothstep between two hashed vertices per cell.</summary>
        private static double Noise(double t, int channel)
        {
            long cell = (long)Math.Floor(t);
            double f = t - cell;
            double s = f * f * (3.0 - 2.0 * f);
            double a = Vertex(cell, channel);
            return a + (Vertex(cell + 1, channel) - a) * s;
        }

        /// <summary>Deterministic pseudo-random vertex in [-1, 1] (splitmix64 finalizer).</summary>
        private static double Vertex(long cell, int channel)
        {
            unchecked
            {
                ulong h = (ulong)cell * 0x9E3779B97F4A7C15UL + (ulong)(channel + 1) * 0xD6E8FEB86659FD93UL;
                h ^= h >> 30; h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 27; h *= 0x94D049BB133111EBUL;
                h ^= h >> 31;
                return h / (double)ulong.MaxValue * 2.0 - 1.0;
            }
        }
    }
}
