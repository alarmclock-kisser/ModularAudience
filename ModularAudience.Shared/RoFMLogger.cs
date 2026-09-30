// ============================================================================
// RollingFileMemoryLogger – aggregierte Single-File-Version
// ============================================================================
// Diese Datei enthält alle Typen, die für den Einsatz von RollingFileMemoryLogger
// (inkl. IRollingFileMemoryLogger, RollingFileMemoryLoggerOptions, ExceptionPrintOptions
// und UniversalHelper) benötigt werden. Sie kann direkt in ein anderes Projekt
// kopiert und dort ohne weitere Abhängigkeiten kompiliert werden.
//
// Namespace: AsynCUDA13.Shared
// ============================================================================

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;

namespace ModularAudience.Shared
{
    // ========================================================================
    // ExceptionPrintOptions
    // ========================================================================

    /// <summary>
    /// Represents configuration options for printing exceptions, including settings for inner exception depth, stack trace inclusion, and formatting.
    /// </summary>
    public class ExceptionPrintOptions
    {
        /// <summary>
        /// Gets or sets the maximum depth of inner exceptions to include when logging exceptions.
        /// </summary>
        public int? InnerExceptionMaxDepth { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to append the stack trace of inner exceptions when logging exceptions.
        /// </summary>
        public bool InnerExceptionAppendStackTrace { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to format inner exceptions as a single line when logging exceptions.
        /// </summary>
        public bool InnerExceptionAsSingleLine { get; set; }

        /// <summary>
        /// The opening bracket used when formatting inner exception messages in the log.
        /// </summary>
        public string InnerExceptionOpeningBracket { get; set; } = "(";

        /// <summary>
        /// The closing bracket used when formatting inner exception messages in the log.
        /// </summary>
        public string InnerExceptionClosingBracket { get; set; } = ")";

        /// <summary>
        /// The separator used when formatting inner exception messages in the log.
        /// </summary>
        public string InnerExceptionSeparator { get; set; } = " ";
    }

    // ========================================================================
    // RollingFileMemoryLoggerOptions
    // ========================================================================

    /// <summary>
    /// Konfigurationsoptionen für RollingFileMemoryLogger.
    /// </summary>
    public class RollingFileMemoryLoggerOptions
    {
        /// <summary>
        /// The maximum number of log entries to retain. Unlimited when null.
        /// </summary>
        public int? MaxLogEntries { get; set; } = 65536;

        /// <summary>
        /// Gets or sets a value indicating whether to use a ring buffer.
        /// </summary>
        public bool? UseRingBuffer { get; set; } = null;

        /// <summary>
        /// The directory where log files are stored.
        /// </summary>
        public string LogDirectory { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        /// <summary>
        /// Whether to create a new log file upon initialization.
        /// </summary>
        public bool CreateLogFile { get; set; } = false;

        /// <summary>
        /// The maximum number of previous log files to retain.
        /// </summary>
        public int MaxLogFiles { get; set; } = 32;

        /// <summary>
        /// The maximum number of saved log files to retain in the repository log directory.
        /// </summary>
        public int MaxRepositoryLogFiles { get; set; } = 8;

        /// <summary>
        /// Gets or sets a value indicating whether the logger should operate in silent mode.
        /// </summary>
        public bool Silent { get; set; } = false;

        /// <summary>
        /// Gets or sets the format string used for timestamps in log entries.
        /// </summary>
        public string? LogTimestampFormat { get; set; } = "HH:mm:ss.fff";

        /// <summary>
        /// Gets or sets the format string used for timestamps in log file names.
        /// </summary>
        public string FileTimestampFormat { get; set; } = "yyyy-MM-dd_HH-mm-ss";

        /// <summary>
        /// Gets or sets the base name used for log files.
        /// </summary>
        public string LogFileBaseName { get; set; } = "dotnet10-Application_Log";

        /// <summary>
        /// Gets or sets the file extension used for log files.
        /// </summary>
        public string LogFileExtension { get; set; } = ".txt";

        /// <summary>
        /// The phrase used to filter log entries into separate BindingList.
        /// </summary>
        public string? FilterPhrase { get; set; } = null;

        /// <summary>
        /// Gets or sets a value indicating whether log lines are echoed to the console.
        /// </summary>
        public bool? EchoToConsole { get; set; } = null;

        /// <summary>
        /// Gets or sets the key phrases that determine which log lines are echoed to the console.
        /// </summary>
        public string[] EchoToConsoleKeyPhrases { get; set; } = ["[SUCCESS]", "[ERROR]", "[WARN", "Exception:"];

        /// <summary>
        /// Whether saving to the repository is configured and enabled.
        /// </summary>
        public bool SaveToRepository { get; set; } = false;

        /// <summary>
        /// Custom file path or directory for saving logs to the repository.
        /// </summary>
        public string? SaveToRepositoryCustomFilePath { get; set; } = null;

        /// <summary>
        /// Gets or sets the exception print settings.
        /// </summary>
        public ExceptionPrintOptions ExceptionPrintSettings { get; set; } = new ExceptionPrintOptions();
    }

    // ========================================================================
    // IRollingFileMemoryLogger
    // ========================================================================

    /// <summary>
    /// A logger class that provides thread-safe logging functionality for applications.
    /// </summary>
    public interface IRollingFileMemoryLogger
    {
        /// <summary>
        /// Settings for this logger instance.
        /// </summary>
        public RollingFileMemoryLoggerOptions Settings { get; }

        /// <summary>
        /// Gets the full path of the current log file.
        /// </summary>
        public string? LogFilePath { get; }

        /// <summary>
        /// Action to perform on shutdown to save logs to the repository.
        /// </summary>
        public Action? SaveToRepositoryOnShutdown { get; }

        /// <summary>
        /// Raised whenever a new line has been recorded.
        /// </summary>
        public event Action<DateTime, string>? LogWritten;

        /// <summary>
        /// Records a user/debugging comment anchored to the timestamp captured when the user initiated it.
        /// </summary>
        public void AddComment(DateTime? capturedAt = null, TimeSpan? elapsedSince = null, string comment = "<!!!>");

        /// <summary>
        /// Asynchronously records a user/debugging comment.
        /// </summary>
        public Task AddCommentAsync(DateTime? capturedAt = null, TimeSpan? elapsedSince = null, string comment = "<!!!>", bool? configureAwait = null);

        /// <summary>
        /// Clears all recorded log entries from the internal dictionary and the binding lists.
        /// </summary>
        public void ClearLogs();

        /// <summary>
        /// Configures the logger to save all recorded log lines to a timestamped TXT file.
        /// </summary>
        public string ConfigureSaveToRepository(bool? configureToggle = null, string? subDirOrDifferentPath = null, int maxPreviousLogFiles = 8, Action? onShutdown = null);

        /// <summary>
        /// Returns the full paths of all log files in the log directory.
        /// </summary>
        public string[] GetAllLogFilePaths();

        /// <summary>
        /// Returns a string representation of all inner exceptions of the provided exception.
        /// </summary>
        public string GetInnerExceptionsRecursively(Exception ex);

        /// <summary>
        /// Returns a string representation of all inner exceptions with custom formatting.
        /// </summary>
        public string GetInnerExceptionsRecursively(Exception ex, int? maxDepth = null, bool appendStackTrace = true, string openingBracket = "(", string closingBracket = ")", string separator = " ", bool asSingleLine = false);

        /// <summary>
        /// Returns a read-only list of log lines.
        /// </summary>
        public IReadOnlyList<string> GetLogLines(bool? returnFilteredLog = false, bool reverseOrder = false);

        /// <summary>
        /// Returns an array of namespace strings for the specified project name.
        /// </summary>
        public string[]? GetNamespacesForProject(string? projectName = null, bool includeSubNamespaces = true, bool ignoreCase = true);

        /// <summary>
        /// Returns the full path of a previous log file based on the specified index.
        /// </summary>
        public string? GetPreviousLogFilePath(int backIndex = 0);

        /// <summary>
        /// Gets the UI synchronization context for updating the BindingList.
        /// </summary>
        public SynchronizationContext? GetUiContext(bool copy = false);

        /// <summary>
        /// Initializes the log files.
        /// </summary>
        public void InitializeLogger(RollingFileMemoryLoggerOptions? options = null, Action? onShutdown = null, CancellationToken? exitCancellationToken = null, SynchronizationContext? synchronizationContext = null);

        /// <summary>
        /// Logs an exception with an optional pre-text message.
        /// </summary>
        public void Log(Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true, string? preText = null);

        /// <summary>
        /// Logs a message with a timestamp.
        /// </summary>
        public void Log(string message);

        /// <summary>
        /// Logs a contextual message together with an exception.
        /// </summary>
        public void Log(string message, Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true);

        /// <summary>
        /// Logs an exception asynchronously.
        /// </summary>
        public Task LogAsync(Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true, string? preText = null, bool? configureAwait = null);

        /// <summary>
        /// Logs a message asynchronously.
        /// </summary>
        public Task LogAsync(string message, bool? configureAwait = null);

        /// <summary>
        /// Logs an error message.
        /// </summary>
        public void LogError(string message);

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        public void LogInfo(string message);

        /// <summary>
        /// Logs a success message.
        /// </summary>
        public void LogSuccess(string message);

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public void LogWarning(string message);

        /// <summary>
        /// Resolves the full path of the repository or project directory.
        /// </summary>
        public string ResolveRepositoryDirectory(string? projectName = null, string subPath = "Logs", bool ensureDirectoryExists = false);

        /// <summary>
        /// Resolves the full path of a log file in the repository's log directory.
        /// </summary>
        public string ResolveRepositoryLogFilePath(string? differentFilePathOrDirectory = null);

        /// <summary>
        /// Saves the current log entries to a timestamped TXT file.
        /// </summary>
        public string SaveToRepository(string? differentFilePathOrDirectory = null, bool? returnFilteredLog = null, bool reverseOrder = false, bool forceSave = false);

        /// <summary>
        /// Sets the action to perform on shutdown to save logs to the repository.
        /// </summary>
        public void SetOnShutdownAction(Action? onShutdown, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the UI synchronization context for updating the BindingList.
        /// </summary>
        public void SetUiContext(SynchronizationContext? context);

        /// <summary>
        /// Starts the background writer task.
        /// </summary>
        public void StartBackgroundWriter(CancellationToken cancellationToken);
    }

    // ========================================================================
    // RollingFileMemoryLogger
    // ========================================================================

    public partial class RollingFileMemoryLogger : IRollingFileMemoryLogger
    {
        public RollingFileMemoryLoggerOptions Settings { get; private set; } = new();

        public RollingFileMemoryLogger(RollingFileMemoryLoggerOptions? settings = null, Action? onShutdown = null, CancellationToken? exitCancellationToken = null, SynchronizationContext? synchronizationContext = null)
        {
            this.Settings = settings ?? new RollingFileMemoryLoggerOptions();

            this._logChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(this.Settings.MaxLogEntries ?? 16384)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

            this.InitializeLogger(this.Settings, onShutdown, exitCancellationToken, synchronizationContext);
        }

        public readonly ConcurrentDictionary<DateTime, string> LogEntries = [];

        private readonly Channel<string> _logChannel;
        private Task? _logWriterTask;
        private CancellationTokenSource? _logCts;

        public void StartBackgroundWriter(CancellationToken cancellationToken)
        {
            if (this._logWriterTask != null) return;
            this._logCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            this._logCts.Token.Register(() => this._logChannel.Writer.TryComplete());

            this._logWriterTask = Task.Run(async () =>
            {
                StreamWriter? streamWriter = null;
                string? currentFilePath = null;

                try
                {
                    await foreach (var line in this._logChannel.Reader.ReadAllAsync(this._logCts.Token))
                    {
                        if (this.LogFilePath != null)
                        {
                            if (streamWriter == null || currentFilePath != this.LogFilePath)
                            {
                                streamWriter?.Dispose();
                                currentFilePath = this.LogFilePath;
                                streamWriter = new StreamWriter(new FileStream(currentFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, true), Encoding.UTF8);
                            }

                            await streamWriter.WriteLineAsync(line.AsMemory(), this._logCts.Token);
                            await streamWriter.FlushAsync();
                        }
                    }
                }
                catch (OperationCanceledException) when (this._logCts.Token.IsCancellationRequested)
                {
                    // Clean exit on cancellation
                }
                finally
                {
                    streamWriter?.Dispose();
                }
            }, this._logCts.Token);
        }

        private int _logEntriesRingBufferCounter = 0;

        public readonly BindingList<string> LogEntriesBindingList = [];
        public readonly BindingList<string> FilteredLogEntriesBindingList = [];

        public string? LogFilePath { get; private set; } = null;

        public event Action<DateTime, string>? LogWritten;

        private SynchronizationContext? UiContext;

        public Action? SaveToRepositoryOnShutdown { get; private set; } = null;

        public SynchronizationContext? GetUiContext(bool copy = false) => copy ? this.UiContext?.CreateCopy() : this.UiContext;

        public void SetUiContext(SynchronizationContext? context)
        {
            context ??= SynchronizationContext.Current;

            // Idempotent: setting the same context again must not log a duplicate line.
            if (ReferenceEquals(this.UiContext, context))
            {
                return;
            }

            this.UiContext = context;
            string projectName = this.GetType().Namespace?.Split('.').FirstOrDefault() ?? "---";
            this.Log($"[Logger] RollingFileMemoryLogger UI context set for project <{projectName}>");
        }

        public void SetOnShutdownAction(Action? onShutdown, CancellationToken cancellationToken = default)
        {
            this.SaveToRepositoryOnShutdown = onShutdown;

            if (cancellationToken != default)
            {
                cancellationToken.Register(() => this.SaveToRepositoryOnShutdown?.Invoke());
            }
        }

        public void InitializeLogger(RollingFileMemoryLoggerOptions? options = null, Action? onShutdown = null, CancellationToken? exitCancellationToken = null, SynchronizationContext? synchronizationContext = null)
        {
            options ??= new();
            string projectName = this.GetType().Namespace?.Split('.').FirstOrDefault() ?? "---";

            // Adopt the supplied options immediately so everything below reads the real configuration
            // instead of the defaults that were assigned in the constructor.
            this.Settings = options;

            string originalTimestampFormat = options.LogTimestampFormat;
            options.LogTimestampFormat = options.LogTimestampFormat.VerifyFormatString(out string? timestampFormatError);
            if (!string.IsNullOrEmpty(timestampFormatError))
            {
                this.LogWarning($"Invalid TimestampFormat '{originalTimestampFormat}'. Reverting to default format 'HH:mm:ss.fff'. Error: {timestampFormatError}");
            }

            // Default to "<app base>/Logs". Must be a directory path, not the assembly file path.
            string baseDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrEmpty(options.LogDirectory))
            {
                options.LogDirectory = Path.Combine(baseDirectory, "Logs");
            }
            options.LogDirectory = Path.GetFullPath(options.LogDirectory);

            onShutdown ??= () => { this.SaveToRepository(); };
            this.SaveToRepositoryOnShutdown = onShutdown;

            if (exitCancellationToken.HasValue && exitCancellationToken.Value != default)
            {
                exitCancellationToken.Value.Register(() => this.SaveToRepositoryOnShutdown?.Invoke());
            }

            if (synchronizationContext != null)
            {
                this.SetUiContext(synchronizationContext);
            }

            try
            {
                if (!Directory.Exists(this.Settings.LogDirectory))
                {
                    Directory.CreateDirectory(this.Settings.LogDirectory);
                }

                if (options.MaxLogFiles == 0)
                {
                    Directory.Delete(this.Settings.LogDirectory, true);
                    Directory.CreateDirectory(this.Settings.LogDirectory);
                }
                else if (options.MaxLogFiles >= 1)
                {
                    var existingLogs = Directory.GetFiles(this.Settings.LogDirectory, "log_*.txt")
                        .Select(path => new FileInfo(path))
                        .OrderByDescending(fi => fi.CreationTime)
                        .ToList();
                    foreach (var oldLog in existingLogs.Skip(options.MaxLogFiles))
                    {
                        try
                        {
                            oldLog.Delete();
                        }
                        catch (Exception ex)
                        {
                            this.LogError($"Error deleting old log file '{oldLog.FullName}': {ex.Message}");
                        }
                    }
                }

                if (options.CreateLogFile)
                {
                    this.LogFilePath = Path.Combine(this.Settings.LogDirectory, $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.Create(this.LogFilePath).Dispose();
                    this.Log($"Log file created at {this.LogFilePath}");
                }
            }
            catch (Exception ex)
            {
                this.Log($"Error with log files initialization: {ex.Message}");
            }
            finally
            {
                this.Log($"[Logger] RollingFileMemoryLogger initialized for project <{projectName}>");
            }
        }

        public void Log(string message)
        {
            DateTime timestamp = DateTime.Now;
            string logEntry = string.IsNullOrEmpty(this.Settings.LogTimestampFormat) ? message : $"[{timestamp.ToString(this.Settings.LogTimestampFormat)}] {message}";

            this.EnsureMaxLogEntriesWithOffloadAndBuffering();

            this.LogEntries[timestamp] = logEntry;

            if (string.IsNullOrEmpty(this.Settings.FilterPhrase) || !logEntry.Contains(this.Settings.FilterPhrase, StringComparison.OrdinalIgnoreCase))
            {
                if (this.UiContext != null)
                {
                    this.UiContext.Post(_ => this.LogEntriesBindingList.Add(logEntry), null);
                }
                else
                {
                    lock (this.LogEntriesBindingList)
                    {
                        this.LogEntriesBindingList.Add(logEntry);
                    }
                }
            }
            else
            {
                if (this.UiContext != null)
                {
                    this.UiContext.Post(_ => this.FilteredLogEntriesBindingList.Add(logEntry), null);
                }
                else
                {
                    lock (this.FilteredLogEntriesBindingList)
                    {
                        this.FilteredLogEntriesBindingList.Add(logEntry);
                    }
                }
            }

            this.RaiseLogWritten(timestamp, logEntry);

            if (this.Settings.Silent)
            {
                return;
            }

            if (this.Settings.EchoToConsole == true || this.ShouldEchoToConsole(logEntry))
            {
                Console.WriteLine(logEntry);
            }

            if (this.LogFilePath != null)
            {
                try
                {
                    this._logChannel.Writer.TryWrite(logEntry);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing to log file: {ex.Message}");
                }
            }
        }

        public void Log(Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true, string? preText = null)
        {
            this.Log($"{(string.IsNullOrEmpty(preText) ? "" : preText + "\n")}Exception: {this.GetInnerExceptionsRecursively(ex, maxInnerEx)}{(appendStackTrace ? "\nStack Trace: " + ex.StackTrace : "")}");
        }

        public void Log(string message, Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true)
        {
            this.Log($"{message} Exception: {this.GetInnerExceptionsRecursively(ex, maxInnerEx)}{(appendStackTrace ? "\nStack Trace: " + ex.StackTrace : "")}");
        }

        public void LogInfo(string message) => this.Log($"[INFO] {message}");
        public void LogSuccess(string message) => this.Log($"[SUCCESS] {message}");
        public void LogWarning(string message) => this.Log($"[WARN] {message}");
        public void LogError(string message) => this.Log($"[ERROR] {message}");

        public async Task LogAsync(string message, bool? configureAwait = null)
        {
            if (configureAwait.HasValue)
            {
                await Task.Run(() => this.Log(message)).ConfigureAwait(configureAwait.Value);
            }
            else
            {
                await Task.Run(() => this.Log(message));
            }
        }

        public async Task LogAsync(Exception ex, int? maxInnerEx = 0, bool appendStackTrace = true, string? preText = null, bool? configureAwait = null)
        {
            if (configureAwait.HasValue)
            {
                await Task.Run(() => this.Log(ex, maxInnerEx, appendStackTrace, preText)).ConfigureAwait(configureAwait.Value);
            }
            else
            {
                await Task.Run(() => this.Log(ex, maxInnerEx, appendStackTrace, preText));
            }
        }

        public void AddComment(DateTime? capturedAt = null, TimeSpan? elapsedSince = null, string comment = "<!!!>")
        {
            if (string.IsNullOrWhiteSpace(comment))
            {
                return;
            }

            elapsedSince ??= TimeSpan.Zero;
            capturedAt ??= DateTime.Now.Subtract(elapsedSince.Value);

            string logEntry = string.IsNullOrEmpty(this.Settings.LogTimestampFormat)
                ? $"[COMMENT] {comment}"
                : $"[{capturedAt.Value.ToString(this.Settings.LogTimestampFormat.VerifyFormatString(out var _))}] [COMMENT] {comment}";

            this.LogEntries[capturedAt.Value] = logEntry;

            if (this.UiContext != null)
            {
                this.UiContext.Post(_ => this.LogEntriesBindingList.Add(logEntry), null);
            }
            else
            {
                lock (this.LogEntriesBindingList)
                {
                    this.LogEntriesBindingList.Add(logEntry);
                }
            }

            this.RaiseLogWritten(capturedAt.Value, logEntry);

            if (this.LogFilePath != null)
            {
                try
                {
                    File.AppendAllText(this.LogFilePath, logEntry + Environment.NewLine);
                }
                catch
                {
                }
            }
        }

        public async Task AddCommentAsync(DateTime? capturedAt = null, TimeSpan? elapsedSince = null, string comment = "<!!!>", bool? configureAwait = null)
        {
            if (configureAwait.HasValue)
            {
                await Task.Run(() => this.AddComment(capturedAt, elapsedSince, comment)).ConfigureAwait(configureAwait.Value);
            }
            else
            {
                await Task.Run(() => this.AddComment(capturedAt, elapsedSince, comment));
            }
        }

        public IReadOnlyList<string> GetLogLines(bool? returnFilteredLog = false, bool reverseOrder = false)
        {
            var result = returnFilteredLog switch
            {
                true => this.FilteredLogEntriesBindingList,
                false => this.LogEntriesBindingList,
                null => this.LogEntries.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value),
            };

            return reverseOrder ? result.Reverse().ToList() : result.ToList();
        }

        public string[] GetAllLogFilePaths()
        {
            return Directory.GetFiles(this.Settings.LogDirectory, "*.txt").Concat(Directory.GetFiles(this.Settings.LogDirectory, "*.log"))
                .OrderByDescending(f => f)
                .ToArray();
        }

        public string? GetPreviousLogFilePath(int backIndex = 0)
        {
            return this.GetAllLogFilePaths().Select(l => new FileInfo(l)).OrderByDescending(f => f.CreationTime) is IEnumerable<FileInfo> fileInfos
                ? fileInfos.Count() > backIndex ? fileInfos.ElementAt(backIndex).FullName : null
                : null;
        }

        public string SaveToRepository(string? differentFilePathOrDirectory = null, bool? returnFilteredLog = null, bool reverseOrder = false, bool forceSave = false)
        {
            if (!this.Settings.SaveToRepository && !forceSave)
            {
                return string.Empty;
            }

            try
            {
                string path = this.ResolveRepositoryLogFilePath(differentFilePathOrDirectory ?? this.Settings.SaveToRepositoryCustomFilePath);
                string fileName = Path.GetFileName(path) ?? (string.IsNullOrEmpty(this.Settings.LogFileBaseName) ? "dotnet-Application_Log_" : this.Settings.LogFileBaseName);
                string directory = Path.GetDirectoryName(path) ?? this.ResolveRepositoryDirectory(ensureDirectoryExists: true);

                IReadOnlyList<string> snapshot = this.GetLogLines(returnFilteredLog, reverseOrder);
                if (snapshot.Count == 0)
                {
                    return string.Empty;
                }

                var sb = new StringBuilder();
                sb.AppendLine("==============================================================");
                sb.AppendLine(UniversalHelper.SanitizeString(fileName, null, "".ToCharArray(), " ", true, true) + " Log");

                string timestamp = DateTime.Now.ToString(this.Settings.FileTimestampFormat);

                nint logCount = new(snapshot.LongCount());
                sb.AppendLine($"Saved at : {timestamp}");
                sb.Append("Entries   : ").AppendLine(logCount.ToString("N0"));
                sb.AppendLine("==============================================================");
                sb.AppendLine();
                sb.AppendJoin(Environment.NewLine, snapshot);

                this.PruneOldRepositoryLogs(directory);
                File.WriteAllText(path, sb.ToString());

                this.Log($"[SUCCESS] Log saved to {path} ({logCount:N0} entries)");

                return path;
            }
            catch (Exception ex)
            {
                this.LogError($"[ERROR] Failed to save log to repository: {ex.Message}");
                return string.Empty;
            }
        }

        public string ConfigureSaveToRepository(bool? configureToggle = null, string? subDirOrDifferentPath = null, int maxPreviousLogFiles = 8, Action? onShutdown = null)
        {
            this.Settings.SaveToRepository = configureToggle == null ? !this.Settings.SaveToRepository : configureToggle.Value;
            this.SaveToRepositoryOnShutdown = onShutdown;
            this.Settings.SaveToRepositoryCustomFilePath = string.IsNullOrEmpty(subDirOrDifferentPath) ? null : this.ResolveRepositoryLogFilePath(subDirOrDifferentPath);

            this.Settings.MaxRepositoryLogFiles = Math.Clamp(maxPreviousLogFiles, 0, int.MaxValue);
            this.PruneOldRepositoryLogs();

            try
            {
                if (this.Settings.SaveToRepository)
                {
                    this.SaveToRepository(subDirOrDifferentPath);
                }
            }
            catch (Exception ex)
            {
                this.Log($"Error configuring save to repository: {ex.Message}");
            }

            return this.Settings.SaveToRepositoryCustomFilePath ?? this.ResolveRepositoryDirectory();
        }

        public string[]? GetNamespacesForProject(string? projectName = null, bool includeSubNamespaces = true, bool ignoreCase = true)
        {
            if (string.IsNullOrEmpty(projectName))
            {
                projectName = typeof(RollingFileMemoryLogger).Namespace;
                if (string.IsNullOrEmpty(projectName))
                {
                    return null;
                }
            }

            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetTypes()
                .Where(t => t.IsClass && t.Namespace != null && (includeSubNamespaces ? t.Namespace.StartsWith(projectName) : t.Namespace.Equals(projectName, (ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))))
                .Select(t => t.Namespace)
                .Distinct()
                .OfType<string>()
                .ToArray();
        }

        public string ResolveRepositoryDirectory(string? projectName = null, string subPath = "Logs", bool ensureDirectoryExists = false)
        {
            projectName ??= typeof(RollingFileMemoryLogger).Namespace;

            // 1) Walk up from the running app until we find a solution file (= repo root).
            string repoRoot = AppContext.BaseDirectory;
            DirectoryInfo? dir = new(repoRoot);
            while (dir != null)
            {
                if (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0)
                {
                    repoRoot = dir.FullName;
                    break;
                }

                dir = dir.Parent;
            }

            // 2) Inside the repo, prefer the directory of the project this logger belongs to
            //    (e.g. "<repo>/ModularAudience.Shared"), otherwise the repo root itself.
            string projectDirectory = repoRoot;
            if (!string.IsNullOrEmpty(projectName))
            {
                try
                {
                    var searchOptions = new EnumerationOptions
                    {
                        MatchCasing = MatchCasing.CaseInsensitive,
                        RecurseSubdirectories = true
                    };
                    string? projectFile = Directory.EnumerateFiles(repoRoot, $"{projectName}.csproj", searchOptions).FirstOrDefault();
                    if (projectFile != null)
                    {
                        projectDirectory = Path.GetDirectoryName(projectFile) ?? repoRoot;
                    }
                }
                catch
                {
                    // fall back to the repo root
                }
            }

            string fullPath = Path.GetFullPath(Path.Combine(projectDirectory, subPath));

            if (ensureDirectoryExists && !Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            return fullPath;
        }

        public string ResolveRepositoryLogFilePath(string? differentFilePathOrDirectory = null)
        {
            string repoRootDir = this.ResolveRepositoryDirectory();
            string directory;
            if (Directory.Exists(differentFilePathOrDirectory))
            {
                directory = differentFilePathOrDirectory;
            }
            else
            {
                directory = this.ResolveRepositoryDirectory();
                Directory.CreateDirectory(directory);
            }

            string fileName;
            if (!string.IsNullOrEmpty(differentFilePathOrDirectory) && !Directory.Exists(differentFilePathOrDirectory))
            {
                fileName = Path.GetFileName(differentFilePathOrDirectory);
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = this.Settings.LogFileBaseName;
                }
            }
            else
            {
                fileName = this.Settings.LogFileBaseName;
            }

            try
            {
                fileName = $"{fileName}{DateTime.Now.ToString(this.Settings.FileTimestampFormat)}";
            }
            catch
            {
                fileName = $"{fileName}{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            }

            return Path.GetFullPath(Path.Combine(directory, fileName + "." + this.Settings.LogFileExtension.Trim('.')));
        }

        public string GetInnerExceptionsRecursively(Exception ex, int? maxDepth = null, bool appendStackTrace = true, string openingBracket = "(", string closingBracket = ")", string separator = " ", bool asSingleLine = false)
        {
            if (ex == null)
            {
                return string.Empty;
            }

            if (maxDepth <= 0)
            {
                return ex.Message;
            }

            StringBuilder sb = new();
            sb.AppendLine($"Exception: {ex.GetType().FullName}");
            string message = $"Message: {ex.Message}";

            Exception? inner = ex.InnerException;
            int count = 0;
            while (inner != null)
            {
                message += $"{separator}{openingBracket}{inner.Message}";
                inner = inner.InnerException;
                count++;
                if (maxDepth.HasValue && count >= maxDepth.Value)
                {
                    break;
                }
            }
            message += string.Concat(Enumerable.Repeat(closingBracket, count));

            sb.AppendLine(message);
            if (appendStackTrace)
            {
                sb.AppendLine($"StackTrace: {ex.StackTrace}");
            }
            return sb.Replace(Environment.NewLine, asSingleLine ? " " : Environment.NewLine).ToString();
        }

        public string GetInnerExceptionsRecursively(Exception ex)
        {
            return this.GetInnerExceptionsRecursively(ex, this.Settings.ExceptionPrintSettings.InnerExceptionMaxDepth, this.Settings.ExceptionPrintSettings.InnerExceptionAppendStackTrace, this.Settings.ExceptionPrintSettings.InnerExceptionOpeningBracket, this.Settings.ExceptionPrintSettings.InnerExceptionClosingBracket, this.Settings.ExceptionPrintSettings.InnerExceptionSeparator, this.Settings.ExceptionPrintSettings.InnerExceptionAsSingleLine);
        }

        public void ClearLogs()
        {
            this.LogEntries.Clear();
            if (this.UiContext != null)
            {
                this.UiContext.Post(_ => this.LogEntriesBindingList.Clear(), null);
                this.UiContext.Post(_ => this.FilteredLogEntriesBindingList.Clear(), null);
            }
            else
            {
                lock (this.LogEntriesBindingList)
                {
                    this.LogEntriesBindingList.Clear();
                }
                lock (this.FilteredLogEntriesBindingList)
                {
                    this.FilteredLogEntriesBindingList.Clear();
                }
            }
        }

        private void PruneOldRepositoryLogs(string? directory = null)
        {
            directory ??= this.Settings.SaveToRepositoryCustomFilePath is not null
                ? Path.GetDirectoryName(this.Settings.SaveToRepositoryCustomFilePath) ?? this.ResolveRepositoryDirectory()
                : this.ResolveRepositoryDirectory();

            try
            {
                FileInfo[] files = new DirectoryInfo(directory)
                    .GetFiles("AggregatedLog_*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToArray();

                foreach (FileInfo file in files.Skip(this.Settings.MaxRepositoryLogFiles))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private bool ShouldEchoToConsole(string logEntry)
        {
            if (this.Settings.Silent)
            {
                return false;
            }

            if (this.Settings.EchoToConsole == true)
            {
                return true;
            }
            else
            {
                return this.Settings.EchoToConsole == false
                    ? false
                    : this.Settings.EchoToConsoleKeyPhrases.Any(phrase => logEntry.Contains(phrase, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void RaiseLogWritten(DateTime timestamp, string line)
        {
            try
            {
                LogWritten?.Invoke(timestamp, line);
                this._logEntriesRingBufferCounter++;
            }
            catch
            {
            }
        }

        private void EnsureMaxLogEntriesWithOffloadAndBuffering()
        {
            int maxVal = this.Settings.MaxLogEntries ?? int.MaxValue;
            if (maxVal <= this.LogEntries.Count)
            {
                bool? error = null;
                switch (this.Settings.UseRingBuffer)
                {
                    case true:
                        {
                            while (maxVal <= this.LogEntries.Count)
                            {
                                var oldestKeyOpt = this.LogEntries.Keys.OrderBy(k => k).FirstOrDefault();
                                if (oldestKeyOpt != default)
                                {
                                    error = !this.LogEntries.TryRemove(oldestKeyOpt, out _);
                                }
                                else
                                {
                                    break;
                                }
                            }
                            break;
                        }
                    case false:
                        {
                            this.SaveToRepository();
                            this.LogEntries.Clear();
                            this.LogEntriesBindingList.Clear();
                            this.FilteredLogEntriesBindingList.Clear();
                            int? fileIndex = int.TryParse(Path.GetFileNameWithoutExtension(this.LogFilePath)?.Split('_').Last().Trim(), out var index) ? index : null;
                            this.LogFilePath = Path.GetFullPath(this.LogFilePath?.TrimEnd('_') + "_" + (fileIndex.HasValue ? (fileIndex.Value + 1).ToString() : "0"));
                            break;
                        }
                    case null:
                        {
                            while (maxVal <= this.LogEntries.Count)
                            {
                                var oldestEntry = this.LogEntries.Keys.OrderBy(k => k).FirstOrDefault();
                                if (oldestEntry != default)
                                {
                                    error = !this.LogEntries.TryRemove(oldestEntry, out _);
                                }
                                else
                                {
                                    break;
                                }
                            }
                            if (this._logEntriesRingBufferCounter >= maxVal)
                            {
                                this.SaveToRepository();
                                this._logEntriesRingBufferCounter = 0;
                                int? fileIndexNull = int.TryParse(Path.GetFileNameWithoutExtension(this.LogFilePath)?.Split('_').Last().Trim(), out var indexNull) ? indexNull : null;
                                this.LogFilePath = Path.GetFullPath(this.LogFilePath?.TrimEnd('_') + "_" + (fileIndexNull.HasValue ? (fileIndexNull.Value + 1).ToString() : "0"));
                            }
                            break;
                        }
                }
                if (error == true)
                {
                    this.LogWarning($"[WARN] Failed to remove the oldest log entry while ensuring max log entries. Current count: {this.LogEntries.Count}, Max allowed: {maxVal}");
                }
            }
        }
    }

    // ========================================================================
    // UniversalHelper
    // ========================================================================

    /// <summary>
    /// Provides highly optimized, allocation-free helper methods for working with character sets.
    /// </summary>
    public static partial class UniversalHelper
    {
        // --- Constant Building Blocks (Compile-Time) ---
        private static readonly string Numeric = "0123456789";
        private static readonly string AlphaLower = "abcdefghijklmnopqrstuvwxyz";
        private static readonly string AlphaUpper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private static readonly string Alpha = AlphaLower + AlphaUpper;
        private static readonly string Alphanumeric = Alpha + Numeric;

        // Language-specific & Extended
        private static readonly string GermanUmlauts = "äöüÄÖÜß";
        private static readonly string AlphaUmlauts = Alpha + GermanUmlauts;
        private static readonly string AlphanumericUmlauts = Alphanumeric + GermanUmlauts;

        // Special Characters
        private static readonly string Space = " ";
        private static readonly string Underscore = "_";
        private static readonly string Dash = "-";
        private static readonly string Apostrophe = "'";
        private static readonly string EmailSpecial = "@.-_";

        // --- Precompiled FrozenSets (Zero-Allocation at Runtime) ---

        // Invalid Sets
        private static readonly FrozenSet<char> InvalidNone = Array.Empty<char>().ToFrozenSet();
        private static readonly FrozenSet<char> InvalidControl = Enumerable.Range(0, 32).Append(127).Select(c => (char)c).ToFrozenSet();
        private static readonly FrozenSet<char> InvalidFileName = Path.GetInvalidFileNameChars().ToFrozenSet();
        private static readonly FrozenSet<char> InvalidPath = Path.GetInvalidPathChars().ToFrozenSet();
        private static readonly FrozenSet<char> InvalidHtmlDangerous = "<>\"'&".ToFrozenSet();
        private static readonly FrozenSet<char> InvalidSqlDangerous = "';-".ToFrozenSet();

        // Valid Sets
        private static readonly FrozenSet<char> ValidNone = Array.Empty<char>().ToFrozenSet();
        private static readonly FrozenSet<char> ValidNumeric = Numeric.ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlpha = Alpha.ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaUmlauts = AlphaUmlauts.ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphanumeric = Alphanumeric.ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphanumericUmlauts = AlphanumericUmlauts.ToFrozenSet();

        // Alphanumeric Combinations
        private static readonly FrozenSet<char> ValidAlphaNumSpace = (Alphanumeric + Space).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumUnderscore = (Alphanumeric + Underscore).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumDash = (Alphanumeric + Dash).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumSpaceUnderscore = (Alphanumeric + Space + Underscore).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumSpaceDash = (Alphanumeric + Space + Dash).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumUnderscoreDash = (Alphanumeric + Underscore + Dash).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumSpaceUnderscoreDash = (Alphanumeric + Space + Underscore + Dash).ToFrozenSet();
        private static readonly FrozenSet<char> ValidAlphaNumUmlautsSpaceDash = (AlphanumericUmlauts + Space + Dash).ToFrozenSet();

        // Domain-Specific Sets
        private static readonly FrozenSet<char> ValidHexadecimal = (Numeric + "abcdefABCDEF").ToFrozenSet();
        private static readonly FrozenSet<char> ValidBase64 = (Alphanumeric + "+/=").ToFrozenSet();
        private static readonly FrozenSet<char> ValidBase64UrlSafe = (Alphanumeric + "-_").ToFrozenSet();
        private static readonly FrozenSet<char> ValidPersonName = (AlphaUmlauts + Space + Dash + Apostrophe).ToFrozenSet();
        private static readonly FrozenSet<char> ValidEmailBasic = (Alphanumeric + EmailSpecial).ToFrozenSet();

        /// <summary>
        /// Retrieves a frozen set of invalid characters based on the specified <see cref="InvalidCharSets"/> value.
        /// </summary>
        public static FrozenSet<char> GetInvalidCharHashSet(InvalidCharSets charSet) => charSet switch
        {
            InvalidCharSets.None => InvalidNone,
            InvalidCharSets.ControlCharacters => InvalidControl,
            InvalidCharSets.InvalidFileNameChars => InvalidFileName,
            InvalidCharSets.InvalidPathChars => InvalidPath,
            InvalidCharSets.HtmlDangerousChars => InvalidHtmlDangerous,
            InvalidCharSets.SqlDangerousChars => InvalidSqlDangerous,
            _ => throw new ArgumentOutOfRangeException(nameof(charSet), charSet, "Unknown InvalidCharSets value provided.")
        };

        /// <summary>
        /// Retrieves a frozen set of valid characters based on the specified <see cref="ValidCharSets"/> value.
        /// </summary>
        public static FrozenSet<char> GetValidCharHashSet(ValidCharSets charSet) => charSet switch
        {
            ValidCharSets.None => ValidNone,
            ValidCharSets.Numeric => ValidNumeric,
            ValidCharSets.Alpha => ValidAlpha,
            ValidCharSets.AlphaWithUmlauts => ValidAlphaUmlauts,
            ValidCharSets.Alphanumeric => ValidAlphanumeric,
            ValidCharSets.AlphanumericWithUmlauts => ValidAlphanumericUmlauts,
            ValidCharSets.AlphanumericWithSpaces => ValidAlphaNumSpace,
            ValidCharSets.AlphanumericWithUnderscores => ValidAlphaNumUnderscore,
            ValidCharSets.AlphanumericWithDashes => ValidAlphaNumDash,
            ValidCharSets.AlphanumericWithSpacesAndUnderscores => ValidAlphaNumSpaceUnderscore,
            ValidCharSets.AlphanumericWithSpacesAndDashes => ValidAlphaNumSpaceDash,
            ValidCharSets.AlphanumericWithUnderscoresAndDashes => ValidAlphaNumUnderscoreDash,
            ValidCharSets.AlphanumericWithSpacesUnderscoresAndDashes => ValidAlphaNumSpaceUnderscoreDash,
            ValidCharSets.AlphanumericWithUmlautsSpacesAndDashes => ValidAlphaNumUmlautsSpaceDash,
            ValidCharSets.Hexadecimal => ValidHexadecimal,
            ValidCharSets.Base64 => ValidBase64,
            ValidCharSets.Base64UrlSafe => ValidBase64UrlSafe,
            ValidCharSets.PersonNameBasic => ValidPersonName,
            ValidCharSets.EmailBasic => ValidEmailBasic,
            _ => throw new ArgumentOutOfRangeException(nameof(charSet), charSet, "Unknown ValidCharSets value provided.")
        };

        /// <summary>
        /// Sanitizes the input string by replacing invalid characters or strings with a specified replacement string.
        /// </summary>
        public static string SanitizeString(this string input, IEnumerable<string>? invalidStrings = null, IEnumerable<char>? invalidChars = null, string replacement = " ", bool caseSensitive = true, bool separateCamelCase = false)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var charSet = new HashSet<char>();

            if (invalidStrings != null)
            {
                foreach (var str in invalidStrings)
                {
                    foreach (var c in str)
                    {
                        charSet.Add(c);
                    }
                }
            }

            if (invalidChars != null)
            {
                foreach (var c in invalidChars)
                {
                    charSet.Add(c);
                }
            }

            string replaced = input;

            if (charSet.Count > 0)
            {
                if (!caseSensitive)
                {
                    var caseInsensitiveSet = new HashSet<char>(charSet.Count * 2);
                    foreach (var c in charSet)
                    {
                        caseInsensitiveSet.Add(char.ToLowerInvariant(c));
                        caseInsensitiveSet.Add(char.ToUpperInvariant(c));
                    }
                    charSet = caseInsensitiveSet;
                }

                var searchValues = SearchValues.Create(charSet.ToArray());

                if (input.AsSpan().IndexOfAny(searchValues) >= 0)
                {
                    var sb = new StringBuilder(input.Length);

                    foreach (char c in input)
                    {
                        if (searchValues.Contains(c))
                        {
                            sb.Append(replacement);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                    }
                    replaced = sb.ToString();
                }
            }

            if (separateCamelCase)
            {
                replaced = CamelCaseRegex().Replace(replaced, " ");
            }

            return replaced;
        }

        /// <summary>
        /// Verifies whether the provided format string is valid for formatting DateTime or TimeSpan objects.
        /// </summary>
        public static string VerifyFormatString(this string formatString, out string? errorMessage, object? referenceObj = null)
        {
            errorMessage = null;
            try
            {
                if (referenceObj != null)
                {
                    var testValue = referenceObj switch
                    {
                        DateTime dt => dt.ToString(formatString),
                        TimeSpan ts => ts.ToString(formatString),
                        IFormattable formattable => formattable.ToString(formatString, null),
                        _ => throw new ArgumentException("Reference object must be DateTime, TimeSpan, or implement IFormattable.")
                    };
                }
                else
                {
                    var testValue = DateTime.Now.ToString(formatString);
                }
                return formatString;
            }
            catch (Exception ex)
            {
                errorMessage = $"Invalid format string '{formatString}': {ex.Message}";
                return "HH-mm-ss.fff";
            }
        }

        [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
        private static partial Regex CamelCaseRegex();
    }

    /// <summary>
    /// Defines contexts for characters that are explicitly forbidden (Blacklisting).
    /// </summary>
    public enum InvalidCharSets
    {
        /// <summary>
        /// No restrictions.
        /// </summary>
        None = 0,

        /// <summary>
        /// Non-printable control characters (ASCII 0-31 and 127).
        /// </summary>
        ControlCharacters = 1,

        /// <summary>
        /// Characters rejected by most file systems for file names.
        /// </summary>
        InvalidFileNameChars = 10,

        /// <summary>
        /// Characters rejected by most file systems for directory paths.
        /// </summary>
        InvalidPathChars = 11,

        /// <summary>
        /// Characters commonly used in Cross-Site Scripting (XSS) attacks.
        /// </summary>
        HtmlDangerousChars = 20,

        /// <summary>
        /// Characters commonly used in SQL injection attacks.
        /// </summary>
        SqlDangerousChars = 21
    }

    /// <summary>
    /// Defines contexts for characters that are explicitly allowed (Whitelisting).
    /// </summary>
    public enum ValidCharSets
    {
        /// <summary>
        /// No valid characters.
        /// </summary>
        None = 0,

        /// <summary>
        /// Allows only digits (0-9).
        /// </summary>
        Numeric = 1,

        /// <summary>
        /// Allows only standard alphabetic letters (A-Z, a-z).
        /// </summary>
        Alpha = 2,

        /// <summary>
        /// Allows alphabetic letters including German umlauts.
        /// </summary>
        AlphaWithUmlauts = 3,

        /// <summary>
        /// Allows letters and digits.
        /// </summary>
        Alphanumeric = 4,

        /// <summary>
        /// Allows letters, digits, and umlauts.
        /// </summary>
        AlphanumericWithUmlauts = 5,

        /// <summary>
        /// Allows letters, digits, and spaces.
        /// </summary>
        AlphanumericWithSpaces = 10,

        /// <summary>
        /// Allows letters, digits, and underscores.
        /// </summary>
        AlphanumericWithUnderscores = 11,

        /// <summary>
        /// Allows letters, digits, and dashes.
        /// </summary>
        AlphanumericWithDashes = 12,

        /// <summary>
        /// Allows letters, digits, spaces, and underscores.
        /// </summary>
        AlphanumericWithSpacesAndUnderscores = 13,

        /// <summary>
        /// Allows letters, digits, spaces, and dashes.
        /// </summary>
        AlphanumericWithSpacesAndDashes = 14,

        /// <summary>
        /// Allows letters, digits, underscores, and dashes.
        /// </summary>
        AlphanumericWithUnderscoresAndDashes = 15,

        /// <summary>
        /// Allows letters, digits, spaces, underscores, and dashes.
        /// </summary>
        AlphanumericWithSpacesUnderscoresAndDashes = 16,

        /// <summary>
        /// Allows letters, digits, umlauts, spaces, and dashes.
        /// </summary>
        AlphanumericWithUmlautsSpacesAndDashes = 20,

        /// <summary>
        /// Allows only valid hexadecimal digits.
        /// </summary>
        Hexadecimal = 30,

        /// <summary>
        /// Allows standard Base64 characters.
        /// </summary>
        Base64 = 31,

        /// <summary>
        /// Allows URL-safe Base64 characters.
        /// </summary>
        Base64UrlSafe = 32,

        /// <summary>
        /// Allows letters, umlauts, spaces, hyphens, and apostrophes.
        /// </summary>
        PersonNameBasic = 40,

        /// <summary>
        /// Allows letters, digits, and special characters commonly required in email routing.
        /// </summary>
        EmailBasic = 41
    }
}
