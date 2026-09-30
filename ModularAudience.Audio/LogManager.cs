using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using ModularAudience.Shared;

namespace ModularAudience.Audio
{
    public static class LogManager
    {
        private static RollingFileMemoryLogger? _logger;
        private static readonly object _initLock = new();
        private static SynchronizationContext? _uiContext;
        private static int _uiThreadId;
        private static bool _hooked;

        // Stable, observable collection bound by the UI (ListBox DataSource).
        // Kept in sync with the logger's in-memory ring buffer.
        public static readonly BindingList<string> Logs = [];

        public static RollingFileMemoryLogger Logger
        {
            get
            {
                lock (_initLock)
                {
                    if (_logger == null)
                    {
                        var options = new RollingFileMemoryLoggerOptions
                        {
                            LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs"),
                            CreateLogFile = true,
                            MaxLogEntries = 65536,
                            LogTimestampFormat = "HH:mm:ss.fff",
                            LogFileBaseName = "ModularAudience_Log",
                            EchoToConsole = true,
                            Silent = false
                        };
                        _logger = new RollingFileMemoryLogger(options);
                    }

                    EnsureHooked(_logger);
                    return _logger;
                }
            }
        }

        /// <summary>Subscribes to the active logger exactly once, so UI bindings stay live.</summary>
        private static void EnsureHooked(RollingFileMemoryLogger logger)
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
            logger.LogWritten += (ts, line) =>
            {
                void Apply()
                {
                    Logs.Add(line);
                    int max = MaxLogCount;
                    while (Logs.Count > max)
                    {
                        Logs.RemoveAt(0);
                    }
                }

                try
                {
                    if (_uiContext is { } ctx)
                    {
                        ctx.Post(_ => Apply(), null);
                    }
                    else
                    {
                        Apply();
                    }
                }
                catch { }

                try { NewLogPostedWithTimestamp?.Invoke(ts, line); } catch { }
                try { NewLogPosted?.Invoke(line); } catch { }
            };
        }

        // Compat properties
        public static int MaxLogCount
        {
            get => Logger.Settings.MaxLogEntries ?? 512;
            set => Logger.Settings.MaxLogEntries = value;
        }

        /// <summary>When true, the log list view keeps itself pinned to the newest entry.</summary>
        public static bool AutoScroll { get; set; } = true;

        public static string TimeFormat
        {
            get => Logger.Settings.LogTimestampFormat;
            set => Logger.Settings.LogTimestampFormat = value;
        }

        public static void Log(string message)
        {
            Logger.Log(message);
        }

        public static void Log(Exception ex)
        {
            Logger.Log(ex);
        }

        public static void Log(string message, Exception ex)
        {
            Logger.Log(message, ex);
        }

        public static void LogError(string message) => Logger.LogError(message);
        public static void LogWarning(string message) => Logger.LogWarning(message);
        public static void LogInfo(string message) => Logger.LogInfo(message);
        public static void LogSuccess(string message) => Logger.LogSuccess(message);

        public static Task LogAsync(string message) => Logger.LogAsync(message);
        public static Task LogAsync(Exception ex) => Logger.LogAsync(ex);

        public static void AddComment(DateTime? capturedAt = null, TimeSpan? elapsedSince = null, string comment = "<!!!>")
        {
            Logger.AddComment(capturedAt, elapsedSince, comment);
        }

        // Overload matching old LogCollection.PostComment signature
        public static void PostComment(DateTime timestamp, string comment)
        {
            Logger.AddComment(timestamp, TimeSpan.Zero, comment);
        }

        public static IReadOnlyList<string> GetLogLines(bool? returnFilteredLog = false, bool reverseOrder = false)
        {
            return Logger.GetLogLines(returnFilteredLog, reverseOrder);
        }

        public static string[] GetAllLogFilePaths() => Logger.GetAllLogFilePaths();

        public static void ConfigureSaveToRepository(bool enabled, int maxFiles = 8)
        {
            Logger.ConfigureSaveToRepository(enabled, maxPreviousLogFiles: maxFiles);
        }

        public static string SaveToRepository() => Logger.SaveToRepository();

        public static void Initialize(SynchronizationContext? uiContext = null)
        {
            var logger = Logger;
            if (uiContext != null)
            {
                _uiContext = uiContext;
                _uiThreadId = System.Environment.CurrentManagedThreadId;
                logger.SetUiContext(uiContext);
            }
            SyncFromLogger();
        }

        /// <summary>Installs the application-configured logger before any UI is created.</summary>
        public static void Configure(RollingFileMemoryLogger logger)
        {
            lock (_initLock)
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
                _hooked = false;
                EnsureHooked(_logger);
            }
        }

        /// <summary>Re-reads the logger's in-memory ring buffer into the observable <see cref="Logs"/> list.</summary>
        public static void SyncFromLogger()
        {
            var lines = Logger.GetLogLines();
            void Apply()
            {
                Logs.RaiseListChangedEvents = false;
                try
                {
                    Logs.Clear();
                    foreach (var line in lines)
                    {
                        Logs.Add(line);
                    }
                }
                finally
                {
                    Logs.RaiseListChangedEvents = true;
                    Logs.ResetBindings();
                }
            }

            if (_uiContext is { } ctx && System.Environment.CurrentManagedThreadId != _uiThreadId)
            {
                ctx.Post(_ => Apply(), null);
            }
            else
            {
                Apply();
            }
        }

        /// <summary>Clears both the logger's in-memory buffer and the observable list.</summary>
        public static void ClearLogs()
        {
            Logger.ClearLogs();
            void Apply()
            {
                Logs.Clear();
            }

            if (_uiContext is { } ctx)
            {
                ctx.Post(_ => Apply(), null);
            }
            else
            {
                Apply();
            }
        }

        // Back-compat events for existing subscribers (WindowMain etc.)
        public static event Action<string>? NewLogPosted;
        public static event Action<DateTime, string>? NewLogPostedWithTimestamp;
    }
}