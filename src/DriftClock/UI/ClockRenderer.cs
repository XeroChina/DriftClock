using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Text;

namespace DriftClock
{
    /// <summary>
    /// Draws layout C: huge clock + date that roams over the whole panel, small dim extras sliding along the
    /// edges. Every element gets its own drift channel so nothing is ever pinned to the same pixels.
    /// </summary>
    public sealed class ClockRenderer : IDisposable
    {
        private const int ClockGray = 200;      // ~#C8C8C8
        private const int DateGray = 150;       // ~#969696: step down from the clock so the hierarchy reads at a glance
        private const int ExtraGray = 112;      // ~#707070
        private const int HighlightGray = 48;   // ~#303030

        private static readonly string ClockFamily = PickFamily("Segoe UI Variable Display Light", "Segoe UI Light", "Segoe UI");
        private const string TextFamily = "Segoe UI";
        private const string SymbolFamily = "Segoe UI Symbol";

        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private readonly StringFormat _fmt = new StringFormat(StringFormat.GenericTypographic);
        private readonly StringFormat _center = new StringFormat(StringFormat.GenericTypographic);

        public ClockRenderer()
        {
            _fmt.FormatFlags |= StringFormatFlags.NoWrap;
            _center.FormatFlags |= StringFormatFlags.NoWrap;
            _center.Alignment = StringAlignment.Center;
            _center.LineAlignment = StringAlignment.Center;
        }

        public void Dispose()
        {
            foreach (Font f in _fonts.Values) f.Dispose();
            _fonts.Clear();
            _fmt.Dispose();
            _center.Dispose();
        }

        /// <summary>Changes only when the drawn output would change (minute, position, night, extras content).</summary>
        public string RenderKey(DateTime now, ScreenState st, DriftSample drift)
        {
            var sb = new StringBuilder();
            sb.Append(now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture));
            sb.Append('|').Append(Pos(drift.Clock));
            sb.Append('|').Append(Pos(drift.TopLeft)).Append(Pos(drift.TopRight))
              .Append(Pos(drift.BottomLeft)).Append(Pos(drift.BottomRight));
            if (st == null) return sb.ToString();
            sb.Append('|').Append(st.IsNight ? 'N' : 'D');
            if (st.Weather != null)
                sb.Append("|W").Append(Math.Round(st.Weather.CurrentTemp)).Append(',').Append(st.Weather.WeatherCode)
                  .Append(',').Append(Math.Round(st.Weather.High)).Append(',').Append(st.Weather.Low);
            if (st.BatteryPercent.HasValue) sb.Append("|B").Append(st.BatteryPercent.Value).Append(st.Charging ? "+" : "-");
            if (st.Events != null)
                foreach (EventLine e in st.Events) sb.Append("|E").Append(e.Text);
            return sb.ToString();
        }

        private static string Pos(PointF p)
        {
            // Three decimals: the drift moves about 20 px per repaint, so a coarser key would freeze the
            // picture in visible steps.
            return Math.Round(p.X, 3).ToString(CultureInfo.InvariantCulture) + "," +
                   Math.Round(p.Y, 3).ToString(CultureInfo.InvariantCulture) + ";";
        }

