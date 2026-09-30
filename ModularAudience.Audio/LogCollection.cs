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
        // Legacy shim. All logging now goes through LogManager -> RollingFileMemoryLogger.
        // Kept so the ~90 existing call sites (Audio/Generators/Forms) keep working.

        // Fields
        public static readonly BindingList<string> Logs = LogManager.Logs;
        // Back-compat events, raised by LogManager.
        public static event Action<string>? NewLogPosted
        {
            add => LogManager.NewLogPosted += value;
            remove => LogManager.NewLogPosted -= value;
        }
        public static event Action<DateTime, string>? NewLogPostedWithTimestamp
        {
            add => LogManager.NewLogPostedWithTimestamp += value;
            remove => LogManager.NewLogPostedWithTimestamp -= value;
        }
        // User comment history (newest first)
        public static readonly List<string> UserComments = [];

        // Compat state, mirrored from LogManager.
        public static int MaxLogCount
        {
            get => LogManager.MaxLogCount;
            set => LogManager.MaxLogCount = value;
        }

        public static bool AutoScroll
        {
            get => LogManager.AutoScroll;
            set => LogManager.AutoScroll = value;
        }

        public static string TimeFormat
        {
            get => LogManager.TimeFormat;
            set => LogManager.TimeFormat = value;
        }


        // Lambda
        public static int CurrentLogCount => LogManager.Logs.Count;
        public static string CurrentTimeStamp => IsTimeFormatValid() ? "[" + DateTime.Now.ToString(TimeFormat) + "]" : string.Empty;

        // Methods
        public static void Log(string message)
        {
            // Forward to the RoFM logger (single source of truth + file logging).
            LogManager.Log(message);
        }

        // Allows posting a fully-formatted log message (including timestamp) directly to subscribers.
        public static void PostRaw(string fullMessage)
        {
            LogManager.Log(fullMessage);
        }

        public static void PostComment(DateTime timestamp, string comment)
        {
            // Record in local history (newest first) and forward to the RoFM logger.
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

            LogManager.PostComment(timestamp, comment);
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
