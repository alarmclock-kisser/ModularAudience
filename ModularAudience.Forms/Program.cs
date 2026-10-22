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

            EnsureAppSettingsFile();

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

            // No automatic tab order in the windows, so that the arrow keys and the tab
            // key do not change the focus to a button or a scroll bar.
            TabNavigationDisabler.Install();

            Application.Run(new WindowMain(logger, serviceProvider));
        }

        private static void EnsureAppSettingsFile()
        {
            var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(appSettingsPath))
            {
                return;
            }

            using var defaults = typeof(Program).Assembly.GetManifestResourceStream("ModularAudience.Forms.appsettings.json")
                ?? throw new InvalidOperationException("The embedded default appsettings.json resource is missing.");

            try
            {
                using var appSettings = new FileStream(appSettingsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                defaults.CopyTo(appSettings);
            }
            catch (IOException) when (File.Exists(appSettingsPath))
            {
            }
        }
    }
}