        public void Draw(Graphics g, Rectangle b, DateTime now, ScreenState st, Settings s, DriftSample drift)
        {
            if (st == null) st = new ScreenState();
            CultureInfo culture = CultureInfo.CurrentCulture;
            float f = st.IsNight ? Math.Max(10, Math.Min(100, s.NightBrightnessPercent)) / 100f : 1f;
            // Daytime ceiling on the clock itself: the night setting dims the whole picture, this one only caps
            // how bright the big digits may get, so a dim clock can be combined with bright corner info.
            float cap = Math.Max(20, Math.Min(100, s.MaxBrightnessPercent)) / 100f;
            float clockF = f * cap;

            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float margin = Math.Max(b.Width, b.Height) * 0.02f;
            float ep = Math.Max(6f, b.Height * 0.024f); // extras font size in px

            // The clock is sized so it still fits when parked against any edge, then it roams the leftover space.
            // The burn-in level decides how much of that leftover space it may use.
            float reach = BurnIn.RangeFactor(s.BurnInLevel);
            ClockLayout layout = MeasureClock(g, b, now, s, culture, margin);
            float roomX = Math.Max(0f, b.Width - 2 * margin - layout.BlockWidth) * reach;
            float roomY = Math.Max(0f, b.Height - 2 * margin - layout.BlockHeight) * reach;
            float freeX = Math.Max(0f, b.Width - 2 * margin - layout.BlockWidth);
            float freeY = Math.Max(0f, b.Height - 2 * margin - layout.BlockHeight);
            float left = margin + (freeX - roomX) / 2f + roomX * Unit(drift.Clock.X);
            float top = margin + (freeY - roomY) / 2f + roomY * Unit(drift.Clock.Y);

            var clockRect = new RectangleF(left, top, layout.BlockWidth, layout.BlockHeight);

            // The corner blocks slide along the edges inside a small band: they stay legible in their corner but
            // never sit on the same pixels for long. They fade out while the clock passes over them, so the big
            // digits always stay readable.
            float bandX = b.Width * 0.06f, bandY = b.Height * 0.08f;
            if (s.ShowWeather && st.Weather != null)
            {
                float x = margin + bandX * Unit(drift.TopLeft.X);
                float y = margin + bandY * Unit(drift.TopLeft.Y);
                float k = Fade(WeatherBounds(g, x, y, ep, st.Weather, s), clockRect);
                DrawWeather(g, x, y, ep, st.Weather, s, Gray(ExtraGray, f * k));
            }
            if (s.ShowBattery && st.BatteryPercent.HasValue)
            {
                float right = b.Right - margin - bandX * Unit(drift.TopRight.X);
                float y = margin + bandY * Unit(drift.TopRight.Y);
                float k = Fade(BatteryBounds(g, right, y, ep, st), clockRect);
                DrawBattery(g, right, y, ep, st, Gray(ExtraGray, f * k));
            }
            if (st.Events != null && st.Events.Count > 0)
            {
                float x = margin + bandX * Unit(drift.BottomLeft.X);
                float bottom = b.Bottom - margin - bandY * Unit(drift.BottomLeft.Y);
                float k = Fade(EventsBounds(g, x, bottom, ep, st.Events), clockRect);
                DrawEvents(g, x, bottom, ep, st.Events, Gray(ExtraGray, f * k));
            }
            if (s.ShowMonthGrid)
            {
                float right = b.Right - margin - bandX * Unit(drift.BottomRight.X);
                float bottom = b.Bottom - margin - bandY * Unit(drift.BottomRight.Y);
                float k = Fade(MonthBounds(g, right, bottom, ep, now, culture), clockRect);
                DrawMonth(g, right, bottom, ep, now, culture, f, Gray(ExtraGray, f * k), k);
            }

            // The clock goes on top of everything, so its strokes always win over the dim corner info.
            DrawClock(g, now, s, layout, left, top, Gray(ClockGray, clockF), Gray(DateGray, clockF), Gray(ExtraGray, clockF));
        }

        /// <summary>1 while the corner block is clear of the clock, 0 when the clock runs over it.</summary>
        private static float Fade(RectangleF extra, RectangleF clock)
        {
            float dx = Math.Max(0f, Math.Max(clock.Left - extra.Right, extra.Left - clock.Right));
            float dy = Math.Max(0f, Math.Max(clock.Top - extra.Bottom, extra.Top - clock.Bottom));
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            return Math.Max(0f, Math.Min(1f, d / 56f));
        }

        /// <summary>Maps a drift value in [-1, 1] onto the free space.</summary>
        private static float Unit(float v)
        {
            return (v + 1f) * 0.5f;
        }

        // ---- centre ------------------------------------------------------------------------------------------

        /// <summary>Metrics of the clock block: fonts, sizes and the offset of the date under the time.</summary>
        private struct ClockLayout
        {
            public Font Big;
            public SizeF TimeSize;
            public string Suffix;
            public float SufPx;
            public float SufW;
            public Font DateFont;
            public SizeF DateSize;
            public float Gap;
            public float BlockWidth;
            public float BlockHeight;
        }

