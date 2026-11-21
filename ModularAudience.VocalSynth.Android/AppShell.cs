using ModularAudience.VocalSynth.Android.Services;
using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace ModularAudience.VocalSynth.Android;

internal sealed class AppShell : Shell
{
    private const string UpdatesDisabledPreference = "androidUpdates.disabled";
    private const string SkippedBuildPreference = "androidUpdates.skippedBuild";
    private readonly AndroidReleaseUpdateService updateService = new();
    private bool updateCheckStarted;

    internal AppShell(SoundboardStore store, CoOpAndroidSession session)
    {
        this.Title = "ModularAudience";
        this.FlyoutBehavior = FlyoutBehavior.Flyout;
        this.Items.Add(CreateItem("VocalSynth", new MainPage()));
        this.Items.Add(CreateItem("Co-op", new CoOpPage(session, store)));
        this.Items.Add(CreateItem("Soundboard", new SoundboardPage(store)));
        var checkForUpdates = new MenuItem
        {
            Text = "Check for updates",
            Command = new Command(async () => await this.CheckForUpdatesAsync(manual: true)),
        };
        this.MenuItems.Add(checkForUpdates);
        this.Loaded += this.AppShell_Loaded;
    }

    private async void AppShell_Loaded(object? sender, EventArgs e)
    {
        this.Loaded -= this.AppShell_Loaded;
        if (this.updateCheckStarted)
            return;
        this.updateCheckStarted = true;
        await this.CheckForUpdatesAsync(manual: false);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (manual)
        {
            Preferences.Default.Remove(UpdatesDisabledPreference);
            Preferences.Default.Remove(SkippedBuildPreference);
        }
        else if (Preferences.Default.Get(UpdatesDisabledPreference, false))
        {
            return;
        }

        try
        {
            if (!manual)
                await Task.Delay(500);
            AndroidReleaseUpdate? update = await this.updateService.FindLatestSignedUpdateAsync();
            if (update == null)
            {
                if (manual)
                    await this.DisplayAlertAsync("No update available", "No signed Android release was found.", "OK");
                return;
            }

            if (update.BuildNumber <= GetInstalledBuildNumber())
            {
                if (manual)
                    await this.DisplayAlertAsync("You're up to date", "This is the latest signed Android build.", "OK");
                return;
            }

            if (!manual && update.BuildNumber == Preferences.Default.Get(SkippedBuildPreference, 0))
                return;

            string choice = await this.DisplayActionSheetAsync(
                $"ModularAudience build {update.BuildNumber} is available.",
                "Later",
                null,
                "Download and install",
                "Skip this build",
                "Don't check again");
            switch (choice)
            {
                case "Download and install":
                    await this.Navigation.PushModalAsync(new AndroidUpdatePage(update, this.updateService));
                    break;
                case "Skip this build":
                    Preferences.Default.Set(SkippedBuildPreference, update.BuildNumber);
                    break;
                case "Don't check again":
                    Preferences.Default.Set(UpdatesDisabledPreference, true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Android update check failed: {ex}");
            if (manual)
                await this.DisplayAlertAsync("Update check failed", ex.ToString(), "OK");
        }
    }

    private static int GetInstalledBuildNumber()
    {
        return int.TryParse(AppInfo.Current.BuildString, out int buildNumber)
            ? buildNumber
            : 0;
    }

    private static FlyoutItem CreateItem(string title, Page page)
    {
        var item = new FlyoutItem
        {
            Title = title,
            Route = title.Replace("-", string.Empty, StringComparison.Ordinal),
        };
        item.Items.Add(new ShellContent
        {
            Title = title,
            Content = page,
            Route = title.Replace("-", string.Empty, StringComparison.Ordinal),
        });
        return item;
    }
}
