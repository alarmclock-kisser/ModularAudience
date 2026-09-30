using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularAudience.Audio;
using ModularAudience.Shared;

namespace ModularAudience.Forms
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // Build configuration from appsettings.json
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Configure RollingFileMemoryLogger from appsettings.json
            var loggerOptions = configuration.GetSection("LoggerSettings").Get<RollingFileMemoryLoggerOptions>()
                ?? new RollingFileMemoryLoggerOptions();

            // Ensure log directory is absolute
            if (!string.IsNullOrEmpty(loggerOptions.LogDirectory) && !Path.IsPathRooted(loggerOptions.LogDirectory))
            {
                loggerOptions.LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, loggerOptions.LogDirectory);
            }

            var logger = new RollingFileMemoryLogger(loggerOptions);
            logger.StartBackgroundWriter(CancellationToken.None);

            // Install the configured logger so all LogManager/LogCollection calls go to it.
            LogManager.Configure(logger);

            // Configure services
            var services = new ServiceCollection();
            services.AddSingleton<IRollingFileMemoryLogger>(logger);
            services.AddSingleton(logger);

            using var serviceProvider = services.BuildServiceProvider();

            // Persist a copy of the session log into the repository (see "SaveToRepository"
            // in appsettings.json) once the UI is done.
            void SaveSessionLog()
            {
                try
                {
                    logger.SaveToRepository();
                }
                catch { }
            }

            Application.ApplicationExit += (_, __) => SaveSessionLog();
            AppDomain.CurrentDomain.ProcessExit += (_, __) => SaveSessionLog();

            Application.Run(new WindowMain(logger, serviceProvider));
        }
    }
}