        private ClockLayout MeasureClock(Graphics g, Rectangle b, DateTime now, Settings s, CultureInfo culture, float margin)
        {
            var l = new ClockLayout();
            string time = now.ToString(s.Use12Hour ? "h\\:mm" : "HH\\:mm", CultureInfo.InvariantCulture);
            l.Suffix = null;
            if (s.Use12Hour)
            {
                l.Suffix = now.Hour < 12 ? culture.DateTimeFormat.AMDesignator : culture.DateTimeFormat.PMDesignator;
                if (string.IsNullOrEmpty(l.Suffix)) l.Suffix = now.Hour < 12 ? "AM" : "PM";
            }

            // Leave room on both sides so the clock can be parked against either edge without being cut off.
            float maxWidth = b.Width - 2 * margin;
            float px = b.Height * 0.34f;
            l.Big = GetFont(ClockFamily, px);
            l.TimeSize = Measure(g, time, l.Big);
            l.SufPx = px * 0.16f;
            l.SufW = l.Suffix == null ? 0 : Measure(g, l.Suffix, GetFont(TextFamily, l.SufPx)).Width + l.SufPx * 0.4f;
            while (l.TimeSize.Width + l.SufW > maxWidth && px > 8)
            {
                px *= 0.92f;
                l.Big = GetFont(ClockFamily, px);
                l.TimeSize = Measure(g, time, l.Big);
                l.SufPx = px * 0.16f;
                l.SufW = l.Suffix == null ? 0 : Measure(g, l.Suffix, GetFont(TextFamily, l.SufPx)).Width + l.SufPx * 0.4f;
            }

            float datePx = Math.Max(b.Height * 0.04f, 10f);
            l.DateFont = GetFont(TextFamily, datePx);
            l.DateSize = Measure(g, now.ToString("dddd, d MMMM", culture), l.DateFont);
            while (l.DateSize.Width > maxWidth && datePx > 8)
            {
                datePx *= 0.92f;
                l.DateFont = GetFont(TextFamily, datePx);
                l.DateSize = Measure(g, now.ToString("dddd, d MMMM", culture), l.DateFont);
            }

            l.Gap = l.TimeSize.Height * -0.06f;
            l.BlockWidth = Math.Max(l.TimeSize.Width + l.SufW, l.DateSize.Width);
            l.BlockHeight = l.TimeSize.Height + l.Gap + l.DateSize.Height;
            return l;
        }

