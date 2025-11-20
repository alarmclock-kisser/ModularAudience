using System;
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
        public static int MaxLogCount { get; set; } = 512;
        public static bool AutoScroll { get; set; } = true;
        public static string TimeFormat { get; set; } = "HH:mm:ss.fff";


        // Lambda
        public static int CurrentLogCount => Logs.Count;
        public static string CurrentTimeStamp => IsTimeFormatValid() ? "[" + DateTime.Now.ToString(TimeFormat) + "]" : string.Empty;



        // Methods
        public static void Log(string message)
        {
            message = $"{CurrentTimeStamp} {message}";
            Logs.Add(message);
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
