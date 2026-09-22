using ModularAudience.Audio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules
{
    /// <summary>
    /// Fullscreen beat-clicker game supporting Classic gameplay (circles, sliders, spinners)
    /// and Taiko mode. Uses a layered window for true per-pixel transparency: the configurable
    /// black background is translucent while game elements are fully opaque.
    /// Swallows all mouse input. Supports an ESC pause menu (Continue / Restart / Exit).
    /// </summary>
    public partial class BeatClickerGameForm : Form
    {
        private readonly AudioObj _audio;
        private readonly System.Windows.Forms.Timer _gameTimer;
        private readonly Stopwatch _gameClock = new();
        private readonly object _lock = new();
        private readonly bool _isTaikoMode;
        private readonly int _difficultyIndex;

        private List<BeatCatchHitObject> _hitObjects = [];
        private List<TaikoBeat> _taikoBeats = [];
        private int _currentHitIndex;
        private int _score;
        private double _scorePoints;
        private int _missed;
        private double _timingOffsetSumMs;
        private int _timingSampleCount;
        private float _lastTimingOffsetMs;
        private Color _lastTimingColor = Color.White;
        private bool _hasTimingFeedback;
        private bool _lastTimingWasMiss;
        private bool _gameRunning;
        private bool _paused;
        private float _bpm;
        private float _totalDuration;

        // Combo streak (Classic): current streak, best streak, and a flag to
        // show the results dialog once when the last object has been resolved.
        private int _comboStreak;
        private int _bestComboStreak;
        private bool _resultsShown;
        private bool _runHasStarted;
        private bool _runSummarySaved;
        private DateTimeOffset _runStartedUtc;

        private const double ScoreBasePoints = 1000d;
        private const double ScoreStreakGrowth = 1.15d;

        private CancellationTokenSource? _playbackCts;
        private BeatClickerPauseMenuForm? _pauseMenu;
        private bool _audioPausedByGame;
        // Audio output latency in ms (measured once in StartGame). The hit windows are
        // shifted by this amount so the optimal click time lines up with the heard beat.
        private float _audioLatencyMs;

        // Verbose debug logging (enabled via the "Create Debug Log" context-menu entry).
        // Null when disabled. All timestamps are relative to the game clock (00:00:000 at start).
        private BeatClickerDebugLog? _debugLog;
        private bool _debugLogOpened;

        // Count-in (3-2-1) before the game starts: the background darkens from 33% to 48%,
        // the numbers advance every two track beats, then the background returns to 33%.
        private bool _countInActive;
        private float _countInStart;
        private float _countInPausedElapsed; // count-in elapsed time captured when paused (to resume in place)
        private bool _beatmapReady;
        private const float CountInFadeSeconds = 0.45f;
        private const float CountInBeatsPerNumber = 2f;
        private const int CountInExtraOpacityPercent = 15;

        // Esc debounce: the pause menu is opened on Esc key-down. The key-up is a safe
        // boundary (it does NOT toggle). The menu only closes on a NEW Esc key-down that
        // occurs after the key was released, so holding Esc never rapidly toggles.
        private bool _escKeyReleased;

        // Re-pause guard: when the menu is closed by an Esc key-down (Continue), the menu's
        // FormClosing handler auto-resumes the game. The global hotkey then runs and re-pauses
        // (because the key is still held). This flag lets the hotkey re-pause exactly once so
        // the game doesn't briefly resume and re-pause (the "flicker" bug). Cleared on the next
        // Esc key-up or when the menu is closed by any other means (button click, Alt+F4).
        private bool _escCloseRepausePending;

        // Slider tracking state
        private int _activeSliderIndex = -1;
        private bool _sliderStartHit;
        // True once the mouse has actually moved while the slider is held. A slider
        // that is only clicked (start point) and never dragged must be a miss, not a hit.
        private bool _sliderDragged;
        private float _sliderMaxProgress;
        private bool _sliderPathValid;
        private bool _leftButtonHeld;
        private int _completedSliderIndex = -1;
        private float _completedSliderProgress;
        private float _completedSliderHitTime;
        private int _failedSliderIndex = -1;

        // Classic hit window tolerances (set by difficulty in the constructor)
        private int _hitWindowEarlyMs = 100;  // max ms before hit time a click is valid
        private int _hitWindowLateMs = 30;    // max ms after hit time a click is valid
        // The approach window deliberately overlaps nearby timings so several upcoming
        // objects can be visible at once. Interaction timing remains exclusive per object.
        private const float ApproachSeconds = 1.0f;
        private const float PostHitFadeSeconds = 0.5f;
        private const float CompletedSliderFadeSeconds = 0.18f;
        private const float CompletedSliderWhiteShiftSeconds = 0.06f;
        private float _sliderProgressTolerance = 0.13f;
        // Element sizes and spacing are PER-DIFFICULTY (set in the constructor): the harder the
        // level, the smaller the elements and the tighter the spacing/clearance/intervals, so
        // hitting gets progressively harder. Beginner uses the largest/most generous values.
        private float _circleRadius = 60f;              // hit-circle radius (Beginner 60 -> H�lle 32)
        private float _spinnerRadius = 80f;             // spinner ring radius (scales with circle radius)
        private float _minElementDistance = 150f;       // min center-to-center distance (Beginner 150 -> H�lle 90)
        private float _sliderClearance = 120f;          // min distance from a slider track to other elements
        private float _spinnerQuietBeats = 2f;          // no elements this many beats before/after a spinner
        private float _minSliderLength = 150f;          // min slider length (start/end must stay visibly separated)
        private float _maxSliderLength = 420f;          // max slider length
        private float _minTimeGapSeconds = 0.25f;       // min time gap between elements (set by difficulty)

        // Taiko hit window tolerances (set by difficulty in the constructor)
        private int _taikoHitWindowEarlyMs = 100;
        private int _taikoHitWindowLateMs = 30;

        // Background opacity is shared through BeatClickerSettings and defaults to 33%.
        private int _backgroundOpacityPercent = BeatClickerSettings.DefaultBackgroundOpacityPercent;

        // Fail animation state
        private struct FailEffect
        {
            public float X, Y;
            public float StartTime;
        }
        private readonly List<FailEffect> _failEffects = [];
        private const float FailEffectDuration = 0.6f;

        private struct HitEffect
        {
            public float X, Y;
            public float EndX, EndY;
            public bool IsSlider;
            public float StartTime;
        }
        private readonly List<HitEffect> _hitEffects = [];
        private const float HitEffectDuration = 0.28f;

        // Spinner mouse tracking
        private int _activeSpinnerIndex = -1;
        private int _mouseX, _mouseY;
        private float _spinnerLastAngle;
        private float _spinnerAccumulatedAngle;
        private int _spinnerCombo;
        private bool _spinnerRotationCompleted;
        private bool _spinnerHitRegistered;
        // Idle spin: while the spinner is NOT grabbed, its indicator slowly rotates on its
        // own (for visual interest). This is the current idle angle; it is seeded from the
        // mouse angle when the player grabs the spinner so the hand-off is seamless.
        private float _spinnerIdleAngle;

        // Combo popup (green "xN" shown at the spinner center, fades out quickly)
        private struct ComboPopup
        {
            public int Value;
            public float X, Y;
            public float StartTime;
        }
        private readonly List<ComboPopup> _comboPopups = [];
        private const float ComboPopupDuration = 0.7f;

        // Procedural pattern library for 64 logical patterns and remixes.
        private readonly BeatClickerPatternLibrary _patternLibrary = new();

        // Win32 message constants for WndProc
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_ESC = 1;
        private const uint MOD_NONE = 0x0000;
        private const int VK_ESCAPE = 0x1B;
        private const int HOTKEY_ID_ESC_UP = 2;
        private const uint MOD_SHIFT = 0x0004;

        // Window tracking for minimize/restore
        private readonly List<IntPtr> _minimizedWindows = [];
        private readonly List<FormWindowState> _previousWindowStates = [];

        // Layered window P/Invoke
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(
            IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int CX, CY; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;       // AC_SRC_OVER = 0
            public byte BlendFlags;    // 0
            public byte SourceConstantAlpha; // 255 = fully opaque
            public byte AlphaFormat;   // AC_SRC_ALPHA = 1
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int ULW_ALPHA = 2;
        private const byte AC_SRC_OVER = 0;
        private const byte AC_SRC_ALPHA = 1;

        public BeatClickerGameForm(AudioObj audio, bool taikoMode = false, int difficultyIndex = 2)
        {
            _audio = audio;
            _isTaikoMode = taikoMode;
            _difficultyIndex = Math.Clamp(difficultyIndex, 0, BeatClickerDifficulty.Levels.Length - 1);
            _minTimeGapSeconds = BeatClickerDifficulty.MinTimeGapSeconds(_difficultyIndex);
            _hitWindowEarlyMs = BeatClickerDifficulty.HitWindowEarlyMs(_difficultyIndex);
            _hitWindowLateMs = BeatClickerDifficulty.HitWindowLateMs(_difficultyIndex);
            _sliderProgressTolerance = BeatClickerDifficulty.SliderProgressTolerance(_difficultyIndex);
            _taikoHitWindowEarlyMs = BeatClickerDifficulty.HitWindowEarlyMs(_difficultyIndex);
            _taikoHitWindowLateMs = BeatClickerDifficulty.HitWindowLateMs(_difficultyIndex);
            // Per-difficulty element sizes and spacing: harder = smaller elements, tighter
            // spacing/clearance, and denser spinner quiet zones (see BeatClickerDifficulty).
            _circleRadius = BeatClickerDifficulty.CircleRadius(_difficultyIndex);
            _spinnerRadius = BeatClickerDifficulty.SpinnerRadius(_difficultyIndex);
            _minElementDistance = BeatClickerDifficulty.MinElementDistance(_difficultyIndex);
            _sliderClearance = BeatClickerDifficulty.SliderClearance(_difficultyIndex);
            _spinnerQuietBeats = BeatClickerDifficulty.SpinnerQuietBeats(_difficultyIndex);
            _minSliderLength = BeatClickerDifficulty.MinSliderLength(_difficultyIndex);
            _maxSliderLength = BeatClickerDifficulty.MaxSliderLength(_difficultyIndex);

            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = Screen.PrimaryScreen.Bounds;
            this.BackColor = Color.Black;
            this.ShowInTaskbar = false;
            this.TopMost = false;
            this.KeyPreview = true;
            this.DoubleBuffered = true;
            this.ShowInTaskbar = false;

            _gameTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _gameTimer.Tick += GameTimer_Tick;

            this.KeyDown += BeatClickerGameForm_KeyDown;
            this.KeyUp += BeatClickerGameForm_KeyUp;
            this.MouseDown += GameForm_MouseDown;
            this.MouseMove += GameForm_MouseMove;
            this.MouseUp += GameForm_MouseUp;
        }

        private void LoadBackgroundOpacity()
        {
            _backgroundOpacityPercent = BeatClickerSettings.LoadBackgroundOpacityPercent();
        }

        private int BackgroundOpacityPercent => _backgroundOpacityPercent;

        private void SetBackgroundOpacityPercent(int percent)
        {
            _backgroundOpacityPercent = Math.Clamp(
                percent,
                BeatClickerSettings.MinBackgroundOpacityPercent,
                BeatClickerSettings.MaxBackgroundOpacityPercent);
            BeatClickerSettings.SaveBackgroundOpacityPercent(_backgroundOpacityPercent);

            try { PushLayeredBitmap(); }
            catch (Exception ex) { Debug.WriteLine($"BeatClickerGame opacity render error: {ex.Message}"); }
        }

        private int GetBackgroundAlpha()
        {
            int opacityPercent = _backgroundOpacityPercent;
            if (_countInActive)
            {
                opacityPercent = Math.Min(
                    BeatClickerSettings.MaxBackgroundOpacityPercent,
                    opacityPercent + CountInExtraOpacityPercent);
            }

            return (int)Math.Round(opacityPercent * 255.0 / 100.0);
        }

        /// <summary>
        /// Runs an audio operation on the UI thread (NAudio's WaveOut is COM-affine to the
        /// thread that created it). Falls back to running inline if the UI thread is not
        /// available (e.g. during disposal).
        /// </summary>
        private void RunAudioOnUiThread(Func<Task> action)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke(new Action(async () =>
                {
                    try { await action().ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Debug.WriteLine($"BeatClickerGame audio error: {ex.Message}"); }
                }));
            }
            else
            {
                try { action().GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Debug.WriteLine($"BeatClickerGame audio error: {ex.Message}"); }
            }
        }

        public void StartGame()
        {
            LoadBackgroundOpacity();
            MinimizeAllWindowsOnScreen();

            // Always restart the audio from the very beginning. The track may already be
            // playing in the main app (the user previewed it), in which case PlayAsync alone
            // is a no-op and the audio would keep playing from its current position while
            // the game clock starts at 0 — producing a fixed offset (e.g. ~2s) between the
            // heard beat and the beatmap. Stopping first resets the position to 0 so the
            // game clock and the audio are aligned from the start.
            _playbackCts?.Cancel();
            _playbackCts = new CancellationTokenSource();
            // NAudio's WaveOut is a COM object bound to the thread that created it. All
            // audio calls (Play/Pause/Resume/Stop) must run on the UI thread, otherwise the
            // audio stutters and pause/resume becomes unreliable (this was the Esc bug).
            RunAudioOnUiThread(() => _audio.StopAsync());
            RunAudioOnUiThread(() => _audio.PlayAsync(_playbackCts.Token));

            // Generate the beatmap asynchronously so the UI never freezes. The count-in
            // gives the background task time to finish before the first playable beat.
            _beatmapReady = false;
            Task.Run(() =>
            {
                try { GenerateBeatMap(); }
                catch (Exception ex) { Debug.WriteLine($"BeatClickerGame beatmap generation error: {ex.Message}"); }
                this.BeginInvoke(new Action(() => { _beatmapReady = true; }));
            });

            ResetGame();
            _runHasStarted = true;
            _runStartedUtc = DateTimeOffset.UtcNow;
            _gameTimer.Start();
            _gameRunning = true;
            _paused = false;

            // Open the verbose debug log (if enabled) so every generation decision and
            // player input event is recorded to a .TXT file for analysis.
            if (BeatClickerDifficulty.DebugLogEnabled && _debugLog == null)
            {
                _debugLog = BeatClickerDebugLog.Create(
                    _audio.Name,
                    BeatClickerDifficulty.Levels[_difficultyIndex],
                    _isTaikoMode,
                    _audio.SampleRate,
                    _audio.Data,
                    _audio.Channels);
                _debugLogOpened = _debugLog != null;
                if (_debugLog != null)
                {
                    _debugLog.LogEvent(0f, $"Game started (mode={(_isTaikoMode ? "taiko" : "classic")}, difficulty={BeatClickerDifficulty.Levels[_difficultyIndex]})");
                }
            }

            // Start the count-in: the game clock stays stopped until the 3-2-1 count-in
            // finishes, so the first hit object's time (beatInterval) lands right after "1".
            _countInActive = true;
            _countInStart = (float)Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency;

            // Measure the audio output latency once (the first call is expensive, ~100ms).
            // The hit windows are shifted by this amount so the optimal click time lines
            // up with the actually heard beat instead of the (earlier) sample position.
            try { _audioLatencyMs = _audio.GetLatencyMs(); }
            catch { _audioLatencyMs = 0f; }

            this.Show();

            // Enable layered window for per-pixel transparency
            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
            SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);

            this.Activate();
            this.Focus();
            PushLayeredBitmap();

            // Register global ESC hotkeys. This is the reliable way to catch Esc: it works
            // regardless of which window has keyboard focus (the layered game window and the
            // pause menu have unreliable focus routing). The hotkey fires WM_HOTKEY here.
            // The key-UP hotkey (Shift+Esc) is a safe boundary: it only marks the key as
            // released so a subsequent (new) Esc key-down can close the pause menu. Without
            // it, the menu could never be closed by Esc while the pause menu has focus.
            RegisterHotKey(this.Handle, HOTKEY_ID_ESC, MOD_NONE, VK_ESCAPE);
            RegisterHotKey(this.Handle, HOTKEY_ID_ESC_UP, MOD_SHIFT, VK_ESCAPE);

            // Ensure the form has keyboard focus after the window is fully shown
            this.BeginInvoke(new Action(() =>
            {
                this.Activate();
                this.Focus();
            }));
        }

        // --- Window minimize/restore -------------------------------------------

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private void MinimizeAllWindowsOnScreen()
        {
            _minimizedWindows.Clear();
            _previousWindowStates.Clear();

            Rectangle screenBounds = Screen.PrimaryScreen.Bounds;

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                {
                    return true;
                }

                // Skip tool windows and our own form
                int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                {
                    return true;
                }

                if (hWnd == this.Handle)
                {
                    return true;
                }

                if (!GetWindowRect(hWnd, out RECT rect))
                {
                    return true;
                }

                // Check if window intersects the primary screen
                int right = rect.Right, bottom = rect.Bottom;
                int left = rect.Left, top = rect.Top;
                bool intersects = right > screenBounds.Left && left < screenBounds.Right
                    && bottom > screenBounds.Top && top < screenBounds.Bottom;

                if (intersects)
                {
                    _minimizedWindows.Add(hWnd);
                    ShowWindow(hWnd, SW_MINIMIZE);
                }

                return true;
            }, IntPtr.Zero);
        }

        private void RestoreAllWindows()
        {
            foreach (IntPtr hWnd in _minimizedWindows)
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            _minimizedWindows.Clear();
        }

        // --- Beatmap generation -----------------------------------------------

        private void GenerateBeatMap()
        {
            _hitObjects = [];
            _taikoBeats = [];
            _bpm = _audio.Bpm > 0 ? _audio.Bpm : 120f;
            _totalDuration = _audio.Data.Length > 0
                ? (float)(_audio.Data.Length / Math.Max(1, _audio.Channels)) / Math.Max(1, _audio.SampleRate)
                : 0f;

            if (_totalDuration <= 0)
            {
                return;
            }

            if (_isTaikoMode)
            {
                GenerateTaikoBeats();
            }
            else
            {
                GenerateBeatCatchClassicBeatmap();
            }
        }

        private void GenerateBeatCatchClassicBeatmap()
        {
            float beatInterval = 60f / _bpm;
            int screenW = Screen.PrimaryScreen.Bounds.Width;
            int screenH = Screen.PrimaryScreen.Bounds.Height;
            int margin = 150;

            // Difficulty parameters
            float density = BeatClickerDifficulty.Density(_difficultyIndex);
            int minCooldown = BeatClickerDifficulty.MinGroupCooldownBeats(_difficultyIndex);
            int maxCooldown = BeatClickerDifficulty.MaxGroupCooldownBeats(_difficultyIndex);
            int minGroupSize = BeatClickerDifficulty.MinGroupSize(_difficultyIndex);
            int maxGroupSize = BeatClickerDifficulty.MaxGroupSize(_difficultyIndex);
            float spinnerChance = BeatClickerDifficulty.SpinnerChance(_difficultyIndex);
            float sliderChance = BeatClickerDifficulty.SliderChance(_difficultyIndex);
            float patternChance = BeatClickerDifficulty.PatternChance(_difficultyIndex);
            float placementGap = _difficultyIndex >= 2 ? _minTimeGapSeconds : beatInterval;

            _debugLog?.LogSection(0f, $"classic beatmap generation (BPM={_bpm:F2}, beatInterval={beatInterval:F4}s, totalBeats={(int)(_totalDuration / beatInterval)})");
            _debugLog?.Log(0f, $"params: density={density:F2} minCooldown={minCooldown} maxCooldown={maxCooldown} groupSize={minGroupSize}-{maxGroupSize} spinnerChance={spinnerChance:F2} sliderChance={sliderChance:F2} patternChance={patternChance:F2} placementGap={placementGap:F3}s minTimeGap={_minTimeGapSeconds:F3}s minElementDist={_minElementDistance:F0} sliderClearance={_sliderClearance:F0} spinnerQuietBeats={_spinnerQuietBeats:F1}");

            // True randomness: no seed. Every playthrough generates a fresh beatmap.
            var rng = new Random();

            // Pre-compute a per-beat energy profile (RMS of a 100ms window) so element
            // placement can react to the track's dynamics.
            int totalBeats = (int)(_totalDuration / beatInterval);
            var energy = new float[totalBeats];
            for (int i = 0; i < totalBeats; i++)
            {
                energy[i] = GetEnergyAtTime(i * beatInterval);
            }

            // Beat-aligned placement: elements are placed ON the track's beats (the
            // strongest onsets in a small window around each beat), so the optimal click
            // timing lines up with the actually heard kick/beat instead of being offset.
            var beatTimes = new List<float>(totalBeats);
            for (int i = 0; i < totalBeats; i++)
            {
                beatTimes.Add(SnapToOnset(i * beatInterval, beatInterval, totalBeats, energy));
            }

            // Elements follow a meandering line (a smooth random walk) instead of being
            // scattered randomly, so the player can keep track of where to look.
            float cx = screenW / 2f;
            float cy = screenH / 2f;
            float heading = (float)(rng.NextDouble() * Math.PI * 2);

            int groupNumber = 0;
            int groupCount = 0;
            // Keep the first playable object after the BPM-based count-in. The game clock
            // already follows the audio timeline while the count-in is displayed.
            int countInBeats = (int)Math.Ceiling(GetCountInDurationSeconds() / beatInterval);
            int beatIndex = Math.Max(2, countInBeats + 1);
            float lastPlacedTime = 0f;

            // Keep the last playable element at least 1.5s before the track ends so the
            // player can actually reach it (the approach window is 1s and the post-hit
            // fade is 0.5s). Without this, the final elements land in the last second of
            // the track and are effectively unplayable.
            int lastPlayableBeat = (int)Math.Floor((_totalDuration - 1.5f) / beatInterval);

            while (beatIndex < totalBeats)
            {
                groupNumber++;
                groupCount = 0;
                int groupTargetSize = rng.Next(minGroupSize, maxGroupSize + 1);
                _debugLog?.Log(beatTimes[beatIndex], $"GROUP START group={groupNumber} targetSize={groupTargetSize}");

                while (beatIndex < totalBeats && groupCount < groupTargetSize)
                {
                    // Stop placing new elements once the beat is past the last playable
                    // time (1.5s before the track ends). This prevents the final elements
                    // from landing in the last second of the track where they are unplayable.
                    if (beatIndex > lastPlayableBeat)
                    {
                        break;
                    }

                    float t = beatTimes[beatIndex];
                    int idx = Math.Clamp(beatIndex, 0, totalBeats - 1);
                    float e = energy[idx];

                    _debugLog?.Log(t, $"beat {beatIndex} (group {groupNumber}, count {groupCount})  nominal={beatIndex * beatInterval:F4}s snapped={t:F4}s  energy={e:F3}  lastPlaced={lastPlacedTime:F4}s");

                    // Beginner and Easy keep a full-beat reaction gap. Moderate and above
                    // may use every valid snapped beat, bounded by the difficulty gap.
                    if (lastPlacedTime > 0f && (t - lastPlacedTime) < placementGap)
                    {
                        _debugLog?.LogDecision(t, "SKIP beat", $"min-time-gap: (t-lastPlaced)={(t - lastPlacedTime):F4}s < placementGap={placementGap:F4}s");
                        beatIndex++;
                        continue;
                    }

                    // Density gate: skip this beat, but do not terminate the group or add a
                    // cooldown. A single random miss must not create a long empty section.
                    double densityRoll = rng.NextDouble();
                    if (densityRoll >= density)
                    {
                        _debugLog?.LogDecision(t, "SKIP beat (density gate)", $"roll={densityRoll:F4} >= density={density:F2}");
                        beatIndex++;
                        continue;
                    }

                    // Advance the meandering line: random-walk the heading, then step forward.
                    heading += (float)((rng.NextDouble() - 0.5) * 1.2);
                    float step = 110f + (float)rng.NextDouble() * 90f;
                    cx += (float)Math.Cos(heading) * step;
                    cy += (float)Math.Sin(heading) * step;

                    // Steer back toward the center if we drift too far, and clamp to the screen.
                    float distToCenter = (float)Math.Sqrt(Math.Pow(cx - screenW / 2f, 2) + Math.Pow(cy - screenH / 2f, 2));
                    float maxDist = Math.Min(screenW, screenH) / 2f - margin;
                    if (distToCenter > maxDist)
                    {
                        heading = (float)Math.Atan2(screenH / 2f - cy, screenW / 2f - cx)
                            + (float)((rng.NextDouble() - 0.5) * 0.6);
                        cx += (float)Math.Cos(heading) * step;
                        cy += (float)Math.Sin(heading) * step;
                    }
                    cx = Math.Clamp(cx, margin, screenW - margin);
                    cy = Math.Clamp(cy, margin, screenH - margin);

                    // A rare precomputed pattern is inserted atomically. If all four timed
                    // clicks do not fit, nothing is added and normal generation continues.
                    int patternCount = 4;
                    if (_difficultyIndex >= 3)
                    {
                        patternCount = 4 + rng.Next(5); // 4-8
                    }
                    if (groupCount + patternCount <= groupTargetSize
                        && patternChance > 0f
                        && e >= 0.45f
                        && rng.NextDouble() < patternChance
                        && TryPlaceClickPattern(
                            t,
                            beatInterval,
                            rng,
                            groupNumber,
                            groupCount + 1,
                            cx,
                            cy,
                            lastPlacedTime,
                            placementGap,
                            margin,
                            screenW,
                            screenH,
                            patternCount,
                            e,
                            out float patternEndTime,
                            out float patternFinalX,
                            out float patternFinalY))
                    {
                        groupCount += patternCount;
                        lastPlacedTime = patternEndTime;
                        cx = patternFinalX;
                        cy = patternFinalY;
                        while (beatIndex < totalBeats
                            && beatTimes[beatIndex] <= patternEndTime + _minTimeGapSeconds)
                        {
                            beatIndex++;
                        }
                        continue;
                    }

                    // Choose the element type from the audio energy (higher energy = more complex).
                    // Spinners are isolated: no element may be placed within SpinnerQuietBeats
                    // before or after a spinner, so multiple spinners can never overlap in time.
                    BeatCatchHitType type;
                    float spinnerDuration = Math.Max(
                        BeatClickerDifficulty.SpinnerMinimumDurationSeconds(_difficultyIndex),
                        beatInterval * BeatClickerDifficulty.SpinnerDurationBeats(_difficultyIndex));
                    float spinnerQuietStart = t - _spinnerQuietBeats * beatInterval;
                    bool spinnerAllowed = true;
                    foreach (var o in _hitObjects)
                    {
                        if (GetInteractionEndTime(o) > spinnerQuietStart)
                        {
                            spinnerAllowed = false;
                            break;
                        }
                    }

                    double spinnerRoll = rng.NextDouble();
                    double sliderRoll = rng.NextDouble();
                    bool recentSpinner = false;
                    foreach (var o in _hitObjects)
                    {
                        if (o.Type == BeatCatchHitType.Spinner
                            && t - o.Time < beatInterval * 12f)
                        {
                            recentSpinner = true;
                            break;
                        }
                    }
                    float spinnerEnergyChance = e >= 0.8f ? spinnerChance * 0.65f : spinnerChance * 0.35f;
                    bool spinnerDue = _difficultyIndex >= 5
                        && !recentSpinner
                        && spinnerRoll < 0.35f;
                    if (spinnerAllowed && e >= 0.45f && (spinnerRoll < spinnerEnergyChance || spinnerDue))
                    {
                        type = BeatCatchHitType.Spinner;
                        _debugLog?.LogDecision(t, "TYPE=Spinner", $"energy={e:F3}>=0.45, spinnerRoll={spinnerRoll:F4}, spinnerChanceByEnergy={spinnerEnergyChance:F4}, spinnerDue={spinnerDue}, spinnerAllowed={spinnerAllowed}");
                    }
                    else if (e > 0.6f && sliderRoll < sliderChance)
                    {
                        type = BeatCatchHitType.Slider;
                        _debugLog?.LogDecision(t, "TYPE=Slider", $"energy={e:F3}>0.6, sliderRoll={sliderRoll:F4}<sliderChance={sliderChance:F2}");
                    }
                    else
                    {
                        type = BeatCatchHitType.Circle;
                        _debugLog?.LogDecision(t, "TYPE=Circle", $"energy={e:F3} (spinner: allowed={spinnerAllowed}, energy>=0.55={e >= 0.55f}, roll={spinnerRoll:F4}; slider: e>0.6={e > 0.6f}, roll={sliderRoll:F4})");
                    }

                    var obj = new BeatCatchHitObject
                    {
                        Type = type,
                        Time = t,
                        X = cx,
                        Y = cy,
                        Group = groupNumber,
                        // Spinners are NOT numbered (Number=0, DrawObjectNumber skips them).
                        Number = type == BeatCatchHitType.Spinner ? 0 : groupCount + 1
                    };

                    if (type == BeatCatchHitType.Spinner)
                    {
                        // Spinners are centered and get a generous lifetime (min 3.0s, scaled by
                        // difficulty in beats) so the player has time to complete a rotation. On
                        // slower tracks the beat-based duration stretches well past 3s.
                        obj.X = screenW / 2f;
                        obj.Y = screenH / 2f;
                        obj.Duration = spinnerDuration;
                        if (!IsPlacementValid(obj, _hitObjects))
                        {
                            _debugLog?.LogDecision(t, "SKIP spinner", "placement or interaction interval is invalid");
                            beatIndex++;
                            continue;
                        }
                        _hitObjects.Add(obj);
                        lastPlacedTime = t;
                        cx = obj.X;
                        cy = obj.Y;
                        // Enforce a quiet zone AFTER the spinner: include the spinner's
                        // duration, its fade, and the next note's approach lead-in so no
                        // following element appears while the spinner still owns the cursor.
                        float spinnerEndTime = t + obj.Duration;
                        float quietEnd = spinnerEndTime
                            + PostHitFadeSeconds
                            + ApproachSeconds
                            + _spinnerQuietBeats * beatInterval;
                        _debugLog?.Log(t, $"SPINNER quiet zone: spinnerEnd={spinnerEndTime:F4}s quietEnd={quietEnd:F4}s (skipping beats until then)");
                        while (beatIndex < totalBeats && beatTimes[beatIndex] < quietEnd)
                        {
                            beatIndex++;
                        }
                        break;
                    }
                    else if (type == BeatCatchHitType.Slider)
                    {
                        ConfigureSliderProfile(ref obj, heading, beatInterval, rng, margin, screenW, screenH, e);
                    }

                    // Enforce spacing: the new element must not overlap or sit too close to any
                    // existing element, a slider must keep its minimum length, and no element may
                    // overlap another in time. If the spot is invalid, nudge the position; if it
                    // still can't be placed, skip the beat (the meander continues on the next beat).
                    if (!IsPlacementValid(obj, _hitObjects))
                    {
                        for (int attempt = 0; attempt < 8 && !IsPlacementValid(obj, _hitObjects); attempt++)
                        {
                            float a = (float)(rng.NextDouble() * Math.PI * 2);
                            obj.X = Math.Clamp(obj.X + (float)Math.Cos(a) * 60f, margin, screenW - margin);
                            obj.Y = Math.Clamp(obj.Y + (float)Math.Sin(a) * 60f, margin, screenH - margin);
                            if (obj.Type == BeatCatchHitType.Slider)
                            {
                                float dx = obj.EndX - obj.X;
                                float dy = obj.EndY - obj.Y;
                                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                                float angle = len < 0.001f
                                    ? heading
                                    : (float)Math.Atan2(dy, dx);
                                float newLen = Math.Clamp(len, _minSliderLength, _maxSliderLength);
                                SetSliderEndpoint(ref obj, angle, newLen, margin, screenW, screenH);
                            }
                        }
                    }

                    if (!IsPlacementValid(obj, _hitObjects))
                    {
                        _debugLog?.LogDecision(t, "SKIP beat (placement invalid after 8 nudges)", $"pos=({obj.X:F0},{obj.Y:F0}) end=({obj.EndX:F0},{obj.EndY:F0}) type={type}");
                        beatIndex++;
                        continue;
                    }

                    _hitObjects.Add(obj);
                    lastPlacedTime = t;
                    groupCount++;
                    _debugLog?.Log(t, $"PLACED {type} #{groupCount}  pos=({obj.X:F0},{obj.Y:F0}) end=({obj.EndX:F0},{obj.EndY:F0})  duration={obj.Duration:F3}s  group={groupNumber}");
                    _debugLog?.LogAudioSnippet(t, "audio at placement", t);
                    beatIndex++;
                }

                // Cool-down between groups: a short break with no elements (a "breather").
                // The cool-down is skipped when the group ended because the music went quiet
                // (a natural pause) � the silence itself is the break.
                if (beatIndex < totalBeats)
                {
                    int idx = Math.Clamp(beatIndex, 0, totalBeats - 1);
                    bool naturalPause = energy[idx] < 0.15f;
                    if (!naturalPause)
                    {
                        int cooldownBeats = minCooldown + rng.Next(Math.Max(1, maxCooldown - minCooldown + 1));
                        _debugLog?.Log(beatTimes[beatIndex], $"COOLDOWN after group {groupNumber} ({groupCount} elements): skip {cooldownBeats} beats (energy={energy[idx]:F3}, not a natural pause)");
                        beatIndex += cooldownBeats;
                    }
                    else
                    {
                        _debugLog?.Log(beatTimes[beatIndex], $"NO cooldown after group {groupNumber}: natural pause (energy={energy[idx]:F3} < 0.15)");
                    }
                }
            }

            _hitObjects.Sort((a, b) => a.Time.CompareTo(b.Time));
            _debugLog?.LogSection(0f, $"beatmap complete: {_hitObjects.Count} elements");
        }

        private float GetCountInStepSeconds()
        {
            float bpm = _bpm > 0f ? _bpm : (_audio.Bpm > 0f ? _audio.Bpm : 120f);
            return CountInBeatsPerNumber * 60f / bpm;
        }

        private float GetCountInDurationSeconds()
        {
            return GetCountInStepSeconds() * 3f;
        }

        private void SetSliderEndpoint(ref BeatCatchHitObject obj, float angle, float desiredLength, int margin, int screenW, int screenH)
        {
            float dx = (float)Math.Cos(angle);
            float dy = (float)Math.Sin(angle);
            float maxForward = float.PositiveInfinity;

            if (dx > 0.001f)
            {
                maxForward = Math.Min(maxForward, (screenW - margin - obj.X) / dx);
            }
            else if (dx < -0.001f)
            {
                maxForward = Math.Min(maxForward, (margin - obj.X) / dx);
            }

            if (dy > 0.001f)
            {
                maxForward = Math.Min(maxForward, (screenH - margin - obj.Y) / dy);
            }
            else if (dy < -0.001f)
            {
                maxForward = Math.Min(maxForward, (margin - obj.Y) / dy);
            }

            if (maxForward < _minSliderLength)
            {
                dx = screenW / 2f - obj.X;
                dy = screenH / 2f - obj.Y;
                float towardCenterLength = (float)Math.Sqrt(dx * dx + dy * dy);
                if (towardCenterLength > 0.001f)
                {
                    dx /= towardCenterLength;
                    dy /= towardCenterLength;
                    maxForward = float.PositiveInfinity;
                    if (dx > 0.001f)
                    {
                        maxForward = Math.Min(maxForward, (screenW - margin - obj.X) / dx);
                    }
                    else if (dx < -0.001f)
                    {
                        maxForward = Math.Min(maxForward, (margin - obj.X) / dx);
                    }
                    if (dy > 0.001f)
                    {
                        maxForward = Math.Min(maxForward, (screenH - margin - obj.Y) / dy);
                    }
                    else if (dy < -0.001f)
                    {
                        maxForward = Math.Min(maxForward, (margin - obj.Y) / dy);
                    }
                }
            }

            float length = Math.Clamp(desiredLength, _minSliderLength, Math.Min(_maxSliderLength, maxForward));
            obj.EndX = obj.X + dx * length;
            obj.EndY = obj.Y + dy * length;
        }

        private void ConfigureSliderProfile(
            ref BeatCatchHitObject obj,
            float heading,
            float beatInterval,
            Random rng,
            int margin,
            int screenW,
            int screenH,
            float energy)
        {
            var id = _patternLibrary.SelectPattern(rng, _difficultyIndex, energy, slider: true);
            var p = _patternLibrary.CreateParameters(id, rng, heading, _minSliderLength, _maxSliderLength, _difficultyIndex);
            var path = _patternLibrary.BuildSliderPath(
                id, p, obj.X, obj.Y, margin, screenW, screenH, _minSliderLength, _maxSliderLength, rng);

            obj.EndX = path.X[^1];
            obj.EndY = path.Y[^1];
            obj.Duration = beatInterval * p.DurationBeats;
            obj.PathX = path.X;
            obj.PathY = path.Y;
            obj.PathCumulativeLength = path.CumulativeLength;
            obj.PathTotalLength = path.TotalLength;
            obj.PatternIndex = id.Index;

            _patternLibrary.RememberFingerprint(
                id, p.LengthFactor, p.Amplitude, p.Phase, p.Heading, p.DurationBeats, 0f);

            _debugLog?.LogDecision(
                obj.Time,
                "SLIDER PATTERN",
                $"id={id.Index} spatial={id.Spatial} rhythm={id.Rhythm} transform={id.Transform} texture={id.Texture} " +
                $"heading={p.Heading:F3} lengthFactor={p.LengthFactor:F3} amplitude={p.Amplitude:F3} phase={p.Phase:F3} " +
                $"frequency={p.Frequency:F3} easing={p.Easing:F3} loopRadius={p.LoopRadius:F3} turnFraction={p.TurnFraction:F3} " +
                $"skew={p.Skew:F3} jitter={p.TextureJitter:F3} damping={p.TextureDamping:F3} resonance={p.TextureResonance:F3} " +
                $"mirror={p.Mirror} reverse={p.Reverse} " +
                $"length={path.TotalLength:F0}px beats={p.DurationBeats} duration={obj.Duration:F3}s points={path.X.Length}");
        }

        private bool TryPlaceClickPattern(
            float startTime,
            float beatInterval,
            Random rng,
            int groupNumber,
            int firstNumber,
            float centerX,
            float centerY,
            float lastPlacedTime,
            float placementGap,
            int margin,
            int screenW,
            int screenH,
            int count,
            float energy,
            out float patternEndTime,
            out float finalX,
            out float finalY)
        {
            patternEndTime = startTime + beatInterval * 1.5f;
            finalX = centerX;
            finalY = centerY;
            if (patternEndTime >= _totalDuration - 0.1f
                || (lastPlacedTime > 0f && startTime - lastPlacedTime < placementGap))
            {
                return false;
            }

            var id = _patternLibrary.SelectPattern(rng, _difficultyIndex, energy, slider: false);
            var p = _patternLibrary.CreateParameters(id, rng, 0f, _minSliderLength, _maxSliderLength, _difficultyIndex);
            var pattern = _patternLibrary.BuildClickPattern(
                id, p, startTime, beatInterval, count, centerX, centerY, margin, screenW, screenH, _circleRadius, _minTimeGapSeconds);

            var candidates = new List<BeatCatchHitObject>(count);
            var occupied = new List<BeatCatchHitObject>(_hitObjects);
            for (int i = 0; i < count; i++)
            {
                var candidate = new BeatCatchHitObject
                {
                    Type = BeatCatchHitType.Circle,
                    Time = pattern.Times[i],
                    X = pattern.X[i],
                    Y = pattern.Y[i],
                    Group = groupNumber,
                    Number = firstNumber + i,
                    PatternIndex = id.Index
                };
                if (!IsPlacementValid(candidate, occupied))
                {
                    return false;
                }
                candidates.Add(candidate);
                occupied.Add(candidate);
            }

            foreach (var candidate in candidates)
            {
                _hitObjects.Add(candidate);
                _debugLog?.Log(
                    candidate.Time,
                    $"PATTERN Circle #{candidate.Number} id={id.Index} pos=({candidate.X:F0},{candidate.Y:F0}) group={groupNumber}");
            }

            finalX = candidates[^1].X;
            finalY = candidates[^1].Y;
            patternEndTime = candidates[^1].Time;
            _patternLibrary.RememberFingerprint(
                id, p.LengthFactor, p.Amplitude, p.Phase, p.Heading, p.DurationBeats, 1f);
            _debugLog?.Log(startTime, $"PATTERN complete id={id.Index} spatial={id.Spatial} rhythm={id.Rhythm} transform={id.Transform} texture={id.Texture} " +
                $"heading={p.Heading:F3} lengthFactor={p.LengthFactor:F3} amplitude={p.Amplitude:F3} phase={p.Phase:F3} " +
                $"frequency={p.Frequency:F3} easing={p.Easing:F3} loopRadius={p.LoopRadius:F3} turnFraction={p.TurnFraction:F3} " +
                $"skew={p.Skew:F3} jitter={p.TextureJitter:F3} damping={p.TextureDamping:F3} resonance={p.TextureResonance:F3} " +
                $"mirror={p.Mirror} reverse={p.Reverse} count={count} group={groupNumber}");
            return true;
        }

        /// <summary>
        /// Snaps a nominal beat time to the strongest onset (energy peak) within a small
        /// window around it, so the element's hit time lines up with the actually heard
        /// kick/beat instead of the (possibly off) estimated beat grid.
        /// </summary>
        private float SnapToOnset(float beatTime, float beatInterval, int totalBeats, float[] energy)
        {
            int sampleRate = Math.Max(1, _audio.SampleRate);
            int windowFrames = sampleRate / 20; // 50ms window
            int centerFrame = (int)(beatTime * sampleRate);
            int startFrame = Math.Max(0, centerFrame - windowFrames);
            int endFrame = Math.Min(_audio.Data?.Length ?? 0, centerFrame + windowFrames);

            float bestTime = beatTime;
            float bestEnergy = -1f;
            for (int f = startFrame; f < endFrame; f += 2)
            {
                float e = GetEnergyAtTime((float)f / sampleRate);
                if (e > bestEnergy)
                {
                    bestEnergy = e;
                    bestTime = (float)f / sampleRate;
                }
            }

            // Keep the snapped time within a quarter beat of the nominal beat so the
            // element stays on the beat grid (no large drift between consecutive elements).
            float maxOffset = beatInterval * 0.25f;
            return Math.Clamp(bestTime, beatTime - maxOffset, beatTime + maxOffset);
        }

        /// <summary>
        /// Returns true if the candidate element can be placed without overlapping or sitting
        /// too close to existing elements (and, for sliders, without its track crossing them).
        /// </summary>
        private static float GetInteractionEndTime(BeatCatchHitObject obj)
        {
            return obj.Time + (obj.Type == BeatCatchHitType.Slider || obj.Type == BeatCatchHitType.Spinner
                ? Math.Max(0f, obj.Duration)
                : 0f);
        }

        private bool IsInteractionIntervalValid(BeatCatchHitObject candidate, List<BeatCatchHitObject> existing)
        {
            float candidateEnd = GetInteractionEndTime(candidate);
            foreach (var other in existing)
            {
                float otherEnd = GetInteractionEndTime(other);
                bool overlaps = candidate.Time < otherEnd + _minTimeGapSeconds
                    && other.Time < candidateEnd + _minTimeGapSeconds;
                if (overlaps)
                {
                    return false;
                }
            }
            return true;
        }

        private float GetApproachWindowSeconds(BeatCatchHitObject obj)
        {
            float beatInterval = _bpm > 0f ? 60f / _bpm : 0.5f;
            return obj.Type == BeatCatchHitType.Spinner
                ? _spinnerQuietBeats * beatInterval
                : ApproachSeconds;
        }

        private float GetVisualEndTime(BeatCatchHitObject obj)
        {
            return GetInteractionEndTime(obj) + PostHitFadeSeconds;
        }

        private bool IsPlacementValid(BeatCatchHitObject candidate, List<BeatCatchHitObject> existing)
        {
            // A slider must keep its minimum length (start and end must not overlap).
            if (candidate.Type == BeatCatchHitType.Slider)
            {
                float len = (float)Math.Sqrt(Math.Pow(candidate.EndX - candidate.X, 2) + Math.Pow(candidate.EndY - candidate.Y, 2));
                if (len < _minSliderLength)
                {
                    return false;
                }
            }

            if (!IsInteractionIntervalValid(candidate, existing))
            {
                return false;
            }

            // Visual overlap is intentional: upcoming objects can be shown together. The
            // interaction interval check above still prevents two objects from requiring the
            // cursor at the same time, including a slider's full drag duration.
            return true;
        }

        private void GenerateTaikoBeats()
        {
            float beatInterval = 60f / _bpm;
            float t = beatInterval;
            int beatCount = 0;
            var rng = new Random();

            float kaChance = BeatClickerDifficulty.TaikoKaChance(_difficultyIndex);
            float restChance = BeatClickerDifficulty.TaikoRestChance(_difficultyIndex);

            _debugLog?.LogSection(0f, $"taiko beatmap generation (BPM={_bpm:F2}, beatInterval={beatInterval:F4}s)");
            _debugLog?.Log(0f, $"params: kaChance={kaChance:F2} restChance={restChance:F2}");

            while (t < _totalDuration - 0.5f)
            {
                beatCount++;

                // Rest beat: skip this beat entirely (higher difficulty = more rests)
                double restRoll = rng.NextDouble();
                if (restChance > 0f && restRoll < restChance)
                {
                    _debugLog?.Log(t, $"REST  t={t:F4}s  (restRoll={restRoll:F3} < restChance={restChance:F2})");
                    t += beatInterval;
                    continue;
                }

                float energy = GetEnergyAtTime(t);
                int beatInBar = beatCount % 4;

                TaikoBeatType type;
                double kaRoll = rng.NextDouble();
                if (beatInBar == 0)
                {
                    type = TaikoBeatType.Don;
                }
                else if (energy > 0.7f)
                {
                    type = TaikoBeatType.Ka;
                }
                else if (kaRoll < kaChance)
                {
                    type = TaikoBeatType.Ka;
                }
                else
                {
                    type = TaikoBeatType.Don;
                }

                _taikoBeats.Add(new TaikoBeat { Time = t, Type = type });
                _debugLog?.Log(t, $"{type}  t={t:F4}s  beatInBar={beatInBar}  energy={energy:F3}");
                t += beatInterval;
            }

            _debugLog?.LogSection(0f, $"taiko beatmap complete: {_taikoBeats.Count} beats");
        }

        private float GetEnergyAtTime(float timeSeconds)
        {
            if (_audio.Data == null || _audio.Data.Length == 0)
            {
                return 0.5f;
            }

            int channels = Math.Max(1, _audio.Channels);
            int sampleRate = Math.Max(1, _audio.SampleRate);
            int startFrame = (int)(timeSeconds * sampleRate);
            int windowFrames = sampleRate / 10; // 100ms window
            int endFrame = Math.Min(startFrame + windowFrames, _audio.Data.Length / channels);

            if (startFrame >= endFrame)
            {
                return 0.5f;
            }

            float sum = 0f;
            int count = 0;
            for (int i = startFrame; i < endFrame; i += 4) // sample every 4th for speed
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    float sample = _audio.Data[i * channels + ch];
                    sum += sample * sample;
                    count++;
                }
            }

            if (count == 0)
            {
                return 0.5f;
            }

            float rms = (float)Math.Sqrt(sum / count);
            return Math.Clamp(rms * 4f, 0f, 1f);
        }

        // --- Game loop --------------------------------------------------------

        private void ResetGame()
        {
            lock (_lock)
            {
                _currentHitIndex = 0;
                _score = 0;
                _scorePoints = 0d;
                _missed = 0;
                _timingOffsetSumMs = 0;
                _timingSampleCount = 0;
                _lastTimingOffsetMs = 0f;
                _lastTimingColor = Color.White;
                _hasTimingFeedback = false;
                _lastTimingWasMiss = false;
                _activeSliderIndex = -1;
                _sliderStartHit = false;
                _sliderDragged = false;
                _sliderMaxProgress = 0f;
                _sliderPathValid = false;
                _leftButtonHeld = false;
                _completedSliderIndex = -1;
                _completedSliderProgress = 0f;
                _completedSliderHitTime = 0f;
                _failedSliderIndex = -1;
                _activeSpinnerIndex = -1;
                _spinnerLastAngle = 0f;
                _spinnerAccumulatedAngle = 0f;
                _spinnerCombo = 0;
                _spinnerRotationCompleted = false;
                _spinnerHitRegistered = false;
                _failEffects.Clear();
                _hitEffects.Clear();
                _comboPopups.Clear();
                _comboStreak = 0;
                _bestComboStreak = 0;
                _resultsShown = false;
                _runSummarySaved = false;
            }
            _gameClock.Restart();
            _escKeyReleased = true;
        }

        private void GameTimer_Tick(object? sender, EventArgs e)
        {
            if (!_gameRunning || _paused)
            {
                return;
            }

            // Idle spinner spin: while no spinner is grabbed, its indicator slowly rotates
            // on its own (for visual interest). Advance it every frame; when a spinner is
            // grabbed, _spinnerIdleAngle is re-seeded from the cursor in the mouse handler.
            if (_activeSpinnerIndex < 0)
            {
                _spinnerIdleAngle += 0.5f * (float)_gameTimer.Interval / 1000f;
            }

            // Count-in phase: the game clock follows the already-playing audio while
            // gameplay is suppressed. Show 3-2-1 at two-beat intervals.
            if (_countInActive)
            {
                float countInElapsed = (float)Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency - _countInStart;
                if (countInElapsed >= GetCountInDurationSeconds())
                {
                    _countInActive = false;
                    _debugLog?.LogEvent((float)_gameClock.Elapsed.TotalSeconds, "COUNT-IN complete (two beats per count, game clock stayed aligned to music)");
                }
                try { PushLayeredBitmap(); }
                catch (Exception ex) { Debug.WriteLine($"BeatClickerGame render error: {ex.Message}"); }
                return;
            }

            float currentTime = (float)_gameClock.Elapsed.TotalSeconds;

            // Audio-position drift: the game clock (Stopwatch) and the actual audio playback
            // position can diverge (audio latency, pause/resume, rate changes). This is the
            // single most important signal for diagnosing "off-beat" hits: if the drift is
            // large, the player is hearing the beat at a different time than the game clock
            // thinks, so every hit window is effectively shifted.
            if (_debugLog != null && (int)(currentTime * 10) % 50 == 0)
            {
                float audioPos = 0f;
                try { audioPos = (float)_audio.CurrentTime.TotalSeconds; }
                catch { }
                _debugLog?.LogEvent(currentTime, $"DRIFT gameClock={currentTime:F4}s audioPos={audioPos:F4}s diff={(audioPos - currentTime) * 1000:F1}ms");
            }

            // The beatmap is generated asynchronously. Until it's ready, only render the
            // background (no elements, no auto-miss) so the player sees a clean count-in.
            if (!_beatmapReady)
            {
                try { PushLayeredBitmap(); }
                catch (Exception ex) { Debug.WriteLine($"BeatClickerGame render error: {ex.Message}"); }
                return;
            }

            lock (_lock)
            {
                if (_isTaikoMode)
                {
                    // Cap auto-miss at 1 per tick so a long pause doesn't cascade into
                    // a wall of red X's. The next tick picks up the next overdue beat.
                    if (_currentHitIndex < _taikoBeats.Count)
                    {
                        float beatTime = _taikoBeats[_currentHitIndex].Time;
                        if (currentTime >= beatTime + _taikoHitWindowLateMs / 1000f)
                        {
                            RegisterMiss();
                            int drumCX = this.ClientSize.Width / 2;
                            int drumCY = this.ClientSize.Height / 2;
                            AddFailEffect(drumCX, drumCY, currentTime);
                            _debugLog?.LogMiss(currentTime, _taikoBeats[_currentHitIndex].Type.ToString(), beatTime, beatTime - currentTime, "auto-miss (late window passed, no click)");
                            _currentHitIndex++;
                        }
                    }
                }
                else
                {
                    // Cap auto-miss at 1 per tick: only the single most-overdue object is
                    // missed per frame. This prevents a wall of red X's when the player
                    // pauses � each element gets its own tick to be missed, and the
                    // approach circle is still visible for the next element.
                    if (_currentHitIndex < _hitObjects.Count)
                    {
                        var obj = _hitObjects[_currentHitIndex];
                        float objTime = obj.Time;

                        if (obj.Type == BeatCatchHitType.Slider
                            && _completedSliderIndex == _currentHitIndex)
                        {
                            _currentHitIndex++;
                            _activeSliderIndex = -1;
                            _sliderStartHit = false;
                            _sliderDragged = false;
                            _sliderMaxProgress = 0f;
                            _sliderPathValid = false;
                            goto render;
                        }

                        if (obj.Type == BeatCatchHitType.Spinner)
                        {
                            // A spinner owns the only mouse pointer until its full duration
                            // ends. Do not resolve it at SliderActiveSeconds, otherwise later
                            // elements become clickable while the spinner is still visible.
                            if (currentTime < objTime + obj.Duration)
                            {
                                goto render;
                            }

                            if (_spinnerHitRegistered || _spinnerRotationCompleted || _spinnerCombo >= 1)
                            {
                                _debugLog?.LogHit(currentTime, "Spinner", objTime, objTime - currentTime, (int)obj.X, (int)obj.Y);
                            }
                            else
                            {
                                RegisterMiss();
                                AddFailEffect(obj.X, obj.Y, currentTime);
                                _debugLog?.LogMiss(currentTime, "Spinner", objTime, objTime - currentTime, "auto-miss (spinner duration expired without a full rotation)");
                            }

                            _currentHitIndex++;
                            _activeSpinnerIndex = -1;
                            _spinnerLastAngle = 0f;
                            _spinnerAccumulatedAngle = 0f;
                            _spinnerCombo = 0;
                            _spinnerRotationCompleted = false;
                            _spinnerHitRegistered = false;
                        }
                        else
                        {
                            // A slider owns the pointer for its beat-aligned duration once its
                            // start has been hit. Ordinary circles retain their normal late window.
                            float lateWindow = obj.Type == BeatCatchHitType.Slider
                                ? obj.Duration + _hitWindowLateMs / 1000f
                                : _hitWindowLateMs / 1000f;

                            if (currentTime < objTime + lateWindow)
                            {
                                goto render;
                            }

                            if (obj.Type == BeatCatchHitType.Slider && _activeSliderIndex == _currentHitIndex && _sliderStartHit)
                            {
                                float currentProgress = GetSliderProgress(obj, _mouseX, _mouseY);
                                float targetProgress = GetSliderTargetProgress(obj, currentTime);
                                bool heldAtEndpoint = _leftButtonHeld
                                    && _sliderDragged
                                    && IsSliderPointOnPath(obj, _mouseX, _mouseY)
                                    && currentProgress >= 0.95f
                                    && targetProgress >= 0.95f
                                    && Math.Abs(currentProgress - targetProgress) <= _sliderProgressTolerance;
                                if (heldAtEndpoint)
                                {
                                    _completedSliderIndex = _activeSliderIndex;
                                    _completedSliderProgress = Math.Max(_sliderMaxProgress, currentProgress);
                                    _completedSliderHitTime = currentTime;
                                    RegisterHit();
                                    AddSliderHitEffect(obj, currentTime);
                                    _debugLog?.LogHit(currentTime, "Slider", objTime, objTime - currentTime, (int)obj.EndX, (int)obj.EndY);
                                }
                                else
                                {
                                    _failedSliderIndex = _currentHitIndex;
                                    RegisterMiss();
                                    AddFailEffect(obj.X, obj.Y, currentTime);
                                    string reason = _sliderDragged
                                        ? "auto-miss (slider duration expired without reaching the end)"
                                        : "auto-miss (slider clicked but not dragged)";
                                    _debugLog?.LogMiss(currentTime, "Slider", objTime, objTime - currentTime, reason);
                                }
                                _currentHitIndex++;
                                _activeSliderIndex = -1;
                                _sliderStartHit = false;
                                _sliderDragged = false;
                                _sliderMaxProgress = 0f;
                                _sliderPathValid = false;
                            }
                            else if (obj.Type == BeatCatchHitType.Slider)
                            {
                                _failedSliderIndex = _currentHitIndex;
                                RegisterMiss();
                                AddFailEffect(obj.X, obj.Y, currentTime);
                                _debugLog?.LogMiss(currentTime, "Slider", objTime, objTime - currentTime, "auto-miss (slider duration passed without a click)");
                                _currentHitIndex++;
                            }
                            else
                            {
                                RegisterMiss();
                                AddFailEffect(obj.X, obj.Y, currentTime);
                                _debugLog?.LogMiss(currentTime, obj.Type.ToString(), objTime, objTime - currentTime, "auto-miss (late window passed, no click)");
                                _currentHitIndex++;
                            }
                        }
                    }
                }
            }

            // Resolve the result only after every object and the audio timeline have ended.
            // The game clock is stopped while paused, so this still measures active track time.
            bool trackFinished = _totalDuration <= 0f || currentTime >= _totalDuration;
            if (_beatmapReady && !_resultsShown && trackFinished
                && _currentHitIndex >= (_isTaikoMode ? _taikoBeats.Count : _hitObjects.Count)
                && (_isTaikoMode ? _taikoBeats.Count : _hitObjects.Count) > 0)
            {
                _resultsShown = true;
                ShowResultsDialog();
            }

        render:
            try { PushLayeredBitmap(); }
            catch (Exception ex) { Debug.WriteLine($"BeatClickerGame render error: {ex.Message}"); }
        }

        // --- Input handling ---------------------------------------------------

        private void GameForm_MouseDown(object? sender, MouseEventArgs e)
        {
            if (!_gameRunning || _paused || e.Button != MouseButtons.Left)
            {
                return;
            }

            _leftButtonHeld = true;
            _mouseX = e.X;
            _mouseY = e.Y;
            float currentTime = (float)_gameClock.Elapsed.TotalSeconds;

            _debugLog?.LogPlayerInput(currentTime, "MouseDown", e.X, e.Y);

            lock (_lock)
            {
                if (_isTaikoMode)
                {
                    HandleTaikoClick(e.X, e.Y, currentTime);
                }
                else
                {
                    HandleBeatCatchMouseDown(e.X, e.Y, currentTime);
                }
            }

            PushLayeredBitmap();
        }

        private void GameForm_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_gameRunning || _paused || _isTaikoMode)
            {
                return;
            }

            _mouseX = e.X;
            _mouseY = e.Y;

            // Track slider dragging only while the original left-button grab is held.
            if (_activeSliderIndex >= 0 && _sliderStartHit && _leftButtonHeld)
            {
                float currentTime = (float)_gameClock.Elapsed.TotalSeconds;
                lock (_lock)
                {
                    if (_activeSliderIndex < _hitObjects.Count)
                    {
                        var obj = _hitObjects[_activeSliderIndex];
                        float dx = obj.EndX - obj.X;
                        float dy = obj.EndY - obj.Y;
                        float lenSq = dx * dx + dy * dy;
                        if (lenSq > 0.001f)
                        {
                            float progress = Math.Clamp(((e.X - obj.X) * dx + (e.Y - obj.Y) * dy) / lenSq, 0f, 1f);
                            float closestX = obj.X + progress * dx;
                            float closestY = obj.Y + progress * dy;
                            float distanceFromPath = (float)Math.Sqrt(
                                Math.Pow(e.X - closestX, 2) + Math.Pow(e.Y - closestY, 2));

                            _sliderPathValid = distanceFromPath <= _circleRadius * 1.5f;
                            if (_sliderPathValid)
                            {
                                _sliderDragged = true;
                                _sliderMaxProgress = Math.Max(_sliderMaxProgress, progress);
                            }

                            float targetProgress = GetSliderTargetProgress(obj, currentTime);
                            bool onTimeForCompletion = targetProgress >= 0.95f
                                && Math.Abs(progress - targetProgress) <= _sliderProgressTolerance;
                            if (_sliderPathValid
                                && _sliderDragged
                                && progress >= 0.95f
                                && onTimeForCompletion)
                            {
                                _completedSliderIndex = _activeSliderIndex;
                                _completedSliderProgress = _sliderMaxProgress;
                                _completedSliderHitTime = currentTime;
                                RegisterHit();
                                AddSliderHitEffect(obj, currentTime);
                                _debugLog?.LogHit(currentTime, "Slider", obj.Time, obj.Time - currentTime, (int)obj.EndX, (int)obj.EndY);
                                _currentHitIndex = _activeSliderIndex + 1;
                                _activeSliderIndex = -1;
                                _sliderStartHit = false;
                                _sliderDragged = false;
                                _sliderMaxProgress = 0f;
                                _sliderPathValid = false;
                            }
                        }
                    }
                }
                PushLayeredBitmap();
            }
            else if (_activeSpinnerIndex >= 0)
            {
                // Spinner is active � track the mouse and count completed rotations
                if (_activeSpinnerIndex < _hitObjects.Count)
                {
                    var obj = _hitObjects[_activeSpinnerIndex];
                    float newAngle = (float)Math.Atan2(e.Y - obj.Y, e.X - obj.X);
                    // Seed the idle spin from the current cursor angle so that, when the
                    // player releases the spinner, the idle rotation continues from exactly
                    // where the cursor was (no visual jump).
                    _spinnerIdleAngle = newAngle;
                    float delta = newAngle - _spinnerLastAngle;
                    // Normalize to [-PI, PI] so a wrap-around is a small step, not a full turn
                    while (delta > Math.PI) { delta -= 2f * (float)Math.PI; }
                    while (delta < -Math.PI) { delta += 2f * (float)Math.PI; }
                    _spinnerLastAngle = newAngle;
                    _spinnerAccumulatedAngle += delta;

                    // One full rotation in either direction = one combo step (like classic spinners)
                    if (Math.Abs(_spinnerAccumulatedAngle) >= 2f * (float)Math.PI)
                    {
                        _spinnerAccumulatedAngle -= Math.Sign(_spinnerAccumulatedAngle) * 2f * (float)Math.PI;
                        _spinnerCombo++;
                        _spinnerRotationCompleted = true;
                        float currentTime = (float)_gameClock.Elapsed.TotalSeconds;
                        AddComboPopup(_spinnerCombo, obj.X, obj.Y, currentTime);
                        if (!_spinnerHitRegistered)
                        {
                            RegisterHit();
                            _spinnerHitRegistered = true;
                            _debugLog?.LogHit(currentTime, "Spinner-rotation", obj.Time, obj.Time - currentTime, (int)obj.X, (int)obj.Y);
                        }
                        _debugLog?.LogEvent(currentTime, $"Spinner rotation complete (count={_spinnerCombo})");
                    }
                }
                PushLayeredBitmap();
            }
        }

        private void GameForm_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (_activeSliderIndex >= 0
                && _activeSliderIndex < _hitObjects.Count
                && _sliderStartHit
                && _currentHitIndex == _activeSliderIndex)
            {
                var slider = _hitObjects[_activeSliderIndex];
                float currentTime = (float)_gameClock.Elapsed.TotalSeconds;
                float progress = GetSliderProgress(slider, e.X, e.Y);
                float targetProgress = GetSliderTargetProgress(slider, currentTime);
                bool releasedAtEndpoint = _sliderDragged
                    && IsSliderPointOnPath(slider, e.X, e.Y)
                    && progress >= 0.95f
                    && targetProgress >= 0.95f
                    && Math.Abs(progress - targetProgress) <= _sliderProgressTolerance;
                if (releasedAtEndpoint)
                {
                    _completedSliderIndex = _activeSliderIndex;
                    _completedSliderProgress = Math.Max(_sliderMaxProgress, progress);
                    _completedSliderHitTime = currentTime;
                    RegisterHit();
                    AddSliderHitEffect(slider, currentTime);
                    _debugLog?.LogHit(currentTime, "Slider", slider.Time, slider.Time - currentTime, (int)slider.EndX, (int)slider.EndY);
                    _currentHitIndex = _activeSliderIndex + 1;
                }
                else
                {
                    _failedSliderIndex = _activeSliderIndex;
                    RegisterMiss();
                    AddFailEffect(slider.X, slider.Y, currentTime);
                    _debugLog?.LogMiss(
                        currentTime,
                        "Slider",
                        slider.Time,
                        slider.Time - currentTime,
                        "slider released before reaching the endpoint");
                    _currentHitIndex = _activeSliderIndex + 1;
                }
            }

            _leftButtonHeld = false;

            if (_activeSliderIndex >= 0 && _sliderStartHit)
            {
                _activeSliderIndex = -1;
                _sliderStartHit = false;
                _sliderDragged = false;
                _sliderMaxProgress = 0f;
                _sliderPathValid = false;
            }

            // Release spinner if active
            if (_activeSpinnerIndex >= 0 && _activeSpinnerIndex < _hitObjects.Count)
            {
                _mouseX = e.X;
                _mouseY = e.Y;
                _spinnerIdleAngle = (float)Math.Atan2(e.Y - _hitObjects[_activeSpinnerIndex].Y, e.X - _hitObjects[_activeSpinnerIndex].X);
                _activeSpinnerIndex = -1;
                _spinnerLastAngle = 0f;
                _spinnerAccumulatedAngle = 0f;
                PushLayeredBitmap();
            }
        }

        private void HandleBeatCatchMouseDown(int mouseX, int mouseY, float currentTime)
        {
            // Find the nearest unhit object
            for (int i = _currentHitIndex; i < _hitObjects.Count; i++)
            {
                var obj = _hitObjects[i];
                float timeUntilHit = obj.Time - currentTime;

                bool isSpinner = obj.Type == BeatCatchHitType.Spinner;
                float approachWindow = GetApproachWindowSeconds(obj);

                // If the object is still far in the future, stop searching.
                if (timeUntilHit > approachWindow)
                {
                    break;
                }

                // Spinners are grabbable from ANYWHERE on the screen for their ENTIRE lifetime
                // (the player whirs the mouse around the spinner center, so the click can be
                // far from the center). The approach ring only visualizes how much time is
                // left; the mouse does NOT need to be inside the circle to spin.
                if (isSpinner)
                {
                    // The spinner is active from the beginning of its visible approach until
                    // its duration has elapsed. The approach ring is only a timer; the cursor
                    // can start from anywhere on this screen.
                    if (timeUntilHit >= -obj.Duration)
                    {
                        if (_activeSpinnerIndex != i)
                        {
                            _activeSpinnerIndex = i;
                            _spinnerLastAngle = (float)Math.Atan2(mouseY - obj.Y, mouseX - obj.X);
                            _spinnerIdleAngle = _spinnerLastAngle;
                            _spinnerAccumulatedAngle = 0f;
                            _debugLog?.LogHit(currentTime, "Spinner-start", obj.Time, timeUntilHit, mouseX, mouseY);
                        }
                        return;
                    }
                    // Past the spinner's lifetime: block clicks on later objects until the
                    // timer resolves this spinner and advances the current index.
                    return;
                }

                // Circles and sliders require the click to be near the object.
                float dist = obj.Type == BeatCatchHitType.Slider
                    ? (float)Math.Sqrt(Math.Pow(mouseX - obj.X, 2) + Math.Pow(mouseY - obj.Y, 2))
                    : GetDistanceToHitObject(mouseX, mouseY, obj);
                bool inRange = dist < _circleRadius * 1.5f;
                if (inRange)
                {
                    // Sliders: wider early window so the player can grab them
                    float earlyWindow = obj.Type == BeatCatchHitType.Slider ? _hitWindowEarlyMs + 100f : _hitWindowEarlyMs;

                    // The audio output latency shifts the heard beat later than the sample
                    // position. Shift the hit windows by the measured latency so the optimal
                    // click time lines up with the actually heard beat (not the sample time).
                    float latencySeconds = _audioLatencyMs / 1000f;

                    // Too-early click: outside the valid early window = instant miss
                    if (timeUntilHit > earlyWindow / 1000f + latencySeconds)
                    {
                        if (obj.Type == BeatCatchHitType.Slider)
                        {
                            _failedSliderIndex = i;
                        }
                        RegisterMiss();
                        AddFailEffect(obj.X, obj.Y, currentTime);
                        _debugLog?.LogMiss(currentTime, obj.Type.ToString(), obj.Time, timeUntilHit, $"too early (timeUntilHit={timeUntilHit * 1000:F1}ms > earlyWindow={earlyWindow + _audioLatencyMs:F0}ms)");
                        _currentHitIndex = i + 1;
                        return;
                    }

                    // Valid hit window: within early window or late window (shifted by latency)
                    if (timeUntilHit >= -_hitWindowLateMs / 1000f + latencySeconds && timeUntilHit <= earlyWindow / 1000f + latencySeconds)
                    {
                        if (obj.Type == BeatCatchHitType.Slider)
                        {
                            // Start the slider: mark as active, player must drag to end
                            _activeSliderIndex = i;
                            _sliderStartHit = true;
                            _sliderDragged = false;
                            _sliderMaxProgress = 0f;
                            _sliderPathValid = true;
                            RecordHitTiming(timeUntilHit, BeatCatchHitType.Slider);
                            _debugLog?.LogEvent(currentTime, $"START Slider objTime={obj.Time:F4}s timeUntilHit={timeUntilHit * 1000:F1}ms @ ({mouseX},{mouseY})");
                            // Don't advance _currentHitIndex yet � slider is still active
                        }
                        else
                        {
                            RegisterHit();
                            RecordHitTiming(timeUntilHit, BeatCatchHitType.Circle);
                            AddHitEffect(obj.X, obj.Y, currentTime);
                            _currentHitIndex = i + 1;
                            _debugLog?.LogHit(currentTime, "Circle", obj.Time, timeUntilHit, mouseX, mouseY);
                        }
                        return;
                    }

                    // Clicked near the object but outside the valid window (too late)
                    if (obj.Type == BeatCatchHitType.Slider)
                    {
                        _failedSliderIndex = i;
                    }
                    RegisterMiss();
                    AddFailEffect(obj.X, obj.Y, currentTime);
                    _debugLog?.LogMiss(currentTime, obj.Type.ToString(), obj.Time, timeUntilHit, $"too late (timeUntilHit={timeUntilHit * 1000:F1}ms < -lateWindow={-_hitWindowLateMs + _audioLatencyMs:F0}ms)");
                    _currentHitIndex = i + 1;
                    return;
                }
            }

            // No object was within the approach window / click radius: the click did nothing.
            // This is a key signal for "unfair" gameplay � the player clicked but there was
            // nothing to hit (or the nearest object was too far / too far in the future).
            if (_debugLog != null)
            {
                float nearestTime = _currentHitIndex < _hitObjects.Count ? _hitObjects[_currentHitIndex].Time - currentTime : float.NaN;
                _debugLog?.LogEvent(currentTime, $"NO-OBJECT click (no hit/miss)  nearestObjTime={nearestTime:F4}s (timeUntilHit={nearestTime * 1000:F1}ms)  approachWindow={ApproachSeconds * 1000:F0}ms");
            }
        }

        private float GetDistanceToHitObject(int mouseX, int mouseY, BeatCatchHitObject obj)
        {
            switch (obj.Type)
            {
                case BeatCatchHitType.Circle:
                    return (float)Math.Sqrt(Math.Pow(mouseX - obj.X, 2) + Math.Pow(mouseY - obj.Y, 2));

                case BeatCatchHitType.Slider:
                    // Distance from point to line segment
                    return PointToSegmentDistance(mouseX, mouseY, obj.X, obj.Y, obj.EndX, obj.EndY);

                case BeatCatchHitType.Spinner:
                    return (float)Math.Sqrt(Math.Pow(mouseX - obj.X, 2) + Math.Pow(mouseY - obj.Y, 2));

                default:
                    return float.MaxValue;
            }
        }

        private static float PointToSegmentDistance(int px, int py, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float lenSq = dx * dx + dy * dy;

            if (lenSq < 0.001f)
            {
                return (float)Math.Sqrt(Math.Pow(px - x1, 2) + Math.Pow(py - y1, 2));
            }

            float t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lenSq, 0f, 1f);
            float closestX = x1 + t * dx;
            float closestY = y1 + t * dy;
            return (float)Math.Sqrt(Math.Pow(px - closestX, 2) + Math.Pow(py - closestY, 2));
        }

        private static float GetSliderProgress(BeatCatchHitObject obj, int mouseX, int mouseY)
        {
            float dx = obj.EndX - obj.X;
            float dy = obj.EndY - obj.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq <= 0.001f)
            {
                return 0f;
            }

            return Math.Clamp(((mouseX - obj.X) * dx + (mouseY - obj.Y) * dy) / lenSq, 0f, 1f);
        }

        private static float GetSliderTargetProgress(BeatCatchHitObject obj, float currentTime)
        {
            if (obj.Duration <= 0f)
            {
                return 1f;
            }

            return Math.Clamp((currentTime - obj.Time) / obj.Duration, 0f, 1f);
        }

        private bool IsSliderPointOnPath(BeatCatchHitObject obj, int mouseX, int mouseY)
        {
            return PointToSegmentDistance(mouseX, mouseY, obj.X, obj.Y, obj.EndX, obj.EndY)
                <= _circleRadius * 1.5f;
        }

        private void HandleTaikoClick(int mouseX, int mouseY, float currentTime)
        {
            int screenW = this.ClientSize.Width;
            int drumCenterX = screenW / 2;
            int drumCenterY = this.ClientSize.Height / 2;
            int drumRadius = 200;

            // Check if click is on the drum
            float dist = (float)Math.Sqrt(Math.Pow(mouseX - drumCenterX, 2) + Math.Pow(mouseY - drumCenterY, 2));
            if (dist > drumRadius)
            {
                _debugLog?.LogEvent(currentTime, $"NO-OBJECT taiko click outside drum (dist={dist:F0} > drumRadius={drumRadius})");
                return;
            }

            // Determine if it's a Don (center) or Ka (edge) hit
            bool isCenter = dist < drumRadius * 0.4f;

            for (int i = _currentHitIndex; i < _taikoBeats.Count; i++)
            {
                var beat = _taikoBeats[i];
                float timeUntilHit = beat.Time - currentTime;

                // If the beat is still far in the future, stop searching
                if (timeUntilHit > _taikoHitWindowEarlyMs / 1000f)
                {
                    break;
                }

                bool typeMatch = (isCenter && beat.Type == TaikoBeatType.Don)
                    || (!isCenter && beat.Type == TaikoBeatType.Ka);

                if (typeMatch)
                {
                    // The audio output latency shifts the heard beat later than the sample
                    // position. Shift the hit windows by the measured latency so the optimal
                    // click time lines up with the actually heard beat (not the sample time).
                    float latencySeconds = _audioLatencyMs / 1000f;

                    // Too-early click = instant miss
                    if (timeUntilHit > _taikoHitWindowEarlyMs / 1000f + latencySeconds)
                    {
                        RegisterMiss();
                        AddFailEffect(drumCenterX, drumCenterY, currentTime);
                        _debugLog?.LogMiss(currentTime, beat.Type.ToString(), beat.Time, timeUntilHit, $"too early (timeUntilHit={timeUntilHit * 1000:F1}ms > earlyWindow={_taikoHitWindowEarlyMs + _audioLatencyMs:F0}ms)");
                        _currentHitIndex = i + 1;
                        return;
                    }

                    // Valid hit window: within early window or late window (shifted by latency)
                    if (timeUntilHit >= -_taikoHitWindowLateMs / 1000f + latencySeconds && timeUntilHit <= _taikoHitWindowEarlyMs / 1000f + latencySeconds)
                    {
                        RegisterHit();
                        _currentHitIndex = i + 1;
                        _debugLog?.LogHit(currentTime, beat.Type.ToString(), beat.Time, timeUntilHit, mouseX, mouseY);
                        return;
                    }

                    // Clicked on drum but outside valid window
                    RegisterMiss();
                        AddFailEffect(drumCenterX, drumCenterY, currentTime);
                        _debugLog?.LogMiss(currentTime, beat.Type.ToString(), beat.Time, timeUntilHit, $"outside window (timeUntilHit={timeUntilHit * 1000:F1}ms, latency={_audioLatencyMs:F0}ms)");
                        _currentHitIndex = i + 1;
                        return;
                }
            }
        }

        // --- Rendering (layered window) ---------------------------------------

        /// <summary>
        /// Renders the game to a 32-bit ARGB bitmap and pushes it to the screen
        /// via UpdateLayeredWindow for true per-pixel transparency.
        /// Background pixels use the persisted opacity setting,
        /// game element pixels are at 100% alpha (fully opaque).
        /// </summary>
        private void PushLayeredBitmap()
        {
            if (this.IsDisposed || !this.Visible)
            {
                return;
            }

            int width = this.ClientSize.Width;
            int height = this.ClientSize.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // The persisted black background becomes slightly darker during count-in.
                // The taskbar area is NOT painted opaque � the taskbar stays visible.
                int bgAlpha = GetBackgroundAlpha();
                using var bgBrush = new SolidBrush(Color.FromArgb(bgAlpha, 0, 0, 0));
                g.FillRectangle(bgBrush, 0, 0, width, height);

                int taskbarHeight = GetTaskbarHeight();

                // When paused, use the last game clock value (Stopwatch keeps its elapsed time when stopped)
                float currentTime = _gameRunning ? (float)_gameClock.Elapsed.TotalSeconds : 0f;

                // Count-in: show 3-2-1 every two track beats, each fading in and out quickly.
                if (_countInActive)
                {
                    float countInElapsed = (float)Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency - _countInStart;
                    float countInStepSeconds = GetCountInStepSeconds();
                    int step = (int)(countInElapsed / countInStepSeconds);
                    if (step >= 0 && step < 3)
                    {
                        float stepElapsed = countInElapsed - step * countInStepSeconds;
                        float fadeSeconds = Math.Min(CountInFadeSeconds, countInStepSeconds * 0.45f);
                        float alpha = stepElapsed < fadeSeconds
                            ? stepElapsed / fadeSeconds
                            : Math.Clamp(1f - (stepElapsed - fadeSeconds) / Math.Max(0.001f, countInStepSeconds - fadeSeconds), 0f, 1f);
                        int a = Math.Clamp((int)(255 * alpha), 0, 255);
                        if (a > 0)
                        {
                            using var countFont = new Font("Consolas", 120f, FontStyle.Bold);
                            using var countBrush = new SolidBrush(Color.FromArgb(a, 255, 255, 255));
                            string countText = (3 - step).ToString();
                            var countSize = g.MeasureString(countText, countFont);
                            g.DrawString(countText, countFont, countBrush, width / 2f - countSize.Width / 2f, height / 2f - countSize.Height / 2f);
                        }
                    }
                }

                // During the count-in gameplay is suppressed, so no elements are shown and
                // no misses are registered. The clock continues to preserve audio alignment.
                if (!_countInActive)
                {
                    if (_isTaikoMode)
                    {
                        PaintTaiko(g, currentTime, width, height);
                    }
                    else
                    {
                        PaintBeatCatchClassic(g, currentTime, width, height);
                    }

                    // Feedback effects and combo popups are painted above the game objects.
                    lock (_lock)
                    {
                        PaintHitEffects(g, currentTime);
                        PaintFailEffects(g, currentTime);
                        PaintComboPopups(g, currentTime);
                    }
                }

                lock (_lock)
                {
                    PaintTimingFeedback(g, width);
                }

                // Score display � fully opaque
                using var font = new Font("Consolas", 20f, FontStyle.Bold);
                using var brush = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
                lock (_lock)
                {
                    string scoreText = _isTaikoMode
                        ? $"Hits: {_score}   Missed: {_missed}   BPM: {_bpm:F0}"
                        : $"Hits: {_score}   Missed: {_missed}   Combo: {_comboStreak}   BPM: {_bpm:F0}";
                    g.DrawString(scoreText, font, brush, 20, 20);

                    using var numericScoreFont = new Font("Consolas", 16f, FontStyle.Bold);
                    string numericScoreText = $"Score: {FormatScore(_scorePoints)}  x{GetCurrentScoreMultiplier():F2}";
                    var numericScoreSize = g.MeasureString(numericScoreText, numericScoreFont);
                    int numericScoreY = height - taskbarHeight - 48;
                    g.DrawString(numericScoreText, numericScoreFont, brush, width - numericScoreSize.Width - 20, numericScoreY);
                }

                // Verbose debug logging indicator � top-right, only when the debug log is active.
                if (_debugLog != null)
                {
                    using var dbgFont = new Font("Consolas", 14f, FontStyle.Bold);
                    using var dbgBrush = new SolidBrush(Color.FromArgb(255, 120, 255, 120));
                    string dbgText = "Verbose Debug Logging";
                    var dbgSize = g.MeasureString(dbgText, dbgFont);
                    g.DrawString(dbgText, dbgFont, dbgBrush, width - dbgSize.Width - 20, 20);
                }

                // Progress bar � fully opaque, positioned above the taskbar with a margin
                // (no UI in the taskbar area itself).
                if (_totalDuration > 0)
                {
                    float progress = Math.Clamp(currentTime / _totalDuration, 0f, 1f);
                    int barWidth = width - 40;
                    int barY = height - taskbarHeight - 24; // 24px margin above the taskbar
                    using var barBgBrush = new SolidBrush(Color.FromArgb(255, 60, 60, 80));
                    using var barFgBrush = new SolidBrush(Color.FromArgb(255, 100, 200, 255));
                    g.FillRectangle(barBgBrush, 20, barY, barWidth, 10);
                    g.FillRectangle(barFgBrush, 20, barY, (int)(barWidth * progress), 10);
                }

                // Hint � fully opaque, positioned above the progress bar (no UI in the taskbar area)
                using var hintFont = new Font("Consolas", 11f);
                using var hintBrush = new SolidBrush(Color.FromArgb(255, 120, 120, 140));
                string hint = _isTaikoMode ? "Click center=Don, edge=Ka | ESC to pause" : "Click circles/sliders/spinners | ESC to pause";
                int hintY = height - taskbarHeight - 48;
                g.DrawString(hint, hintFont, hintBrush, 20, hintY);
            }

            // Push the bitmap to the screen via UpdateLayeredWindow
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bitmap.GetHbitmap();
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);

            try
            {
                POINT dst = new POINT { X = this.Location.X, Y = this.Location.Y };
                SIZE size = new SIZE { CX = width, CY = height };
                POINT src = new POINT { X = 0, Y = 0 };
                BLENDFUNCTION blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(
                    this.Handle, screenDc, ref dst, ref size,
                    memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void PaintBeatCatchClassic(Graphics g, float currentTime, int width, int height)
        {
            if (!_beatmapReady)
            {
                return;
            }

            lock (_lock)
            {
                if (_completedSliderIndex >= 0
                    && _completedSliderIndex < _hitObjects.Count
                    && _completedSliderIndex < _currentHitIndex
                    && currentTime < GetVisualEndTime(_hitObjects[_completedSliderIndex]))
                {
                    var completedSlider = _hitObjects[_completedSliderIndex];
                    PaintSlider(
                        g,
                        completedSlider,
                        completedSlider.Time - currentTime,
                        currentTime,
                        completedVisual: true);
                }

                // Draw every currently visible unresolved object. Their interaction intervals
                // are still sequential, but their approach circles can overlap on screen.
                for (int i = _currentHitIndex; i < _hitObjects.Count; i++)
                {
                    var obj = _hitObjects[i];
                    float timeUntilHit = obj.Time - currentTime;
                    float visualStart = obj.Time - GetApproachWindowSeconds(obj);
                    if (currentTime < visualStart)
                    {
                        continue;
                    }

                    bool activeSlider = i == _activeSliderIndex && _sliderStartHit;
                    if ((currentTime > GetVisualEndTime(obj) && !activeSlider) || _failedSliderIndex == i)
                    {
                        continue;
                    }

                    switch (obj.Type)
                    {
                        case BeatCatchHitType.Circle:
                            PaintCircle(g, obj, timeUntilHit, currentTime);
                            break;

                        case BeatCatchHitType.Slider:
                            PaintSlider(g, obj, timeUntilHit, currentTime);
                            break;

                        case BeatCatchHitType.Spinner:
                            PaintSpinner(g, obj, timeUntilHit, currentTime);
                            break;
                    }
                }
            }
        }

        private void PaintCircle(Graphics g, BeatCatchHitObject obj, float timeUntilHit, float currentTime)
        {
            // Post-hit fade: after the hit time, the circle fades out over PostHitFadeSeconds
            float fadeAlpha = 1f;
            if (timeUntilHit < 0)
            {
                fadeAlpha = Math.Clamp(1f + timeUntilHit / PostHitFadeSeconds, 0f, 1f);
            }

            // Approach circle: shrinks from 3x to 1x as it approaches
            float approachScale = 1f + (timeUntilHit / ApproachSeconds) * 2f;
            float approachRadius = _circleRadius * approachScale;

            // Approach circle (fading in as it approaches) � fully opaque
            int approachAlpha = Math.Clamp((int)((1f - timeUntilHit / ApproachSeconds) * 255 * fadeAlpha), 0, 255);
            if (approachAlpha > 0)
            {
                using var approachPen = new Pen(Color.FromArgb(approachAlpha, 255, 100, 100), 2);
                g.DrawEllipse(approachPen, obj.X - approachRadius, obj.Y - approachRadius, approachRadius * 2, approachRadius * 2);
            }

            // Hit circle � fully opaque, with post-hit fade
            bool inHitWindow = timeUntilHit >= -_hitWindowLateMs / 1000f && timeUntilHit <= _hitWindowEarlyMs / 1000f;
            Color circleColor = inHitWindow ? Color.FromArgb(255, 255, 200, 50) : Color.FromArgb(255, 255, 100, 100);
            int fillAlpha = (int)(255 * fadeAlpha);
            if (fillAlpha > 0)
            {
                using var brush = new SolidBrush(Color.FromArgb(fillAlpha, circleColor.R, circleColor.G, circleColor.B));
                g.FillEllipse(brush, obj.X - _circleRadius, obj.Y - _circleRadius, _circleRadius * 2, _circleRadius * 2);
            }
            int penAlpha = (int)(255 * fadeAlpha);
            if (penAlpha > 0)
            {
                using var pen = new Pen(Color.FromArgb(penAlpha, 255, 255, 255), 3);
                g.DrawEllipse(pen, obj.X - _circleRadius, obj.Y - _circleRadius, _circleRadius * 2, _circleRadius * 2);
            }

            // Number in the center (position within the group)
            DrawObjectNumber(g, obj, fadeAlpha);
        }

        private void PaintSlider(
            Graphics g,
            BeatCatchHitObject obj,
            float timeUntilHit,
            float currentTime,
            bool completedVisual = false)
        {
            // Sliders stay visible for their beat-aligned duration and fade out afterwards.
            float fadeAlpha = 1f;
            bool isActive = _activeSliderIndex >= 0
                && _activeSliderIndex < _hitObjects.Count
                && _hitObjects[_activeSliderIndex].Time == obj.Time
                && _sliderStartHit;
            if (timeUntilHit < -obj.Duration && !isActive)
            {
                fadeAlpha = Math.Clamp(1f + (timeUntilHit + obj.Duration) / PostHitFadeSeconds, 0f, 1f);
            }

            float completionProgress = 0f;
            if (completedVisual)
            {
                float completionAge = Math.Max(0f, currentTime - _completedSliderHitTime);
                fadeAlpha = Math.Clamp(1f - completionAge / CompletedSliderFadeSeconds, 0f, 1f);
                completionProgress = Math.Clamp(completionAge / CompletedSliderWhiteShiftSeconds, 0f, 1f);
            }

            // The approach circle only exists before the scheduled start. Reusing the
            // approach formula after the hit would make it grow backward during the fade.
            if (timeUntilHit >= 0f)
            {
                float approachScale = 1f + (timeUntilHit / ApproachSeconds) * 2f;
                float approachRadius = _circleRadius * approachScale;
                int approachAlpha = Math.Clamp((int)((1f - timeUntilHit / ApproachSeconds) * 255 * fadeAlpha), 0, 255);
                using var approachPen = new Pen(Color.FromArgb(approachAlpha, 100, 200, 255), 2);
                g.DrawEllipse(approachPen, obj.X - approachRadius, obj.Y - approachRadius, approachRadius * 2, approachRadius * 2);
            }

            // Slider body � fully opaque (highlighted when active)
            int bodyAlpha = (int)(255 * fadeAlpha);
            if (bodyAlpha > 0)
            {
                Color bodyColor = completedVisual
                    ? BlendTimingColors(Color.FromArgb(255, 150, 255, 100), Color.White, completionProgress)
                    : isActive
                        ? Color.FromArgb(255, 150, 255, 100)
                        : Color.FromArgb(255, 100, 200, 255);
                using var sliderPen = new Pen(bodyColor, isActive || completedVisual ? 16 : 12);
                g.DrawLine(sliderPen, obj.X, obj.Y, obj.EndX, obj.EndY);
            }

            // Slider head animation: the white point follows the scheduled target progress;
            // the mouse is judged against it but never moves the point.
            bool showProgressHead = isActive || completedVisual;
            if (showProgressHead && bodyAlpha > 0)
            {
                float displayedProgress = completedVisual
                    ? 1f
                    : GetSliderTargetProgress(obj, currentTime);

                float headX = obj.X + (obj.EndX - obj.X) * displayedProgress;
                float headY = obj.Y + (obj.EndY - obj.Y) * displayedProgress;

                using var headBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
                g.FillEllipse(headBrush, headX - 11, headY - 11, 22, 22);
            }

            // Start circle � fully opaque
            int fillAlpha = (int)(255 * fadeAlpha);
            if (fillAlpha > 0)
            {
                Color startColor = completedVisual
                    ? BlendTimingColors(Color.FromArgb(255, 150, 255, 100), Color.White, completionProgress)
                    : isActive
                        ? Color.FromArgb(255, 150, 255, 100)
                        : Color.FromArgb(255, 100, 200, 255);
                using var brush = new SolidBrush(startColor);
                g.FillEllipse(brush, obj.X - _circleRadius, obj.Y - _circleRadius, _circleRadius * 2, _circleRadius * 2);
            }
            int penAlpha = (int)(255 * fadeAlpha);
            if (penAlpha > 0)
            {
                using var pen = new Pen(Color.FromArgb(penAlpha, 255, 255, 255), 3);
                g.DrawEllipse(pen, obj.X - _circleRadius, obj.Y - _circleRadius, _circleRadius * 2, _circleRadius * 2);
            }

            // Number in the center of the start circle (position within the group)
            DrawObjectNumber(g, obj, fadeAlpha);

            // End circle � fully opaque (pulsing when active to guide the player)
            if (fillAlpha > 0)
            {
                Color endColor = completedVisual
                    ? BlendTimingColors(Color.FromArgb(255, 255, 255, 150), Color.White, completionProgress)
                    : isActive
                        ? Color.FromArgb(255, 255, 255, 150)
                        : Color.FromArgb(255, 255, 200, 100);
                float endRadius = isActive ? _circleRadius + 8 : _circleRadius;
                using var brush = new SolidBrush(endColor);
                g.FillEllipse(brush, obj.EndX - endRadius, obj.EndY - endRadius, endRadius * 2, endRadius * 2);
            }
            if (penAlpha > 0)
            {
                float endRadius = isActive ? _circleRadius + 8 : _circleRadius;
                using var pen = new Pen(Color.FromArgb(penAlpha, 255, 255, 255), 3);
                g.DrawEllipse(pen, obj.EndX - endRadius, obj.EndY - endRadius, endRadius * 2, endRadius * 2);
            }
        }

        private void PaintSpinner(Graphics g, BeatCatchHitObject obj, float timeUntilHit, float currentTime)
        {
            // The spinner stays fully visible for its ENTIRE duration (the approach ring
            // shrinks over that span to show how much time is left). It only fades out
            // AFTER the duration has elapsed (the post-hit fade window).
            float fadeAlpha = 1f;
            if (timeUntilHit < -obj.Duration)
            {
                fadeAlpha = Math.Clamp(1f + (timeUntilHit + obj.Duration) / PostHitFadeSeconds, 0f, 1f);
            }
            int alpha = (int)(255 * fadeAlpha);
            if (alpha <= 0)
            {
                return;
            }

            // The spinner's outer ring starts very large (~80% of the screen height) and
            // shrinks at a constant rate to the center. It begins shrinking BEFORE the hit
            // time (an approach lead-in) so the player can see it coming, and keeps shrinking
            // until the spinner's Duration has fully elapsed (when the ring reaches the center
            // the spinner is finished).
            float screenH = this.ClientSize.Height;
            float screenW = this.ClientSize.Width;
            float maxRadius = 0.8f * Math.Min(screenH, screenW) / 2f;
            float minRadius = _spinnerRadius * 0.5f;
            float approachLead = Math.Min(ApproachSeconds, obj.Duration * 0.5f);
            float totalApproach = obj.Duration + approachLead;
            float elapsed = currentTime - (obj.Time - approachLead);
            float progress = Math.Clamp(elapsed / totalApproach, 0f, 1f);
            float ringRadius = maxRadius + (minRadius - maxRadius) * progress;

            // Interior of the approach ring: a brighter, semi-transparent white fill so the
            // spinner is clearly visible while it approaches (the area inside the ring).
            int interiorAlpha = (int)(90 * fadeAlpha);
            if (interiorAlpha > 0)
            {
                using var interiorBrush = new SolidBrush(Color.FromArgb(interiorAlpha, 255, 255, 255));
                g.FillEllipse(interiorBrush, obj.X - ringRadius, obj.Y - ringRadius, ringRadius * 2, ringRadius * 2);
            }

            // Outer ring (the shrinking approach ring) - fully opaque
            using (var pen = new Pen(Color.FromArgb(alpha, 255, 200, 50), 4))
            {
                g.DrawEllipse(pen, obj.X - ringRadius, obj.Y - ringRadius, ringRadius * 2, ringRadius * 2);
            }

            // Indicator line: while the spinner is grabbed, the indicator points directly at
            // the mouse cursor in real time (so it "looks at" the player as they whirl around
            // it). While it is NOT grabbed, it slowly rotates on its own (idle spin) so the
            // spinner is easy to see and read.
            float angle;
            bool isActive = _activeSpinnerIndex >= 0
                && _activeSpinnerIndex < _hitObjects.Count
                && _activeSpinnerIndex == _currentHitIndex
                && _hitObjects[_activeSpinnerIndex].Type == BeatCatchHitType.Spinner
                && _hitObjects[_activeSpinnerIndex].Time == obj.Time;
            if (isActive)
            {
                float dx = _mouseX - obj.X;
                float dy = _mouseY - obj.Y;
                angle = (Math.Abs(dx) < 1f && Math.Abs(dy) < 1f)
                    ? _spinnerLastAngle
                    : (float)Math.Atan2(dy, dx);
            }
            else
            {
                angle = _spinnerIdleAngle;
            }

            float endX = obj.X + (float)Math.Cos(angle) * ringRadius;
            float endY = obj.Y + (float)Math.Sin(angle) * ringRadius;
            using (var pen = new Pen(Color.FromArgb(alpha, isActive ? 255 : 200, isActive ? 255 : 200, isActive ? 100 : 255), 4))
            {
                g.DrawLine(pen, obj.X, obj.Y, endX, endY);
            }

            // Rotation progress: while active, draw a bright arc from the start angle to the
            // current mouse angle so the player can see how far they have spun.
            if (isActive)
            {
                float startAngle = _spinnerLastAngle - _spinnerAccumulatedAngle;
                float sweep = _spinnerAccumulatedAngle;
                if (Math.Abs(sweep) > 0.02f)
                {
                    float arcSize = ringRadius * 2;
                    var arcRect = new RectangleF(obj.X - ringRadius, obj.Y - ringRadius, arcSize, arcSize);
                    float startDeg = (float)(startAngle * 180.0 / Math.PI);
                    float sweepDeg = (float)(sweep * 180.0 / Math.PI);
                    using var arcPen = new Pen(Color.FromArgb(alpha, 120, 255, 120), 6);
                    g.DrawArc(arcPen, arcRect, startDeg, sweepDeg);
                }

                // Progress ring: fills up as the player completes rotations
                float rotProgress = _spinnerRotationCompleted
                    ? 1f
                    : Math.Clamp(Math.Abs(_spinnerAccumulatedAngle) / (2f * (float)Math.PI), 0f, 1f);
                if (rotProgress > 0.01f)
                {
                    float ringSize = ringRadius * 2 * 1.15f;
                    var ringRect = new RectangleF(obj.X - ringSize / 2f, obj.Y - ringSize / 2f, ringSize, ringSize);
                    using var ringPen = new Pen(Color.FromArgb(alpha, 80, 220, 80), 3);
                    g.DrawArc(ringPen, ringRect, -90f, rotProgress * 360f);
                }
            }

            // Center dot
            using var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
            g.FillEllipse(brush, obj.X - 8, obj.Y - 8, 16, 16);

            // Spinners show only the green combo popups (x1, x2, ...) for completed
            // rotations - no group number. The combo popups are drawn in PaintComboPopups.
        }
        /// <summary>
        /// Draws the object's number (its position within the current group) in the center
        /// of the hit object, so the player can see the order in which to click.
        /// </summary>
        private void DrawObjectNumber(Graphics g, BeatCatchHitObject obj, float fadeAlpha)
        {
            if (obj.Number <= 0)
            {
                return;
            }

            int a = Math.Clamp((int)(255 * fadeAlpha), 0, 255);
            if (a <= 0)
            {
                return;
            }

            using var font = new Font("Consolas", 18f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(a, 255, 255, 255));
            string text = obj.Number.ToString();
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, obj.X - size.Width / 2f, obj.Y - size.Height / 2f);
        }

        private void PaintTaiko(Graphics g, float currentTime, int width, int height)
        {
            int drumCenterX = width / 2;
            int drumCenterY = height / 2;
            int drumRadius = 200;

            // Draw the drum
            using (var drumBrush = new SolidBrush(Color.FromArgb(200, 139, 69, 19)))
            {
                g.FillEllipse(drumBrush, drumCenterX - drumRadius, drumCenterY - drumRadius, drumRadius * 2, drumRadius * 2);
            }
            using (var rimPen = new Pen(Color.FromArgb(255, 80, 40, 10), 8))
            {
                g.DrawEllipse(rimPen, drumCenterX - drumRadius, drumCenterY - drumRadius, drumRadius * 2, drumRadius * 2);
            }

            // Center circle (Don area)
            using (var centerBrush = new SolidBrush(Color.FromArgb(150, 200, 150, 50)))
            {
                g.FillEllipse(centerBrush, drumCenterX - drumRadius * 0.4f, drumCenterY - drumRadius * 0.4f, drumRadius * 0.8f, drumRadius * 0.8f);
            }

            // Draw approaching beats with post-hit fade
            lock (_lock)
            {
                for (int i = _currentHitIndex; i < _taikoBeats.Count; i++)
                {
                    var beat = _taikoBeats[i];
                    float timeUntilHit = beat.Time - currentTime;

                    // Stop when well past the post-hit fade window
                    if (timeUntilHit < -PostHitFadeSeconds)
                    {
                        break;
                    }

                    // Skip beats that haven't entered the approach window yet
                    if (timeUntilHit > ApproachSeconds)
                    {
                        continue;
                    }

                    // Post-hit fade
                    float fadeAlpha = 1f;
                    if (timeUntilHit < 0)
                    {
                        fadeAlpha = Math.Clamp(1f + timeUntilHit / PostHitFadeSeconds, 0f, 1f);
                    }

                    // Approach ring: shrinks toward the drum � full opacity
                    float approachScale = 1f + (timeUntilHit / ApproachSeconds) * 2f;
                    float ringRadius = drumRadius * approachScale;
                    int alpha = Math.Clamp((int)((1f - timeUntilHit / ApproachSeconds) * 255 * fadeAlpha), 0, 255);

                    if (alpha <= 0)
                    {
                        continue;
                    }

                    Color beatColor = beat.Type == TaikoBeatType.Don
                        ? Color.FromArgb(alpha, 255, 200, 50)
                        : Color.FromArgb(alpha, 100, 200, 255);

                    using var pen = new Pen(beatColor, 4);
                    g.DrawEllipse(pen, drumCenterX - ringRadius, drumCenterY - ringRadius, ringRadius * 2, ringRadius * 2);
                }
            }
        }

        // --- ESC / Pause menu -------------------------------------------------

        private void BeatClickerGameForm_KeyDown(object? sender, KeyEventArgs e)
        {
            // ESC is handled in WndProc for reliability with layered windows.
            // This handler is kept as a fallback but does nothing for ESC.
        }

        private void BeatClickerGameForm_KeyUp(object? sender, KeyEventArgs e)
        {
            // Esc key-up is a SAFE BOUNDARY: it does NOT toggle the menu. It only marks the
            // key as released so that a subsequent (new) Esc key-down can close the menu.
            // This is what prevents rapid pause/continue toggling when Esc is held down.
            if (e.KeyCode == Keys.Escape)
            {
                HandleEscKeyUp();
            }
        }

        /// <summary>
        /// Handles an Esc key-up (from the global hotkey or the WM_KEYUP fallback).
        /// The key-up is a SAFE BOUNDARY: it never toggles the menu. It only marks the key
        /// as released so that a subsequent (new) Esc key-down can close the pause menu.
        /// </summary>
        private void HandleEscKeyUp()
        {
            _escKeyReleased = true;
            _escCloseRepausePending = false;
        }

        private void TogglePauseMenu()
        {
            if (_pauseMenu is { IsDisposed: false })
            {
                _pauseMenu.Close();
                _pauseMenu = null;
                ResumeGame();
                return;
            }

            PauseGame();
            try
            {
                _pauseMenu = new BeatClickerPauseMenuForm(this);
                _pauseMenu.Show();
                _pauseMenu.BringToFront();
                _pauseMenu.Activate();
                _pauseMenu.Focus();

                // The pause menu is a separate top-level window, so it must be made the
                // foreground window at the Win32 level for it to receive keyboard input.
                // WinForms Activate()/Focus() alone is not reliable here (the game form is a
                // layered window and the menu is shown from a WndProc handler).
                if (_pauseMenu.IsHandleCreated)
                {
                    SetForegroundWindow(_pauseMenu.Handle);
                }
                _pauseMenu.BeginInvoke(new Action(() =>
                {
                    if (_pauseMenu is { IsDisposed: false, IsHandleCreated: true })
                    {
                        SetForegroundWindow(_pauseMenu.Handle);
                        _pauseMenu.Activate();
                        _pauseMenu.Focus();
                    }
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BeatClickerGame pause menu error: {ex.Message}");
                _pauseMenu = null;
                ResumeGame();
            }
        }

        private void ClosePauseMenuAndResume()
        {
            // Close the menu and resume the game. The menu's FormClosing handler does NOT
            // auto-resume (it's a no-op for the game state), so we resume here. If the Esc
            // key is still held, the global hotkey re-pauses (see _escCloseRepausePending);
            // if it was released, the game stays resumed. This guarantees the menu always
            // closes on a normal Esc press (no stuck-open state).
            if (_pauseMenu is { IsDisposed: false })
            {
                _pauseMenu.Close();
                _pauseMenu = null;
            }
            ResumeGame();
        }

        /// <summary>
        /// Handles an Esc key-down (from the global hotkey or the WM_KEYDOWN fallback).
        /// - If the game is running and NOT paused: open the pause menu (and mark the key
        ///   as not-released so the same press can't immediately close it again).
        /// - If the game is paused and the menu is open: only close it (Continue) if the key
        ///   was previously RELEASED (i.e. this is a new, separate press). Holding Esc down
        ///   never toggles, because _escKeyReleased stays false while the key is held.
        /// </summary>
        private void HandleEscKeyDown()
        {
            if (!_gameRunning)
            {
                return;
            }

            if (!_paused)
            {
                // Re-pause guard: the menu was just closed by an Esc key-down (Continue) and
                // its FormClosing handler auto-resumed the game. If the Esc key is still held
                // (the user is holding it), re-pause so the game doesn't briefly resume and
                // re-pause (the "flicker" bug). This happens exactly once per press.
                if (_escCloseRepausePending)
                {
                    _escCloseRepausePending = false;
                    _escKeyReleased = false;
                    PauseGame();
                    return;
                }

                // Open the pause menu. Mark the key as held so this same press can't
                // immediately close it (the close requires a new press after release).
                _escKeyReleased = false;
                TogglePauseMenu();
            }
            else if (_pauseMenu is { IsDisposed: false, Visible: true })
            {
                // Only close (Continue) on a NEW press: the key must have been released
                // since the menu was opened. If Esc is still held, do nothing (no toggle).
                if (_escKeyReleased)
                {
                    _escKeyReleased = false;
                    _escCloseRepausePending = true; // the menu's FormClosing will resume; re-pause if still held
                    ClosePauseMenuAndResume();
                }
            }
        }

        private void PauseGame()
        {
            // The game pause (clock + timer) is independent of the audio pause, so a flaky
            // audio pause can never prevent the pause menu from opening.
            _paused = true;
            _gameClock.Stop();

            // Freeze the count-in while paused: the count-in uses a wall-clock stopwatch
            // (_countInStart), which would keep advancing while the game is paused. Capture
            // the elapsed count-in time so it resumes exactly where it was left off.
            if (_countInActive)
            {
                _countInPausedElapsed = (float)Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency - _countInStart;
            }

            _debugLog?.LogEvent((float)_gameClock.Elapsed.TotalSeconds, "PAUSED (countInActive=" + _countInActive + ")");

            if (!_audioPausedByGame)
            {
                // Pause the audio directly (no async, no re-initialization) to avoid the
                // stutter that PauseAsync's position-recalculation + cross-thread marshaling caused.
                RunAudioOnUiThread(async () =>
                {
                    try { await _audio.PauseAsync().ConfigureAwait(false); }
                    catch (Exception ex) { Debug.WriteLine($"BeatClickerGame pause audio error: {ex.Message}"); }
                });
                _audioPausedByGame = true;
            }
            try { PushLayeredBitmap(); }
            catch (Exception ex) { Debug.WriteLine($"BeatClickerGame pause render error: {ex.Message}"); }
        }

        private void ResumeGame()
        {
            _paused = false;
            _gameClock.Start();

            // Resume the count-in from where it was frozen: rebase the count-in stopwatch
            // so the elapsed time continues from _countInPausedElapsed instead of having
            // advanced (and possibly finished) while the game was paused.
            if (_countInActive)
            {
                _countInStart = (float)Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency - _countInPausedElapsed;
            }

            _debugLog?.LogEvent((float)_gameClock.Elapsed.TotalSeconds, "RESUMED (countInActive=" + _countInActive + ")");

            if (_audioPausedByGame)
            {
                RunAudioOnUiThread(async () =>
                {
                    try { await _audio.PauseAsync().ConfigureAwait(false); }
                    catch (Exception ex) { Debug.WriteLine($"BeatClickerGame resume audio error: {ex.Message}"); }
                });
                _audioPausedByGame = false;
            }
            try { PushLayeredBitmap(); }
            catch (Exception ex) { Debug.WriteLine($"BeatClickerGame resume render error: {ex.Message}"); }
        }

        private void RestartGame()
        {
            SaveRunSummaryIfNeeded();
            LoadBackgroundOpacity();
            _playbackCts?.Cancel();
            _audioPausedByGame = false;
            RunAudioOnUiThread(() => _audio.StopAsync());

            _playbackCts = new CancellationTokenSource();
            RunAudioOnUiThread(() => _audio.PlayAsync(_playbackCts.Token));

            // Regenerate the beatmap asynchronously (same as StartGame).
            _beatmapReady = false;
            Task.Run(() =>
            {
                try { GenerateBeatMap(); }
                catch (Exception ex) { Debug.WriteLine($"BeatClickerGame beatmap generation error: {ex.Message}"); }
                this.BeginInvoke(new Action(() => { _beatmapReady = true; }));
            });

            ResetGame();
            _runHasStarted = true;
            _runStartedUtc = DateTimeOffset.UtcNow;
            _paused = false;
            // Re-run the count-in (3-2-1) before the restarted game begins.
            _countInActive = true;
            _countInStart = (float)Stopwatch.GetTimestamp() / (float)Stopwatch.Frequency;
            this.Invalidate();
        }

        private void ExitGame()
        {
            SaveRunSummaryIfNeeded();
            _gameTimer.Stop();
            _gameRunning = false;
            _playbackCts?.Cancel();
            StopAudioSafely();
            RestoreAllWindows();
            OpenDebugLogInEditor();
            this.Close();
        }

        private bool IsRunComplete()
        {
            int totalObjects = _isTaikoMode ? _taikoBeats.Count : _hitObjects.Count;
            return totalObjects > 0 && _currentHitIndex >= totalObjects;
        }

        private double GetTrackPlayedPercent()
        {
            return _totalDuration > 0f
                ? Math.Clamp(_gameClock.Elapsed.TotalSeconds / _totalDuration * 100d, 0d, 100d)
                : 0d;
        }

        private double GetObjectsResolvedPercent()
        {
            int totalObjects = _isTaikoMode ? _taikoBeats.Count : _hitObjects.Count;
            return totalObjects > 0
                ? Math.Clamp((double)_currentHitIndex / totalObjects * 100d, 0d, 100d)
                : 0d;
        }

        private void SaveRunSummaryIfNeeded()
        {
            if (!_runHasStarted || _runSummarySaved)
            {
                return;
            }

            BeatClickerRunHistory.Save(new BeatClickerRunSummary
            {
                StartedUtc = _runStartedUtc,
                TrackName = _audio.Name,
                Mode = _isTaikoMode ? "Taiko" : "Classic",
                Difficulty = BeatClickerDifficulty.Levels[_difficultyIndex],
                Score = _scorePoints,
                Hits = _score,
                Misses = _missed,
                BestStreak = _bestComboStreak,
                FinalStreak = _comboStreak,
                MeanTimingMs = GetMeanTimingMs(),
                TimingSampleCount = _timingSampleCount,
                TrackPlayedPercent = GetTrackPlayedPercent(),
                ObjectsResolvedPercent = GetObjectsResolvedPercent(),
                DurationSeconds = _gameClock.Elapsed.TotalSeconds,
                Completed = IsRunComplete()
            });
            _runSummarySaved = true;
        }

        /// <summary>
        /// If a verbose debug log was created for this game, opens the .TXT file in the
        /// default system editor so the user can inspect it immediately after finishing or
        /// exiting. Best-effort: any failure is silently ignored.
        /// </summary>
        private void OpenDebugLogInEditor()
        {
            if (_debugLog == null)
            {
                return;
            }
            try
            {
                string path = _debugLog.FilePath;
                WriteDebugLogSummary();
                _debugLog.Dispose();
                _debugLog = null;
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BeatClickerGame open debug log error: {ex.Message}");
            }
        }

        private void WriteDebugLogSummary()
        {
            _debugLog?.LogSummary(
                (float)_gameClock.Elapsed.TotalSeconds,
                _score,
                _missed,
                _bestComboStreak,
                (_score + _missed) > 0 ? (float)_score / (_score + _missed) * 100f : 0f,
                GetMeanTimingMs(),
                _timingSampleCount,
                _scorePoints,
                GetTrackPlayedPercent(),
                IsRunComplete(),
                _comboStreak);
        }

        /// <summary>
        /// Shows the end-of-game results dialog (Classic mode only): hits, misses,
        /// best combo streak, and the evaluated pass rate in % (2 decimal places).
        /// </summary>
        private void ShowResultsDialog()
        {
            SaveRunSummaryIfNeeded();
            if (_isTaikoMode)
            {
                return;
            }

            int hits = _score;
            int misses = _missed;
            int total = hits + misses;
            double passRate = total > 0 ? (double)hits / total * 100.0 : 0.0;

            // Determine a rank label without using the ambiguous "SS" abbreviation.
            string rank;
            Color rankColor;
            if (passRate >= 95.0)
            {
                rank = "S";
                rankColor = Color.FromArgb(255, 215, 0);
            }
            else if (passRate >= 90.0)
            {
                rank = "A";
                rankColor = Color.FromArgb(255, 180, 0);
            }
            else if (passRate >= 80.0)
            {
                rank = "B";
                rankColor = Color.FromArgb(100, 255, 100);
            }
            else if (passRate >= 70.0)
            {
                rank = "C";
                rankColor = Color.FromArgb(100, 200, 255);
            }
            else if (passRate >= 50.0)
            {
                rank = "D";
                rankColor = Color.FromArgb(200, 200, 200);
            }
            else
            {
                rank = "F";
                rankColor = Color.FromArgb(255, 100, 100);
            }

            var dialog = new Form
            {
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(460, 420),
                BackColor = Color.FromArgb(20, 20, 35),
                ForeColor = Color.White,
                Text = "Results",
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                TopMost = true
            };

            // Rank label (large, colored)
            var lblRank = new Label
            {
                Text = rank,
                Font = new Font("Consolas", 48f, FontStyle.Bold),
                ForeColor = rankColor,
                Location = new Point(0, 10),
                Size = new Size(460, 70),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Pass rate
            var lblPassRate = new Label
            {
                Text = $"Pass Rate: {passRate:F2}%",
                Font = new Font("Consolas", 16f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(0, 85),
                Size = new Size(460, 30),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Stats
            var lblStats = new Label
            {
                Text = $"Hits: {hits}    Misses: {misses}\r\nBest Streak: {_bestComboStreak}",
                Font = new Font("Consolas", 13f),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(0, 122),
                Size = new Size(460, 48),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Timing summary
            var lblTiming = new Label
            {
                Text = $"Mean Timing (valid hits): {GetMeanTimingMs():F3} ms\r\nSamples: {_timingSampleCount}",
                Font = new Font("Consolas", 10.5f),
                ForeColor = Color.FromArgb(190, 220, 210),
                Location = new Point(0, 234),
                Size = new Size(460, 42),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Numeric score
            var lblScore = new Label
            {
                Text = $"Score: {FormatScore(_scorePoints)}  |  Final Streak: {_comboStreak}",
                Font = new Font("Consolas", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 230, 120),
                Location = new Point(0, 204),
                Size = new Size(460, 25),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var lblTrackPlayed = new Label
            {
                Text = $"Track Played: {GetTrackPlayedPercent():F1}%",
                Font = new Font("Consolas", 10.5f),
                ForeColor = Color.FromArgb(170, 180, 200),
                Location = new Point(0, 280),
                Size = new Size(460, 25),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Difficulty label
            var lblDifficulty = new Label
            {
                Text = $"Difficulty: {BeatClickerDifficulty.Levels[_difficultyIndex]}",
                Font = new Font("Consolas", 11f),
                ForeColor = Color.FromArgb(150, 150, 170),
                Location = new Point(0, 174),
                Size = new Size(460, 25),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Buttons
            var btnRestart = new Button
            {
                Text = "Restart",
                Location = new Point(55, 330),
                Size = new Size(160, 45),
                BackColor = Color.FromArgb(120, 100, 0),
                ForeColor = Color.White,
                Font = new Font("Consolas", 13f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnRestart.FlatAppearance.BorderSize = 0;
            btnRestart.Click += (_, _) =>
            {
                dialog.Close();
                RestartGame();
            };

            var btnExit = new Button
            {
                Text = "Exit",
                Location = new Point(245, 330),
                Size = new Size(160, 45),
                BackColor = Color.FromArgb(140, 0, 0),
                ForeColor = Color.White,
                Font = new Font("Consolas", 13f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnExit.FlatAppearance.BorderSize = 0;
            btnExit.Click += (_, _) =>
            {
                dialog.Close();
                ExitGame();
            };

            dialog.Controls.Add(lblRank);
            dialog.Controls.Add(lblPassRate);
            dialog.Controls.Add(lblStats);
            dialog.Controls.Add(lblDifficulty);
            dialog.Controls.Add(lblScore);
            dialog.Controls.Add(lblTiming);
            dialog.Controls.Add(lblTrackPlayed);
            dialog.Controls.Add(btnRestart);
            dialog.Controls.Add(btnExit);

            // Stop the game timer and audio before showing the dialog
            _gameTimer.Stop();
            _gameRunning = false;
            _playbackCts?.Cancel();
            StopAudioSafely();
            RestoreAllWindows();

            dialog.ShowDialog(this);
            dialog.Dispose();

            // If the user didn't click Restart or Exit, close the game form
            if (this.Visible)
            {
                this.Close();
            }
        }

        /// <summary>
        /// Stops the audio synchronously and safely so the music never keeps playing after
        /// the game window closes. Runs inline on the UI thread (NAudio's WaveOut is
        /// COM-affine to its creating thread). Stopping is fast (it just halts the output
        /// device), so running it inline does not noticeably delay the UI. This avoids the
        /// 2-second freeze that BeginInvoke+Wait caused (the UI thread can't pump the queued
        /// delegate until Wait times out).
        /// </summary>
        private void StopAudioSafely()
        {
            try { _audio.StopAsync().GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"BeatClickerGame stop audio error: {ex.Message}"); }
        }

        protected override void WndProc(ref Message m)
        {
            // Global ESC hotkey (registered in StartGame) � the reliable trigger. It fires
            // regardless of which window has keyboard focus.
            // Global ESC hotkey (registered in StartGame) � the reliable trigger. It fires
            // regardless of which window has keyboard focus.
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID_ESC)
            {
                HandleEscKeyDown();
                return;
            }

            // Global ESC key-UP hotkey (Shift+Esc): a safe boundary that only marks the key
            // as released. It never toggles the menu, so holding Esc never re-pauses.
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID_ESC_UP)
            {
                HandleEscKeyUp();
                return;
            }

            // Fallback: direct WM_KEYDOWN interception in case the hotkey is unavailable.
            if (m.Msg == WM_KEYDOWN && (int)m.WParam == VK_ESCAPE)
            {
                HandleEscKeyDown();
                return;
            }

            // Fallback: direct WM_KEYUP interception (only fires when the game form has focus).
            if (m.Msg == WM_KEYUP && (int)m.WParam == VK_ESCAPE)
            {
                HandleEscKeyUp();
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveRunSummaryIfNeeded();
            if (_debugLog != null)
            {
                WriteDebugLogSummary();
                _debugLog.Dispose();
                _debugLog = null;
            }
            _gameTimer.Stop();
            _playbackCts?.Cancel();
            StopAudioSafely();
            RestoreAllWindows();

            // Unregister the global ESC hotkeys and remove the layered window style before closing
            if (this.IsHandleCreated)
            {
                UnregisterHotKey(this.Handle, HOTKEY_ID_ESC);
                UnregisterHotKey(this.Handle, HOTKEY_ID_ESC_UP);
                int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_LAYERED);
            }

            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gameTimer.Dispose();
                _playbackCts?.Dispose();
            }
            base.Dispose(disposing);
        }

        // --- Pause menu (borderless) ------------------------------------------

        private sealed class BeatClickerPauseMenuForm : Form
        {
            private readonly BeatClickerGameForm _game;

            public BeatClickerPauseMenuForm(BeatClickerGameForm game)
            {
                _game = game;
                // Borderless (no title bar, no handle buttons). Keyboard focus is made
                // reliable via SetForegroundWindow + Activate/Focus (see below), so the
                // title bar is no longer needed.
                this.FormBorderStyle = FormBorderStyle.None;
                this.StartPosition = FormStartPosition.CenterScreen;
                this.BackColor = Color.FromArgb(20, 20, 35);
                this.ForeColor = Color.White;
                this.ClientSize = new Size(320, 241);
                this.KeyPreview = true;
                this.TopMost = true;
                this.ShowInTaskbar = false;
                this.Opacity = 1.0f; // Pause menu is fully opaque
                this.DoubleBuffered = true;

                var opacityLabel = new Label
                {
                    Text = $"Background opacity: {_game.BackgroundOpacityPercent}%",
                    Location = new Point(60, 4),
                    Size = new Size(200, 24),
                    ForeColor = Color.White,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Consolas", 10f, FontStyle.Bold)
                };

                var opacitySlider = new TrackBar
                {
                    Minimum = BeatClickerSettings.MinBackgroundOpacityPercent,
                    Maximum = BeatClickerSettings.MaxBackgroundOpacityPercent,
                    Value = _game.BackgroundOpacityPercent,
                    Location = new Point(50, 25),
                    Size = new Size(220, 42),
                    TickFrequency = 10,
                    LargeChange = 10,
                    SmallChange = 1,
                    Orientation = Orientation.Horizontal,
                    AutoSize = false
                };
                opacitySlider.ValueChanged += (_, _) =>
                {
                    opacityLabel.Text = $"Background opacity: {opacitySlider.Value}%";
                    _game.SetBackgroundOpacityPercent(opacitySlider.Value);
                };

                var btnContinue = new Button
                {
                    Text = "Continue",
                    Location = new Point(60, 76),
                    Size = new Size(200, 45),
                    BackColor = Color.FromArgb(0, 120, 60),
                    ForeColor = Color.White,
                    Font = new Font("Consolas", 13f, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat
                };
                btnContinue.FlatAppearance.BorderSize = 0;
                btnContinue.Click += (_, _) => _game.ClosePauseMenuAndResume();

                var btnRestart = new Button
                {
                    Text = "Restart",
                    Location = new Point(60, 131),
                    Size = new Size(200, 45),
                    BackColor = Color.FromArgb(120, 100, 0),
                    ForeColor = Color.White,
                    Font = new Font("Consolas", 13f, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat
                };
                btnRestart.FlatAppearance.BorderSize = 0;
                btnRestart.Click += (_, _) => { this.Close(); _game.RestartGame(); };

                var btnExit = new Button
                {
                    Text = "Exit",
                    Location = new Point(60, 186),
                    Size = new Size(200, 45),
                    BackColor = Color.FromArgb(140, 0, 0),
                    ForeColor = Color.White,
                    Font = new Font("Consolas", 13f, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat
                };
                btnExit.FlatAppearance.BorderSize = 0;
                btnExit.Click += (_, _) => { this.Close(); _game.ExitGame(); };

                this.Controls.Add(opacityLabel);
                this.Controls.Add(opacitySlider);
                this.Controls.Add(btnContinue);
                this.Controls.Add(btnRestart);
                this.Controls.Add(btnExit);

                // ESC is handled by the game's GLOBAL hotkey (RegisterHotKey), which fires
                // regardless of which window has keyboard focus. The menu's own KeyDown/KeyUp
                // are intentionally NOT used for Esc: if the menu (which has focus) handled
                // Esc key-down, it would close the menu BEFORE the game's global hotkey ran,
                // causing the game to briefly resume and re-pause (the "flicker" bug).
                // The global hotkey handles both open and close with the debounce logic.

                // The menu's FormClosing does NOT auto-resume the game. The game state is
                // managed by the game form: ClosePauseMenuAndResume() (Esc/Continue) resumes
                // explicitly, and ExitGame()/RestartGame() handle their own audio. Auto-resuming
                // here caused a race with the global hotkey (the menu would close, the game
                // would resume, then the hotkey would re-pause � the "flicker" bug, and on a
                // short press the re-pause never happened so the menu appeared stuck open).
                // Leaving this empty keeps the Esc close reliable and flicker-free.
            }
        }

        // --- Fail animation --------------------------------------------------

        private void AddFailEffect(float x, float y, float time)
        {
            _failEffects.Add(new FailEffect { X = x, Y = y, StartTime = time });
        }

        private void AddHitEffect(float x, float y, float time)
        {
            _hitEffects.Add(new HitEffect { X = x, Y = y, StartTime = time });
        }

        private void AddSliderHitEffect(BeatCatchHitObject obj, float time)
        {
            _hitEffects.Add(new HitEffect
            {
                X = obj.X,
                Y = obj.Y,
                EndX = obj.EndX,
                EndY = obj.EndY,
                IsSlider = true,
                StartTime = time
            });
        }

        private void AddComboPopup(int value, float x, float y, float time)
        {
            _comboPopups.Add(new ComboPopup { Value = value, X = x, Y = y, StartTime = time });
        }

        /// <summary>
        /// Records a successful hit: increments the combo streak and updates the best streak.
        /// Must be called under _lock.
        /// </summary>
        private void RegisterHit()
        {
            _score++;
            _comboStreak++;
            double streakMultiplier = GetScoreMultiplier(_comboStreak);
            _scorePoints += ScoreBasePoints * streakMultiplier;
            if (_comboStreak > _bestComboStreak)
            {
                _bestComboStreak = _comboStreak;
            }
        }

        /// <summary>
        /// Records a miss: resets the combo streak to zero.
        /// Must be called under _lock.
        /// </summary>
        private void RegisterMiss()
        {
            _missed++;
            _comboStreak = 0;
            _hasTimingFeedback = true;
            _lastTimingWasMiss = true;
            _lastTimingColor = Color.FromArgb(255, 220, 40, 40);
        }

        private static double GetScoreMultiplier(int streak)
        {
            return streak > 0 ? Math.Pow(ScoreStreakGrowth, streak - 1) : 1d;
        }

        private double GetCurrentScoreMultiplier()
        {
            return GetScoreMultiplier(_comboStreak);
        }

        private static string FormatScore(double score)
        {
            if (!double.IsFinite(score))
            {
                return "MAX";
            }

            return Math.Abs(score) < 1e18
                ? score.ToString("N0")
                : score.ToString("0.###E+0");
        }

        private void RecordHitTiming(float timeUntilHitSeconds, BeatCatchHitType type)
        {
            float offsetMs = timeUntilHitSeconds * 1000f;
            float earlyWindowMs = type == BeatCatchHitType.Slider
                ? _hitWindowEarlyMs + 100f
                : _hitWindowEarlyMs;
            _timingOffsetSumMs += offsetMs;
            _timingSampleCount++;
            _lastTimingOffsetMs = offsetMs;
            _lastTimingColor = GetTimingColor(offsetMs, earlyWindowMs, _hitWindowLateMs);
            _hasTimingFeedback = true;
            _lastTimingWasMiss = false;
        }

        private static Color GetTimingColor(float offsetMs, float earlyWindowMs, float lateWindowMs)
        {
            float windowMs = offsetMs >= 0f ? earlyWindowMs : lateWindowMs;
            float closeness = 1f - Math.Clamp(Math.Abs(offsetMs) / Math.Max(1f, windowMs), 0f, 1f);
            if (closeness < 1f / 3f)
            {
                return BlendTimingColors(Color.FromArgb(255, 230, 30, 30), Color.FromArgb(255, 255, 210, 30), closeness * 3f);
            }

            if (closeness < 2f / 3f)
            {
                return BlendTimingColors(Color.FromArgb(255, 255, 210, 30), Color.FromArgb(255, 40, 210, 80), (closeness - 1f / 3f) * 3f);
            }

            return BlendTimingColors(Color.FromArgb(255, 40, 210, 80), Color.White, (closeness - 2f / 3f) * 3f);
        }

        private static Color BlendTimingColors(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(
                255,
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        private double GetMeanTimingMs()
        {
            return _timingSampleCount > 0 ? _timingOffsetSumMs / _timingSampleCount : 0d;
        }

        private void PaintHitEffects(Graphics g, float currentTime)
        {
            for (int i = _hitEffects.Count - 1; i >= 0; i--)
            {
                var fx = _hitEffects[i];
                float age = currentTime - fx.StartTime;
                if (age > HitEffectDuration)
                {
                    _hitEffects.RemoveAt(i);
                    continue;
                }

                float progress = Math.Clamp(age / HitEffectDuration, 0f, 1f);
                int alpha = Math.Clamp((int)(255f * (1f - progress)), 0, 255);
                if (alpha <= 0)
                {
                    continue;
                }

                using var pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 6f - progress * 2f);
                if (fx.IsSlider)
                {
                    g.DrawLine(pen, fx.X, fx.Y, fx.EndX, fx.EndY);
                    float ringRadius = 12f + progress * 18f;
                    g.DrawEllipse(pen, fx.EndX - ringRadius, fx.EndY - ringRadius, ringRadius * 2f, ringRadius * 2f);
                }
                else
                {
                    float ringRadius = 12f + progress * 22f;
                    g.DrawEllipse(pen, fx.X - ringRadius, fx.Y - ringRadius, ringRadius * 2f, ringRadius * 2f);
                }
            }
        }

        private void PaintTimingFeedback(Graphics g, int width)
        {
            if (!_hasTimingFeedback)
            {
                return;
            }

            float centerX = width / 2f;
            float centerY = 58f;
            if (_lastTimingWasMiss)
            {
                using var borderPen = new Pen(Color.White, 8f);
                using var xPen = new Pen(_lastTimingColor, 4f);
                borderPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                borderPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                xPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                xPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(borderPen, centerX - 13f, centerY - 13f, centerX + 13f, centerY + 13f);
                g.DrawLine(borderPen, centerX + 13f, centerY - 13f, centerX - 13f, centerY + 13f);
                g.DrawLine(xPen, centerX - 13f, centerY - 13f, centerX + 13f, centerY + 13f);
                g.DrawLine(xPen, centerX + 13f, centerY - 13f, centerX - 13f, centerY + 13f);
                return;
            }

            string timingText = $"{(_lastTimingOffsetMs >= 0f ? "+" : string.Empty)}{_lastTimingOffsetMs:F1} ms";
            using var font = new Font("Consolas", 18f, FontStyle.Bold);
            using var brush = new SolidBrush(_lastTimingColor);
            var textSize = g.MeasureString(timingText, font);
            g.DrawString(timingText, font, brush, centerX - textSize.Width / 2f, centerY - textSize.Height / 2f);
        }

        private void PaintComboPopups(Graphics g, float currentTime)
        {
            for (int i = _comboPopups.Count - 1; i >= 0; i--)
            {
                var cp = _comboPopups[i];
                float age = currentTime - cp.StartTime;
                if (age > ComboPopupDuration)
                {
                    _comboPopups.RemoveAt(i);
                    continue;
                }

                // Fade out over the popup lifetime
                float alpha = (float)(255 * (1f - age / ComboPopupDuration));
                int a = Math.Clamp((int)alpha, 0, 255);
                if (a <= 0) continue;

                // Slight upward drift for a "pop" feel
                float y = cp.Y - 40f - age * 30f;
                using var font = new Font("Consolas", 26f, FontStyle.Bold);
                using var brush = new SolidBrush(Color.FromArgb(a, 80, 255, 80));
                var size = g.MeasureString($"x{cp.Value}", font);
                g.DrawString($"x{cp.Value}", font, brush, cp.X - size.Width / 2f, y);
            }
        }

        private void PaintFailEffects(Graphics g, float currentTime)
        {
            for (int i = _failEffects.Count - 1; i >= 0; i--)
            {
                var fx = _failEffects[i];
                float age = currentTime - fx.StartTime;
                if (age > FailEffectDuration)
                {
                    _failEffects.RemoveAt(i);
                    continue;
                }

                float alpha = (float)(255 * (1f - age / FailEffectDuration));
                int a = Math.Clamp((int)alpha, 0, 255);
                if (a <= 0) continue;

                // Draw a red X with a white border at the fail position
                int size = 30;
                using var borderPen = new Pen(Color.FromArgb(a, 255, 255, 255), 8);
                using var pen = new Pen(Color.FromArgb(a, 255, 50, 50), 4);
                borderPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                borderPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                g.DrawLine(borderPen, fx.X - size, fx.Y - size, fx.X + size, fx.Y + size);
                g.DrawLine(borderPen, fx.X + size, fx.Y - size, fx.X - size, fx.Y + size);
                g.DrawLine(pen, fx.X - size, fx.Y - size, fx.X + size, fx.Y + size);
                g.DrawLine(pen, fx.X + size, fx.Y - size, fx.X - size, fx.Y + size);
            }
        }

        // --- Taskbar height --------------------------------------------------

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_CYCAPTION = 4;
        private const int SM_CYSIZEFRAME = 9;
        private const int SM_CYSIZEFRAME_EXTRA = 33;

        private int GetTaskbarHeight()
        {
            // Get the work area (screen minus taskbar) and compare to full screen bounds
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
            // The taskbar height is the difference between the full screen height and the work area height
            int taskbarHeight = screenBounds.Height - workArea.Height;
            return Math.Max(0, taskbarHeight);
        }

        // --- Data types -------------------------------------------------------

        private enum BeatCatchHitType
        {
            Circle,
            Slider,
            Spinner
        }

        private struct BeatCatchHitObject
        {
            public BeatCatchHitType Type;
            public float Time;
            public float X;
            public float Y;
            public float EndX;
            public float EndY;
            public float Duration;
            public int Group;   // group number (1-based); numbering restarts per group
            public int Number; // position within the group (1-based)
            public float[]? PathX;
            public float[]? PathY;
            public float[]? PathCumulativeLength;
            public float PathTotalLength;
            public int PatternIndex;
        }

        private enum TaikoBeatType
        {
            Don,
            Ka
        }

        private struct TaikoBeat
        {
            public float Time;
            public TaikoBeatType Type;
        }
    }
}
