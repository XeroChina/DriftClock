using System;
using System.Collections.Generic;
using System.Drawing;
using DriftClock;
using Xunit;

namespace DriftClock.Tests
{
    public class DriftPathTests
    {
        [Fact]
        public void Sample_stays_inside_the_normalized_range()
        {
            for (int i = 0; i < 5000; i++)
            {
                DriftSample s = DriftPath.Sample(i * 0.37, 1f);
                foreach (PointF p in new[] { s.Clock, s.TopLeft, s.TopRight, s.BottomLeft, s.BottomRight })
                {
                    Assert.InRange(p.X, -1f, 1f);
                    Assert.InRange(p.Y, -1f, 1f);
                }
            }
        }

        [Fact]
        public void Movement_is_smooth_rather_than_a_jump()
        {
            // One repaint apart (250 ms) the picture must still glide: the step stays far below a pixel jump.
            for (int i = 0; i < 2000; i++)
            {
                DriftSample a = DriftPath.Sample(i * 0.25 / 60.0, 1f);
                DriftSample b = DriftPath.Sample((i + 1) * 0.25 / 60.0, 1f);
                Assert.InRange(Math.Abs(a.Clock.X - b.Clock.X) + Math.Abs(a.Clock.Y - b.Clock.Y), 0f, 0.02f);
            }
        }

        [Fact]
        public void Path_sweeps_the_whole_box_and_never_repeats()
        {
            float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
            var seen = new HashSet<string>();
            for (int i = 0; i < 48 * 60 / 2; i++) // 48 hours, sampled every 2 minutes
            {
                DriftSample s = DriftPath.Sample(i * 2.0, 1f);
                minX = Math.Min(minX, s.Clock.X); maxX = Math.Max(maxX, s.Clock.X);
                minY = Math.Min(minY, s.Clock.Y); maxY = Math.Max(maxY, s.Clock.Y);
                seen.Add(Math.Round(s.Clock.X, 3) + "|" + Math.Round(s.Clock.Y, 3));
            }
            Assert.True(minX <= -0.75f && maxX >= 0.75f, "x sweep " + minX + ".." + maxX);
            Assert.True(minY <= -0.75f && maxY >= 0.75f, "y sweep " + minY + ".." + maxY);
            Assert.True(seen.Count > 300, "positions are too repetitive: " + seen.Count);

            // The route is hashed from absolute time, so it does not come back a day later.
            DriftSample a = DriftPath.Sample(600, 1f);
            DriftSample b = DriftPath.Sample(600 + 24 * 60, 1f);
            Assert.NotEqual(a.Clock, b.Clock);
        }

        [Fact]
        public void Corner_blocks_drift_independently_of_the_clock()
        {
            DriftSample s = DriftPath.Sample(137.5, 1f);
            Assert.NotEqual(s.Clock, s.TopLeft);
            Assert.NotEqual(s.TopLeft, s.TopRight);
            Assert.NotEqual(s.TopRight, s.BottomLeft);
            Assert.NotEqual(s.BottomLeft, s.BottomRight);
        }

        [Fact]
        public void Same_moment_gives_the_same_sample()
        {
            Assert.Equal(DriftPath.Sample(12345.25, 1f).Clock, DriftPath.Sample(12345.25, 1f).Clock);
        }

        [Fact]
        public void Negative_times_and_zero_speed_are_safe()
        {
            DriftSample s = DriftPath.Sample(-5000.5, 1f);
            Assert.InRange(s.Clock.X, -1f, 1f);
            Assert.InRange(s.Clock.Y, -1f, 1f);

            DriftSample still = DriftPath.Sample(42, 0f);
            Assert.Equal(PointF.Empty, still.Clock);
            Assert.Equal(PointF.Empty, still.TopLeft);
            Assert.Equal(PointF.Empty, still.TopRight);
            Assert.Equal(PointF.Empty, still.BottomLeft);
            Assert.Equal(PointF.Empty, still.BottomRight);
        }

        [Fact]
        public void Higher_burn_in_levels_travel_farther()
        {
            float low = Travel(BurnIn.SpeedFactor(BurnIn.Low));
            float normal = Travel(BurnIn.SpeedFactor(BurnIn.Normal));
            float high = Travel(BurnIn.SpeedFactor(BurnIn.High));
            Assert.True(low < normal && normal < high, low + " / " + normal + " / " + high);
            Assert.Equal(0f, BurnIn.SpeedFactor(BurnIn.Off));
        }

        [Fact]
        public void Higher_burn_in_levels_cover_more_of_the_screen()
        {
            // "Low" keeps to a small part of the free space, "Normal" and "High" both use all of it.
            Assert.True(BurnIn.RangeFactor(BurnIn.Low) < BurnIn.RangeFactor(BurnIn.Normal));
            Assert.True(BurnIn.RangeFactor(BurnIn.Normal) <= BurnIn.RangeFactor(BurnIn.High));
            Assert.Equal(1f, BurnIn.RangeFactor(BurnIn.High));
            Assert.Equal(1f, BurnIn.RangeFactor(BurnIn.Normal));
            Assert.Equal(0f, BurnIn.RangeFactor(BurnIn.Off));
        }

        /// <summary>
        /// Distance the clock channel covers in one hour of wall-clock time, for a given speed factor.
        /// The path itself is the same at every speed; the speed factor only says how fast it is walked through.
        /// </summary>
        private static float Travel(float speed)
        {
            float dist = 0f;
            const double StepMinutes = 0.1 / 60.0;   // 100 ms of wall-clock time
            DriftSample prev = DriftPath.Sample(0, speed);
            for (int i = 1; i < 60 * 60 * 10; i++)
            {
                DriftSample s = DriftPath.Sample(i * StepMinutes, speed);
                dist += Math.Abs(s.Clock.X - prev.Clock.X);
                prev = s;
            }
            return dist;
        }
    }
}
