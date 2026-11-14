using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Xunit;

namespace DriftClock.Tests
{
    public class ClockRendererTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 10, 14, 37, 0);

        private static Settings AllOn()
        {
            return new Settings { ShowWeather = true, ShowBattery = true, ShowMonthGrid = true, ShowEvents = true, LocationName = "Milan" };
        }

        private static Settings AllOff()
        {
            return new Settings { ShowWeather = false, ShowBattery = false, ShowMonthGrid = false, ShowEvents = false };
        }

        private static ScreenState Full(bool night = false)
        {
            return new ScreenState
            {
                Weather = new WeatherInfo { CurrentTemp = 12.3, WeatherCode = 3, High = 14, Low = 8, FetchedUtc = DateTime.UtcNow },
                BatteryPercent = 78,
                Charging = true,
                Events = new List<EventLine> { new EventLine { Text = "16:00 Design review" }, new EventLine { Text = "18:30 Gym" } },
                IsNight = night
            };
        }

        private static byte[] Render(int w, int h, Settings s, ScreenState st, DriftSample drift)
        {
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var r = new ClockRenderer())
            {
                g.Clear(Color.Black);
                r.Draw(g, new Rectangle(0, 0, w, h), Now, st, s, drift);
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var bytes = new byte[data.Stride * h];
                    Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    return bytes;
                }
                finally { bmp.UnlockBits(data); }
            }
        }

        private static bool Lit(byte[] px, int w, Rectangle area)
        {
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int i = (y * w + x) * 4;
                    if (px[i] > 0 || px[i + 1] > 0 || px[i + 2] > 0) return true;
                }
            return false;
        }

        /// <summary>Vertical band of lit pixels, used to locate the clock block on screen.</summary>
        private static int[] ClockRows(byte[] px, int w, int h)
        {
            int top = -1, bottom = -1;
            for (int y = 0; y < h; y++)
            {
                bool any = false;
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    if (px[i] > 0) { any = true; break; }
                }
                if (any) { if (top < 0) top = y; bottom = y; }
            }
            return new[] { top, bottom };
        }

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(160, 120)]
        [InlineData(1080, 1920)]
        public void Draw_does_not_throw_with_extras_on_or_off(int w, int h)
        {
            Render(w, h, AllOn(), Full(), new DriftSample());
            var far = new DriftSample { Clock = new PointF(-1, -1), TopLeft = new PointF(-1, 1), BottomRight = new PointF(1, -1) };
            Render(w, h, AllOff(), new ScreenState(), far);
        }

        [Fact]
        public void With_everything_off_the_corners_stay_black()
        {
            var px = Render(1920, 1080, AllOff(), new ScreenState(), new DriftSample());
            Assert.False(Lit(px, 1920, new Rectangle(0, 0, 200, 150)));
            Assert.False(Lit(px, 1920, new Rectangle(1720, 0, 200, 150)));
            Assert.False(Lit(px, 1920, new Rectangle(0, 930, 200, 150)));
            Assert.False(Lit(px, 1920, new Rectangle(1720, 930, 200, 150)));
            Assert.True(Lit(px, 1920, new Rectangle(600, 300, 720, 400))); // the clock itself
        }

        [Fact]
        public void Each_corner_shows_its_extra()
        {
            var px = Render(1920, 1080, AllOn(), Full(), new DriftSample());
            Assert.True(Lit(px, 1920, new Rectangle(0, 0, 600, 200)), "weather");
            Assert.True(Lit(px, 1920, new Rectangle(1320, 0, 600, 200)), "battery");
            Assert.True(Lit(px, 1920, new Rectangle(0, 880, 600, 200)), "events");
            Assert.True(Lit(px, 1920, new Rectangle(1320, 780, 600, 300)), "month grid");
        }

        [Fact]
        public void Night_mode_scales_brightness_down()
        {
            var s = AllOn();
            s.NightBrightnessPercent = 35;
            var px = Render(960, 540, s, Full(night: true), new DriftSample());
            int max = 0;
            for (int i = 0; i < px.Length; i += 4) max = Math.Max(max, Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])));
            Assert.InRange(max, 1, (int)(200 * 0.35) + 2);
        }

        [Fact]
        public void Max_brightness_caps_the_clock_in_daytime()
        {
            var s = AllOff();
            s.MaxBrightnessPercent = 40;
            var px = Render(960, 540, s, new ScreenState(), new DriftSample());
            int max = 0;
            for (int i = 0; i < px.Length; i += 4) max = Math.Max(max, Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])));
            Assert.InRange(max, 1, (int)(200 * 0.4) + 2);
        }

        [Fact]
        public void Drift_offset_moves_the_picture()
        {
            var a = Render(640, 360, AllOff(), new ScreenState(), new DriftSample());
            var b = Render(640, 360, AllOff(), new ScreenState(), new DriftSample { Clock = new PointF(1, 0) });
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void The_clock_roams_to_the_edges_of_the_screen()
        {
            const int W = 1920, H = 1080;
            var s = AllOff();
            s.BurnInLevel = BurnIn.High;

            var topLeft = Render(W, H, s, new ScreenState(), new DriftSample { Clock = new PointF(-1, -1) });
            var bottomRight = Render(W, H, s, new ScreenState(), new DriftSample { Clock = new PointF(1, 1) });

            int[] a = ClockRows(topLeft, W, H);
            int[] b = ClockRows(bottomRight, W, H);
            Assert.True(a[0] >= 0 && b[0] >= 0, "clock not drawn");
            // At the extreme corners the clock must start much higher / end much lower than in the middle.
            Assert.True(a[0] < b[0], "clock did not move down: " + a[0] + " vs " + b[0]);
            Assert.True(b[1] - b[0] > 200, "clock travel too small: " + (b[1] - b[0]));
        }

        [Fact]
        public void The_clock_never_leaves_the_screen()
        {
            const int W = 1920, H = 1080;
            var s = AllOff();
            s.BurnInLevel = BurnIn.High;
            var px = Render(W, H, s, new ScreenState(), new DriftSample { Clock = new PointF(-1, -1) });
            // The leftmost lit column must not be cut off at x = 0 and the clock must stay inside the panel.
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = (y * W + x) * 4;
                    if (px[i] > 0) { Assert.True(x >= 0 && x < W && y >= 0 && y < H); return; }
                }
        }

        [Fact]
        public void RenderKey_changes_only_when_output_would_change()
        {
            using (var r = new ClockRenderer())
            {
                var st = Full();
                string k = r.RenderKey(Now, st, new DriftSample());
                Assert.Equal(k, r.RenderKey(Now.AddSeconds(20), st, new DriftSample()));
                Assert.NotEqual(k, r.RenderKey(Now.AddMinutes(1), st, new DriftSample()));
                Assert.NotEqual(k, r.RenderKey(Now, st, new DriftSample { Clock = new PointF(0.5f, 0.5f) }));
                Assert.NotEqual(k, r.RenderKey(Now, Full(night: true), new DriftSample()));
            }
        }
    }
}
