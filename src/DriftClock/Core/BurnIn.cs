namespace DriftClock
{
    /// <summary>
    /// How hard the picture works against OLED burn-in. The level scales how much of the free screen area the
    /// clock is allowed to use and how fast it moves, so "High" sweeps the whole panel.
    /// </summary>
    public static class BurnIn
    {
        public const int Off = 0;
        public const int Low = 1;
        public const int Normal = 2;
        public const int High = 3;

        public static readonly string[] LevelNames = { "Off", "Low", "Normal", "High" };

        /// <summary>Fraction of the free screen area the clock may roam over. 0 means it never moves.</summary>
        public static float RangeFactor(int level)
        {
            switch (level)
            {
                case Off: return 0f;
                case Low: return 0.45f;
                case High: return 1f;
                default: return 1f;
            }
        }

        /// <summary>Multiplier on the drift speed. 0 means the picture never moves.</summary>
        public static float SpeedFactor(int level)
        {
            switch (level)
            {
                case Off: return 0f;
                case Low: return 0.7f;
                case High: return 1.8f;
                default: return 1f;
            }
        }

        public static string NameOf(int level)
        {
            return level >= 0 && level < LevelNames.Length ? LevelNames[level] : LevelNames[Normal];
        }
    }
}
