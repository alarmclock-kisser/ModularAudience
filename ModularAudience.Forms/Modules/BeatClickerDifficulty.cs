namespace ModularAudience.Forms.Modules
{
    /// <summary>
    /// Difficulty levels for the Beat Clicker Game. The selected level is stored in a
    /// static field so it persists for the entire app run and applies to every track
    /// in every collection (all context menus share the same setting).
    /// </summary>
    public static class BeatClickerDifficulty
    {
        public static readonly string[] Levels =
        [
            "Beginner",
            "Easy",
            "Moderate",
            "Intermediate",
            "Advanced",
            "Hard",
            "\u062C\u0647\u0646\u0645"
        ];

        public static int SelectedIndex { get; set; } = 2;

        public static string SelectedName => Levels[SelectedIndex];

        /// <summary>
        /// Instance-wide (in-memory, NOT persisted to disk) flag for the "Create Debug Log"
        /// option in the Beat Clicker Game context menu. When enabled, the game writes a
        /// verbose, millisecond-precise debug log of every element-generation decision and
        /// every player input event to a .TXT file. It is a plain static so it is shared
        /// across every track and every AudioCollectionView for the duration of the app run,
        /// and resets to false on the next app start (no JSON, no disk persistence).
        /// </summary>
        public static bool DebugLogEnabled { get; set; }

        public static float Density(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 0.45f;
                case 1: return 0.58f;
                case 2: return 0.72f;
                case 3: return 0.82f;
                case 4: return 0.90f;
                case 5: return 0.96f;
                default: return 1.00f;
            }
        }

        public static int MinGroupCooldownBeats(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 3;
                case 1: return 2;
                case 2: return 1;
                case 3: return 1;
                case 4: return 1;
                case 5: return 1;
                default: return 0;
            }
        }

        public static int MaxGroupCooldownBeats(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 4;
                case 1: return 3;
                case 2: return 2;
                case 3: return 2;
                case 4: return 1;
                case 5: return 1;
                default: return 1;
            }
        }

        public static int MinGroupSize(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 4;
                case 1: return 5;
                case 2: return 6;
                case 3: return 8;
                case 4: return 10;
                case 5: return 12;
                default: return 14;
            }
        }

        public static int MaxGroupSize(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 12;
                case 1: return 16;
                case 2: return 24;
                case 3: return 30;
                case 4: return 36;
                case 5: return 44;
                default: return 52;
            }
        }

        public static float SpinnerChance(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 0.05f;
                case 1: return 0.10f;
                case 2: return 0.18f;
                case 3: return 0.25f;
                case 4: return 0.35f;
                case 5: return 0.45f;
                default: return 0.60f;
            }
        }

        public static float SliderChance(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 0.10f;
                case 1: return 0.20f;
                case 2: return 0.30f;
                case 3: return 0.40f;
                case 4: return 0.50f;
                case 5: return 0.60f;
                default: return 0.75f;
            }
        }

        public static float MinTimeGapSeconds(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 0.35f;
                case 1: return 0.30f;
                case 2: return 0.25f;
                case 3: return 0.20f;
                case 4: return 0.15f;
                case 5: return 0.12f;
                default: return 0.08f;
            }
        }

        public static float TaikoKaChance(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 0.20f;
                case 1: return 0.30f;
                case 2: return 0.40f;
                case 3: return 0.50f;
                case 4: return 0.60f;
                case 5: return 0.70f;
                default: return 0.85f;
            }
        }

        public static float TaikoRestChance(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 0.00f;
                case 1: return 0.05f;
                case 2: return 0.10f;
                case 3: return 0.15f;
                case 4: return 0.20f;
                case 5: return 0.25f;
                default: return 0.35f;
            }
        }

        public static int HitWindowEarlyMs(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 180;
                case 1: return 150;
                case 2: return 120;
                case 3: return 90;
                case 4: return 60;
                case 5: return 40;
                default: return 20;
            }
        }

        public static int HitWindowLateMs(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 200;
                case 1: return 160;
                case 2: return 120;
                case 3: return 80;
                case 4: return 50;
                case 5: return 35;
                default: return 20;
            }
        }

        // ─── Per-difficulty element sizes and spacing ─────────────────────────
        // The harder the level, the smaller the elements and the tighter the spacing,
        // clearance, and spinner quiet zones — so hitting gets progressively harder.
        // Beginner uses the largest/most generous values.

        /// <summary>Hit-circle radius in px (Beginner 60 -> Hölle 32).</summary>
        public static float CircleRadius(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 60f;
                case 1: return 57.5f;
                case 2: return 55f;
                case 3: return 50f;
                case 4: return 45f;
                case 5: return 40f;
                default: return 32f;
            }
        }

        /// <summary>Spinner ring radius in px (scales with the circle radius).</summary>
        public static float SpinnerRadius(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 80f;
                case 1: return 77f;
                case 2: return 74f;
                case 3: return 68f;
                case 4: return 62f;
                case 5: return 56f;
                default: return 46f;
            }
        }

        /// <summary>Min center-to-center distance between elements in px (Beginner 150 -> Hölle 90).</summary>
        public static float MinElementDistance(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 150f;
                case 1: return 140f;
                case 2: return 130f;
                case 3: return 120f;
                case 4: return 110f;
                case 5: return 100f;
                default: return 90f;
            }
        }

        /// <summary>Min distance from a slider track to other element centers in px (Beginner 120 -> Hölle 70).</summary>
        public static float SliderClearance(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 120f;
                case 1: return 112f;
                case 2: return 104f;
                case 3: return 96f;
                case 4: return 88f;
                case 5: return 80f;
                default: return 70f;
            }
        }

        /// <summary>Spinner quiet zone in beats (no elements this many beats before/after a spinner).
        /// Denser (1 beat) on the hardest levels, 2 beats otherwise.</summary>
        public static float SpinnerQuietBeats(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 5:
                case 6: return 1f;
                default: return 2f;
            }
        }

        /// <summary>Spinner lifetime in beats (the ring shrinks over this span). Longer on the
        /// easier levels (more time to complete a rotation), shorter on the hardest. The actual
        /// duration is max(3.0s, this many beats) so it can stretch well past 3s on slow tracks.</summary>
        public static float SpinnerDurationBeats(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 8f;
                case 1: return 7f;
                case 2: return 6f;
                case 3: return 5f;
                case 4: return 4f;
                case 5: return 4f;
                default: return 3f;
            }
        }

        /// <summary>Min slider length in px (start/end must not touch; scales with the circle radius).</summary>
        public static float MinSliderLength(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 120f;
                case 1: return 115f;
                case 2: return 110f;
                case 3: return 100f;
                case 4: return 90f;
                case 5: return 80f;
                default: return 65f;
            }
        }

        /// <summary>Max slider length in px (scales with the circle radius).</summary>
        public static float MaxSliderLength(int index)
        {
            index = System.Math.Clamp(index, 0, Levels.Length - 1);
            switch (index)
            {
                case 0: return 240f;
                case 1: return 230f;
                case 2: return 220f;
                case 3: return 200f;
                case 4: return 180f;
                case 5: return 160f;
                default: return 130f;
            }
        }
    }
}
