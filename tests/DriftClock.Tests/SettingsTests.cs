using System;
using System.IO;
using Xunit;

namespace DriftClock.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void Roundtrip_preserves_values_and_encrypts_url()
        {
            var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini");
            var s = new Settings
            {
                LocationName = "Milan", Latitude = 45.46, Longitude = 9.19, Use12Hour = true,
                ShowEvents = true, ICalUrl = "https://x/secret.ics", NightBrightnessPercent = 20
            };
            s.Save(p);
            Assert.DoesNotContain("secret", File.ReadAllText(p));
            var r = Settings.Load(p);
            Assert.Equal("Milan", r.LocationName);
            Assert.Equal(45.46, r.Latitude);
            Assert.True(r.Use12Hour);
            Assert.Equal("https://x/secret.ics", r.ICalUrl);
            Assert.Equal(20, r.NightBrightnessPercent);
        }

        [Fact]
        public void Missing_or_corrupt_file_yields_defaults()
        {
            Assert.Equal(35, Settings.Load(@"C:\nope\none.ini").NightBrightnessPercent);
            var p = Path.GetTempFileName();
            File.WriteAllText(p, "garbage\n=\nNightBrightnessPercent=abc");
            Assert.Equal(35, Settings.Load(p).NightBrightnessPercent);
        }

        [Fact]
        public void Brightness_is_clamped()
        {
            var p = Path.GetTempFileName();
            File.WriteAllText(p, "NightBrightnessPercent=500");
            Assert.Equal(100, Settings.Load(p).NightBrightnessPercent);
            File.WriteAllText(p, "NightBrightnessPercent=1");
            Assert.Equal(10, Settings.Load(p).NightBrightnessPercent);
        }

        [Fact]
        public void Defaults_match_spec()
        {
            var s = new Settings();
            Assert.True(s.ShowWeather);
            Assert.True(s.ShowBattery);
            Assert.False(s.ShowMonthGrid);
            Assert.False(s.ShowEvents);
            Assert.False(s.Use12Hour);
            Assert.False(s.UseFahrenheit);
            Assert.Equal(TimeSpan.FromHours(23), s.ManualNightStart);
            Assert.Equal(TimeSpan.FromHours(7), s.ManualNightEnd);
            Assert.Equal(100, s.MaxBrightnessPercent);
            Assert.Equal(BurnIn.Normal, s.BurnInLevel);
        }

        [Fact]
        public void Burn_in_level_and_max_brightness_roundtrip()
        {
            var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini");
            var s = new Settings { BurnInLevel = BurnIn.High, MaxBrightnessPercent = 55 };
            s.Save(p);
            var r = Settings.Load(p);
            Assert.Equal(BurnIn.High, r.BurnInLevel);
            Assert.Equal(55, r.MaxBrightnessPercent);
        }

        [Fact]
        public void Burn_in_level_and_max_brightness_are_clamped()
        {
            var p = Path.GetTempFileName();
            File.WriteAllText(p, "BurnInLevel=99\nMaxBrightnessPercent=500");
            Assert.Equal(BurnIn.High, Settings.Load(p).BurnInLevel);
            Assert.Equal(100, Settings.Load(p).MaxBrightnessPercent);
            File.WriteAllText(p, "BurnInLevel=-5\nMaxBrightnessPercent=1");
            Assert.Equal(BurnIn.Off, Settings.Load(p).BurnInLevel);
            Assert.Equal(20, Settings.Load(p).MaxBrightnessPercent);
        }
    }
}