        private void DrawClock(Graphics g, DateTime now, Settings s, ClockLayout l, float left, float top,
                               Color clockColor, Color dateColor, Color dimColor)
        {
            using (var cb = new SolidBrush(clockColor))
            using (var db = new SolidBrush(dateColor))
            using (var sb = new SolidBrush(dimColor))
            {
                // Huge glyphs look cleaner without GridFit snapping their stems to whole pixels.
                TextRenderingHint hint = g.TextRenderingHint;
                if (l.TimeSize.Height >= 96f) g.TextRenderingHint = TextRenderingHint.AntiAlias;
                try
                {
                    g.DrawString(now.ToString(s.Use12Hour ? "h\\:mm" : "HH\\:mm", CultureInfo.InvariantCulture),
                                 l.Big, cb, left, top, _fmt);
                    if (l.Suffix != null)
                    {
                        Font sf = GetFont(TextFamily, l.SufPx);
                        float sy = top + l.TimeSize.Height * 0.62f;
                        g.DrawString(l.Suffix, sf, sb, left + l.TimeSize.Width + l.SufPx * 0.4f, sy, _fmt);
                    }
                    g.DrawString(now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture), l.DateFont, db,
                                 left + (l.BlockWidth - l.DateSize.Width) / 2f, top + l.TimeSize.Height + l.Gap, _fmt);
                }
                finally { g.TextRenderingHint = hint; }
            }
        }

        // ---- corners -----------------------------------------------------------------------------------------

        /// <summary>Rectangle the weather line occupies, used to fade it when the clock comes close.</summary>
        private RectangleF WeatherBounds(Graphics g, float x, float y, float ep, WeatherInfo w, Settings s)
        {
            Font text = GetFont(TextFamily, ep);
            Font sym = GetFont(SymbolFamily, ep * 1.25f);
            string body = WeatherBody(w, s);
            float gw = Measure(g, Weather.GlyphFor(w.WeatherCode), sym).Width + ep * 0.5f;
            float bw = Measure(g, body, text).Width;
            return new RectangleF(x, y - ep * 0.1f, gw + bw, ep * 1.4f);
        }

        private static string WeatherBody(WeatherInfo w, Settings s)
        {
            string body = string.Format(CultureInfo.InvariantCulture, "{0}°   ↑{1}° ↓{2}°",
                Whole(w.CurrentTemp), Whole(w.High), Whole(w.Low));
            if (!string.IsNullOrWhiteSpace(s.LocationName)) body += "   " + s.LocationName;
            return body;
        }

        private void DrawWeather(Graphics g, float x, float y, float ep, WeatherInfo w, Settings s, Color c)
        {
            Font text = GetFont(TextFamily, ep);
            Font sym = GetFont(SymbolFamily, ep * 1.25f);
            string glyph = Weather.GlyphFor(w.WeatherCode);
            string body = WeatherBody(w, s);

            using (var br = new SolidBrush(c))
            {
                g.DrawString(glyph, sym, br, x, y - ep * 0.1f, _fmt);
                float gw = Measure(g, glyph, sym).Width + ep * 0.5f;
                g.DrawString(body, text, br, x + gw, y, _fmt);
            }
        }

        /// <summary>Rectangle the battery readout occupies.</summary>
        private RectangleF BatteryBounds(Graphics g, float right, float y, float ep, ScreenState st)
        {
            Font text = GetFont(TextFamily, ep);
            Font sym = GetFont(SymbolFamily, ep * 1.1f);
            float tw = Measure(g, st.BatteryPercent.Value.ToString(CultureInfo.InvariantCulture) + "%", text).Width;
            float bw = st.Charging ? Measure(g, "⚡", sym).Width + ep * 0.2f : 0f;
            return new RectangleF(right - tw - bw, y, tw + bw, ep * 1.4f);
        }

        /// <summary>Right-aligned at <paramref name="right"/>.</summary>
        private void DrawBattery(Graphics g, float right, float y, float ep, ScreenState st, Color c)
        {
            Font text = GetFont(TextFamily, ep);
            Font sym = GetFont(SymbolFamily, ep * 1.1f);
            string body = st.BatteryPercent.Value.ToString(CultureInfo.InvariantCulture) + "%";
            float tw = Measure(g, body, text).Width;
            string bolt = "⚡";
            float bw = st.Charging ? Measure(g, bolt, sym).Width + ep * 0.2f : 0f;
            float x = right - tw - bw;
            using (var br = new SolidBrush(c))
            {
                if (st.Charging) g.DrawString(bolt, sym, br, x, y, _fmt);
                g.DrawString(body, text, br, x + bw, y, _fmt);
            }
        }

        /// <summary>Rectangle the event list occupies.</summary>
        private RectangleF EventsBounds(Graphics g, float x, float bottom, float ep, List<EventLine> events)
        {
            Font text = GetFont(TextFamily, ep);
            float lineH = ep * 1.5f;
            float w = 0f;
            foreach (EventLine e in events) w = Math.Max(w, Measure(g, e.Text, text).Width);
            return new RectangleF(x, bottom - events.Count * lineH, w, events.Count * lineH);
        }

        /// <summary>Anchored at the bottom-left of the block.</summary>
        private void DrawEvents(Graphics g, float x, float bottom, float ep, List<EventLine> events, Color c)
        {
            Font text = GetFont(TextFamily, ep);
            float lineH = ep * 1.5f;
            float y = bottom - events.Count * lineH;
            using (var br = new SolidBrush(c))
                foreach (EventLine e in events)
                {
                    g.DrawString(e.Text, text, br, x, y, _fmt);
                    y += lineH;
                }
        }

        /// <summary>Metrics of the month grid.</summary>
        private struct MonthLayout
        {
            public Font Font;
            public float CellW;
            public float LineH;
            public int Lead;
            public int Days;
            public RectangleF Rect;   // the whole grid
        }

        private MonthLayout MeasureMonth(Graphics g, float right, float bottom, float ep, DateTime now, CultureInfo culture)
        {
            var m = new MonthLayout();
            float gp = ep * 0.9f;
            m.Font = GetFont(TextFamily, gp);
            m.CellW = gp * 2.2f;
            m.LineH = gp * 1.55f;

            DateTime first = new DateTime(now.Year, now.Month, 1);
            DayOfWeek firstDow = culture.DateTimeFormat.FirstDayOfWeek;
            m.Lead = ((int)first.DayOfWeek - (int)firstDow + 7) % 7;
            m.Days = DateTime.DaysInMonth(now.Year, now.Month);
            int rows = (m.Lead + m.Days + 6) / 7;

            float gridW = m.CellW * 7, gridH = m.LineH * (rows + 2);
            m.Rect = new RectangleF(right - gridW, bottom - gridH, gridW, gridH);
            return m;
        }

        /// <summary>Rectangle the month grid occupies.</summary>
        private RectangleF MonthBounds(Graphics g, float right, float bottom, float ep, DateTime now, CultureInfo culture)
        {
            return MeasureMonth(g, right, bottom, ep, now, culture).Rect;
        }

        /// <summary>Anchored at the bottom-right of the block.</summary>
        private void DrawMonth(Graphics g, float right, float bottom, float ep, DateTime now, CultureInfo culture, float f, Color c, float k)
        {
            MonthLayout m = MeasureMonth(g, right, bottom, ep, now, culture);
            Font font = m.Font;
            float cellW = m.CellW, lineH = m.LineH;
            float x0 = m.Rect.Left, y0 = m.Rect.Top;

            using (var br = new SolidBrush(c))
            using (var todayBg = new SolidBrush(Gray(HighlightGray, f * k)))
            using (var todayFg = new SolidBrush(Gray(ClockGray, f * k)))
            {
                // Centered over the columns (the weekday initials and day numbers are centered in their cells too).
                g.DrawString(now.ToString("MMMM yyyy", culture).ToUpper(culture), font, br,
                             new RectangleF(x0, y0, m.Rect.Width, lineH), _center);
                DayOfWeek firstDow = culture.DateTimeFormat.FirstDayOfWeek;
                for (int i = 0; i < 7; i++)
                {
                    var dow = (DayOfWeek)(((int)firstDow + i) % 7);
                    string name = culture.DateTimeFormat.GetAbbreviatedDayName(dow);
                    string initial = string.IsNullOrEmpty(name) ? "" : name.Substring(0, 1).ToUpper(culture);
                    g.DrawString(initial, font, br, new RectangleF(x0 + i * cellW, y0 + lineH, cellW, lineH), _center);
                }
                for (int d = 1; d <= m.Days; d++)
                {
                    int slot = m.Lead + d - 1;
                    var cell = new RectangleF(x0 + (slot % 7) * cellW, y0 + (2 + slot / 7) * lineH, cellW, lineH);
                    bool today = d == now.Day;
                    if (today) FillRounded(g, todayBg, cell, lineH * 0.3f);
                    g.DrawString(d.ToString(CultureInfo.InvariantCulture), font, today ? todayFg : br, cell, _center);
                }
            }
        }

        // ---- helpers -----------------------------------------------------------------------------------------

        private static int Whole(double v)
        {
            return (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        private static Color Gray(int v, float f)
        {
            int n = Math.Max(0, Math.Min(255, (int)(v * f + 0.5f)));
            return Color.FromArgb(n, n, n);
        }

        private SizeF Measure(Graphics g, string text, Font font)
        {
            return g.MeasureString(text, font, PointF.Empty, _fmt);
        }

        private Font GetFont(string family, float px)
        {
            string key = family + "|" + Math.Round(px, 1).ToString(CultureInfo.InvariantCulture);
            Font font;
            if (!_fonts.TryGetValue(key, out font))
                _fonts[key] = font = new Font(family, Math.Max(1f, px), FontStyle.Regular, GraphicsUnit.Pixel);
            return font;
        }

        private static void FillRounded(Graphics g, Brush brush, RectangleF r, float radius)
        {
            float d = radius * 2;
            using (var path = new GraphicsPath())
            {
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        private static string PickFamily(params string[] candidates)
        {
            try
            {
                using (var fc = new InstalledFontCollection())
                    foreach (string want in candidates)
                        foreach (FontFamily ff in fc.Families)
                            if (string.Equals(ff.Name, want, StringComparison.OrdinalIgnoreCase)) return ff.Name;
            }
            catch { }
            return candidates[candidates.Length - 1];
        }
    }
}
