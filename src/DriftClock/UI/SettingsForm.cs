using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DriftClock
{
    internal sealed class Win32Owner : IWin32Window
    {
        public Win32Owner(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; private set; }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly Settings _settings;
        private string _locName;
        private double? _lat, _lon;

        private readonly TextBox _search = new TextBox();
        private readonly Button _searchBtn = new Button();
        private readonly ListBox _results = new ListBox();
        private readonly Label _chosen = new Label();
        private readonly Button _clear = new Button();
        private readonly CheckBox _weather = new CheckBox(), _battery = new CheckBox(), _month = new CheckBox(),
                                  _events = new CheckBox(), _12h = new CheckBox(), _fahrenheit = new CheckBox();
        private readonly TextBox _ical = new TextBox();
        private readonly TrackBar _night = new TrackBar();
        private readonly Label _nightValue = new Label();
        private readonly ComboBox _burnIn = new ComboBox();
        private readonly Label _burnInHint = new Label();
        private readonly TrackBar _maxBright = new TrackBar();
        private readonly Label _maxBrightValue = new Label();
        private readonly DateTimePicker _nightFrom = new DateTimePicker(), _nightTo = new DateTimePicker();
        private readonly Button _ok = new Button(), _cancel = new Button();
        private readonly float _scale;

        public SettingsForm(Settings settings)
        {
            _settings = settings;
            _locName = settings.LocationName;
            _lat = settings.Latitude;
            _lon = settings.Longitude;

            Text = "DriftClock settings";
            Font = new Font("Segoe UI", 9f);
            // Fonts are rendered at the real monitor DPI, but this fixed pixel layout is not scaled by
            // WinForms (the per-monitor manifest means AutoScaleMode.Dpi never sees a DPI change), so the
            // dialog came out cramped: text was clipped and the track bar grew over the label below it.
            // Scale the layout from the font metrics instead: Segoe UI 9pt is 16px high at 96 DPI.
            _scale = Math.Max(1f, Font.Height / 16f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(424), S(602));

            Add(Lbl("Location (for weather and sunset/sunrise)", 12, 10, 398));
            Place(_search, 12, 32, 290, 24);
            Place(_searchBtn, 310, 31, 100, 26);
            _searchBtn.Text = "Search";
            _searchBtn.Click += (s, e) => RunSearch();
            _search.Enter += (s, e) => AcceptButton = _searchBtn;
            _search.Leave += (s, e) => AcceptButton = _ok;
            Place(_results, 12, 64, 398, 72);
            _results.SelectedIndexChanged += (s, e) => PickResult();
            Place(_chosen, 12, 142, 330, 20);
            Place(_clear, 346, 138, 64, 24);
            _clear.Text = "Clear";
            _clear.Click += (s, e) => { _locName = null; _lat = _lon = null; ShowChosen(); };

            Check(_weather, "Show weather", 12, 172);
            Check(_battery, "Show battery (if any)", 220, 172);
            Check(_month, "Show month calendar", 12, 196);
            Check(_12h, "12-hour clock", 220, 196);
            Check(_events, "Show calendar events", 12, 220);
            Check(_fahrenheit, "Fahrenheit (°F)", 220, 220);
            _events.CheckedChanged += (s, e) => _ical.Enabled = _events.Checked;

            Add(Lbl("Calendar link (private iCal / webcal address)", 12, 252, 398));
            Place(_ical, 12, 272, 398, 24);

            Add(Lbl("Anti burn-in protection (OLED)", 12, 306, 300));
            _burnIn.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string name in BurnIn.LevelNames) _burnIn.Items.Add(name);
            Place(_burnIn, 12, 328, 160, 24);
            _burnInHint.Left = S(180);
            _burnInHint.Top = S(332);
            _burnInHint.Width = S(230);
            _burnInHint.Height = S(20);
            _burnInHint.AutoSize = false;
            Controls.Add(_burnInHint);
            _burnIn.SelectedIndexChanged += (s, e) => UpdateBurnInHint();

            Add(Lbl("Maximum clock brightness", 12, 364, 200));
            _maxBright.Minimum = 20;
            _maxBright.Maximum = 100;
            _maxBright.TickFrequency = 10;
            Place(_maxBright, 12, 386, 340, 40);
            Place(_maxBrightValue, 360, 392, 50, 20);
            _maxBright.Scroll += (s, e) => _maxBrightValue.Text = _maxBright.Value + "%";

            Add(Lbl("Brightness at night", 12, 438, 200));
            _night.Minimum = 10;
            _night.Maximum = 100;
            _night.TickFrequency = 10;
            Place(_night, 12, 460, 340, 40);
            Place(_nightValue, 360, 466, 50, 20);
            _night.Scroll += (s, e) => _nightValue.Text = _night.Value + "%";

            Add(Lbl("Night hours when sunrise/sunset isn't available:", 12, 510, 398));
            foreach (var p in new[] { _nightFrom, _nightTo })
            {
                p.Format = DateTimePickerFormat.Custom;
                p.CustomFormat = "HH:mm";
                p.ShowUpDown = true;
            }
            Place(_nightFrom, 12, 532, 70, 24);
            Add(Lbl("to", 92, 536, 28));
            Place(_nightTo, 124, 532, 70, 24);

            Place(_ok, 254, 564, 75, 28);
            _ok.Text = "OK";
            _ok.Click += (s, e) => Save();
            Place(_cancel, 335, 564, 75, 28);
            _cancel.Text = "Cancel";
            _cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = _ok;
            CancelButton = _cancel;

            LoadValues();
        }

        private void LoadValues()
        {
            _weather.Checked = _settings.ShowWeather;
            _battery.Checked = _settings.ShowBattery;
            _month.Checked = _settings.ShowMonthGrid;
            _events.Checked = _settings.ShowEvents;
            _12h.Checked = _settings.Use12Hour;
            _fahrenheit.Checked = _settings.UseFahrenheit;
            _ical.Text = _settings.ICalUrl ?? "";
            _ical.Enabled = _events.Checked;
            _burnIn.SelectedIndex = Math.Max(BurnIn.Off, Math.Min(BurnIn.High, _settings.BurnInLevel));
            UpdateBurnInHint();
            _maxBright.Value = Math.Max(20, Math.Min(100, _settings.MaxBrightnessPercent));
            _maxBrightValue.Text = _maxBright.Value + "%";
            _night.Value = Math.Max(10, Math.Min(100, _settings.NightBrightnessPercent));
            _nightValue.Text = _night.Value + "%";
            _nightFrom.Value = DateTime.Today + _settings.ManualNightStart;
            _nightTo.Value = DateTime.Today + _settings.ManualNightEnd;
            ShowChosen();
        }

        /// <summary>Explains what the selected burn-in level actually does to the drift.</summary>
        private void UpdateBurnInHint()
        {
            switch (_burnIn.SelectedIndex)
            {
                case BurnIn.Off:
                    _burnInHint.Text = "clock stays still";
                    break;
                case BurnIn.Low:
                    _burnInHint.Text = "small, slow movement";
                    break;
                case BurnIn.High:
                    _burnInHint.Text = "faster movement";
                    break;
                default:
                    _burnInHint.Text = "moves over the whole screen";
                    break;
            }
        }

        private void ShowChosen()
        {
            _chosen.Text = string.IsNullOrEmpty(_locName) || !_lat.HasValue
                ? "No location selected"
                : "Selected: " + _locName;
        }

        private void RunSearch()
        {
            string q = _search.Text;
            if (string.IsNullOrWhiteSpace(q)) return;
            _searchBtn.Enabled = false;
            _results.Items.Clear();
            _chosen.Text = "Searching…";
            Task.Run(() =>
            {
                List<GeoResult> found = Weather.Search(q);
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    _searchBtn.Enabled = true;
                    foreach (GeoResult g in found) _results.Items.Add(g);
                    if (found.Count == 0) _chosen.Text = "No results (or you are offline)";
                    else ShowChosen();
                }));
            });
        }

        private void PickResult()
        {
            var g = _results.SelectedItem as GeoResult;
            if (g == null) return;
            _locName = g.Name;
            _lat = g.Latitude;
            _lon = g.Longitude;
            ShowChosen();
        }

        private void Save()
        {
            string url = _ical.Text.Trim();
            if (_events.Checked && url.Length > 0 && !ICal.IsSecureUrl(url))
            {
                MessageBox.Show(this,
                    "The calendar link must start with https:// or webcal://.\n" +
                    "Plain http:// links are not accepted because the link is a secret and would be sent unencrypted.",
                    "DriftClock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // The cached calendar holds private events: drop it now if the link changed or events were turned off.
            string newUrl = url.Length == 0 ? null : url;
            bool linkChanged = ICal.NormalizeUrl(_settings.ICalUrl) != ICal.NormalizeUrl(newUrl);
            if (linkChanged || !_events.Checked) DataService.DeleteCalendarCache(DataService.DefaultCacheDir);

            _settings.LocationName = _locName;
            _settings.Latitude = _lat;
            _settings.Longitude = _lon;
            _settings.ShowWeather = _weather.Checked;
            _settings.ShowBattery = _battery.Checked;
            _settings.ShowMonthGrid = _month.Checked;
            _settings.ShowEvents = _events.Checked;
            _settings.Use12Hour = _12h.Checked;
            _settings.UseFahrenheit = _fahrenheit.Checked;
            _settings.ICalUrl = url.Length == 0 ? null : url;
            _settings.NightBrightnessPercent = _night.Value;
            _settings.MaxBrightnessPercent = _maxBright.Value;
            _settings.BurnInLevel = Math.Max(BurnIn.Off, Math.Min(BurnIn.High, _burnIn.SelectedIndex));
            _settings.ManualNightStart = _nightFrom.Value.TimeOfDay;
            _settings.ManualNightEnd = _nightTo.Value.TimeOfDay;

            try
            {
                _settings.Save(Settings.DefaultPath);
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save settings: " + ex.Message, "DriftClock",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---- layout helpers ----------------------------------------------------------------------------------

        private void Add(Control c) { Controls.Add(c); }

        private void Place(Control c, int x, int y, int w, int h)
        {
            c.SetBounds(S(x), S(y), S(w), S(h));
            Controls.Add(c);
        }

        private int S(int value)
        {
            return (int)Math.Round(value * _scale);
        }

        private Label Lbl(string text, int x, int y, int w)
        {
            return new Label { Text = text, Left = S(x), Top = S(y), Width = S(w), Height = S(20), AutoSize = false };
        }

        private void Check(CheckBox c, string text, int x, int y)
        {
            c.Text = text;
            Place(c, x, y, 190, 22);
        }
    }
}
