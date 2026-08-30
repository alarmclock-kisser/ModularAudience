using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModularAudience.Audio
{
    public static class LogCollection
    {
        // Fields
        public static readonly BindingList<string> Logs = [];
        // Event used to notify UI code to append a log on the UI thread.
        public static event Action<string>? NewLogPosted;
        // New event providing explicit timestamp and full preformatted message so UI can insert chronologically
        public static event Action<DateTime, string>? NewLogPostedWithTimestamp;
        // User comment history (newest first)
        public static readonly List<string> UserComments = [];
        // Buffer pending comment posts to coalesce duplicates (debounce)
        private static readonly List<(DateTime ts, string full)> _pendingPosts = [];
        private static readonly object _pendingLock = new();
        private static readonly System.Windows.Forms.Timer _flushTimer;
        public static int MaxLogCount { get; set; } = 512;
        public static bool AutoScroll { get; set; } = true;
        public static string TimeFormat { get; set; } = "HH:mm:ss.fff";


        // Lambda
        public static int CurrentLogCount => Logs.Count;
        public static string CurrentTimeStamp => IsTimeFormatValid() ? "[" + DateTime.Now.ToString(TimeFormat) + "]" : string.Empty;

        static LogCollection()
        {
            _flushTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _flushTimer.Tick += (_, __) => FlushPendingOnUi();
        }



        // Methods
        public static void Log(string message)
        {
            DateTime ts = DateTime.Now;
            string full = $"[{ts.ToString(TimeFormat)}] {message}";
            try
            {
                if (NewLogPostedWithTimestamp != null)
                {
                    NewLogPostedWithTimestamp.Invoke(ts, full);
                }
                else
                {
                    NewLogPosted?.Invoke(full);
                }
            }
            catch { }
        }

        // Allows posting a fully-formatted log message (including timestamp) directly to subscribers.
        public static void PostRaw(string fullMessage)
        {
            DateTime ts = DateTime.Now;
            // try to parse timestamp from leading [..]
            try
            {
                int a = fullMessage.IndexOf('[');
                int b = fullMessage.IndexOf(']');
                if (a >= 0 && b > a)
                {
                    string inner = fullMessage.Substring(a + 1, b - a - 1);
                    if (DateTime.TryParseExact(inner, TimeFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime parsed))
                    {
                        ts = parsed;
                    }
                }
            }
            catch { }

            try
            {
                if (NewLogPostedWithTimestamp != null)
                {
                    NewLogPostedWithTimestamp.Invoke(ts, fullMessage);
                }
                else
                {
                    NewLogPosted?.Invoke(fullMessage);
                }
            }
            catch { }
        }

        public static void PostComment(DateTime timestamp, string comment)
        {
            string full = $"[{timestamp.ToString(TimeFormat)}] {comment}";
            // store in user comments (newest first)
            try
            {
                lock (UserComments)
                {
                    UserComments.Insert(0, comment);
                    if (UserComments.Count > 256)
                    {
                        UserComments.RemoveRange(256, UserComments.Count - 256);
                    }
                }
            }
            catch { }

            // Enqueue pending and flush shortly to avoid duplicate inserts when multiple callers fire quickly.
            try
            {
                lock (_pendingLock)
                {
                    _pendingPosts.Add((timestamp, full));
                }
                try
                {
                    // Start timer on UI thread if possible; otherwise start directly.
                    if (Application.OpenForms.Count > 0)
                    {
                        var any = Application.OpenForms[0];
                        try { any?.BeginInvoke((Action) (() => { try { _flushTimer.Stop(); } catch { } try { _flushTimer.Start(); } catch { } })); } catch { _flushTimer.Stop(); try { _flushTimer.Start(); } catch { } }
                    }
                    else
                    {
                        try { _flushTimer.Stop(); } catch { }
                        try { _flushTimer.Start(); } catch { }
                    }
                }
                catch { }
            }
            catch { }
        }

        private static void FlushPendingOnUi()
        {
            List<(DateTime ts, string full)> items;
            lock (_pendingLock)
            {
                if (_pendingPosts.Count == 0)
                {
                    return;
                }

                items = new List<(DateTime ts, string full)>(_pendingPosts);
                _pendingPosts.Clear();
            }

            try
            {
                // Coalesce by full message to avoid duplicates
                var unique = items
                    .GroupBy(i => i.full)
                    .Select(g => g.OrderBy(i => i.ts).First())
                    .OrderBy(i => i.ts)
                    .ToList();

                foreach (var it in unique)
                {
                    try
                    {
                        // Avoid duplicates by checking existing Logs collection
                        bool shouldPost = true;
                        try
                        {
                            if (Logs.Contains(it.full))
                            {
                                shouldPost = false;
                            }
                        }
                        catch { }

                        if (!shouldPost)
                        {
                            continue;
                        }

                        if (NewLogPostedWithTimestamp != null)
                        {
                            NewLogPostedWithTimestamp.Invoke(it.ts, it.full);
                        }
                        else
                        {
                            NewLogPosted?.Invoke(it.full);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void Log(Exception exception)
        {
            string exceptionMessage = exception.Message;
            int innerExceptionCount = 0;
            Exception? innerException = exception.InnerException;
            while (innerException != null)
            {
                innerExceptionCount++;
                exceptionMessage += $" ({innerException.Message}";
                innerException = innerException.InnerException;
            }
            exceptionMessage += new string(')', innerExceptionCount);
            Log(exceptionMessage);
        }


        // Helpers
        private static bool IsTimeFormatValid(string? format = null)
        {
            if (string.IsNullOrEmpty(format))
            {
                format = TimeFormat;
            }

            try
            {
                string test = DateTime.Now.ToString(format);
                return true;
            }
            catch
            {
                return false;
            }
        }



    }
}
