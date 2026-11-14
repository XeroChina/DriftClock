using System;
using System.Drawing;
using System.Windows.Forms;

namespace DriftClock
{
    /// <summary>
    /// Fullscreen window for one monitor. The primary monitor draws the clock and drifts it; every other
    /// monitor is pure black.
    /// </summary>
    internal sealed class SaverForm : Form
    {
        private static readonly DateTime Epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly Screen _screen;
        private readonly bool _primary;
        private readonly DataService _data;
        private readonly Settings _settings;
        private readonly float _driftSpeed;
        private ClockRenderer _renderer;
        private Timer _timer;
        private DateTime _now;
        private ScreenState _state;
        private DriftSample _drift;
        private string _key;

        public SaverForm(Screen screen, bool primary, DataService data, Settings settings)
        {
            _screen = screen;
            _primary = primary;
            _data = data;
            _settings = settings;
            // How fast the picture walks along its path. The travel distance itself is decided by the renderer,
            // which knows how much free space the clock leaves on the screen; "Off" keeps everything completely still.
            _driftSpeed = BurnIn.SpeedFactor(settings.BurnInLevel);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            BackColor = Color.Black;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Bounds = _screen.Bounds;
            if (!_primary) return;

            _renderer = new ClockRenderer();
            Tick();
            // The clock drifts several pixels per second, so it has to be repainted a few times per second;
            // a one-second tick would make the motion look like a series of jumps.
            _timer = new Timer { Interval = 250 };
            _timer.Tick += (s, a) => Tick();
            _timer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = _screen.Bounds;
            Cursor.Hide();
        }

        private void Tick()
        {
            DateTime now = DateTime.Now;
            // Quantised to the repaint interval so the picture moves in small steps instead of teleporting.
            double minutes = Math.Floor((now.ToUniversalTime() - Epoch).TotalMilliseconds / 250.0) * 250.0 / 60000.0;
            DriftSample drift = DriftPath.Sample(minutes, _driftSpeed);
            ScreenState state = _data.Current;

            string key = _renderer.RenderKey(now, state, drift);
            _now = now;
            _state = state;
            _drift = drift;
            if (key == _key) return;
            _key = key;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Black);
            if (_renderer != null)
                _renderer.Draw(e.Graphics, ClientRectangle, _now, _state, _settings, _drift);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // OnPaint clears; skipping the background pass avoids flicker.
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) _timer.Dispose();
                if (_renderer != null) _renderer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Closes the saver on key presses, clicks, or a real mouse movement anywhere in the process.</summary>
    internal sealed class SaverInput : IMessageFilter
    {
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204,
                          WM_MBUTTONDOWN = 0x0207, WM_MOUSEWHEEL = 0x020A, WM_XBUTTONDOWN = 0x020B;

        private readonly ExitDetector _exit = new ExitDetector();

        public bool PreFilterMessage(ref Message m)
        {
            bool quit = false;
            switch (m.Msg)
            {
                case WM_KEYDOWN:
                case WM_SYSKEYDOWN:
                case WM_LBUTTONDOWN:
                case WM_RBUTTONDOWN:
                case WM_MBUTTONDOWN:
                case WM_XBUTTONDOWN:
                case WM_MOUSEWHEEL:
                    quit = _exit.OnKeyOrClick();
                    break;
                case WM_MOUSEMOVE:
                    quit = _exit.OnMouseMove(Cursor.Position);
                    break;
            }
            if (quit) Application.Exit();
            return false;
        }
    }
}
