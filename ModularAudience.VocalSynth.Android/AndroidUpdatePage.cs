using Android.Content;
using Android.OS;
using Android.Provider;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using ModularAudience.VocalSynth.Android.Services;

namespace ModularAudience.VocalSynth.Android;

internal sealed class AndroidUpdatePage : ContentPage
{
    private readonly AndroidReleaseUpdate update;
    private readonly AndroidReleaseUpdateService updateService;
    private readonly Label statusLabel = new() { Text = "Preparing download..." };
    private readonly Label detailsLabel = new() { Text = string.Empty };
    private readonly ProgressBar progressBar = new();
    private readonly Button installButton = new() { Text = "Install update", IsEnabled = false };
    private readonly Button closeButton = new() { Text = "Cancel download" };
    private readonly CancellationTokenSource cancellation = new();
    private string? apkPath;
    private bool downloadStarted;

    internal AndroidUpdatePage(
        AndroidReleaseUpdate update,
        AndroidReleaseUpdateService updateService)
    {
        this.update = update;
        this.updateService = updateService;
        this.Title = "App update";
        this.BackgroundColor = Color.FromArgb("#171717");
        this.installButton.Clicked += async (_, _) => await this.InstallAsync();
        this.closeButton.Clicked += async (_, _) =>
        {
            if (this.apkPath == null)
                this.cancellation.Cancel();
            await Shell.Current.Navigation.PopModalAsync();
        };
        this.Content = new VerticalStackLayout
        {
            Padding = new Thickness(24),
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = $"Downloading build {update.BuildNumber}",
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
                this.statusLabel,
                this.progressBar,
                this.detailsLabel,
                this.installButton,
                this.closeButton,
            },
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!this.downloadStarted)
        {
            this.downloadStarted = true;
            _ = this.DownloadAsync();
        }
    }

    private async Task DownloadAsync()
    {
        try
        {
            var progress = new Progress<AndroidUpdateProgress>(this.UpdateProgress);
            this.apkPath = await this.updateService.DownloadAsync(
                this.update,
                progress,
                this.cancellation.Token);
            this.statusLabel.Text = $"Build {this.update.BuildNumber} is ready to install.";
            this.detailsLabel.Text = FormatBytes(this.update.FileSize);
            this.progressBar.Progress = 1;
            this.installButton.IsEnabled = true;
            this.closeButton.Text = "Close";
        }
        catch (OperationCanceledException) when (this.cancellation.IsCancellationRequested)
        {
            this.statusLabel.Text = "Download cancelled.";
            this.closeButton.Text = "Close";
        }
        catch (Exception ex)
        {
            this.statusLabel.Text = "The update could not be downloaded.";
            this.closeButton.Text = "Close";
            await this.DisplayAlertAsync("Update download failed", ex.ToString(), "OK");
        }
    }

    private void UpdateProgress(AndroidUpdateProgress progress)
    {
        if (progress.TotalBytes > 0)
            this.progressBar.Progress = Math.Clamp(progress.DownloadedBytes / (double)progress.TotalBytes, 0, 1);
        string total = progress.TotalBytes > 0 ? FormatBytes(progress.TotalBytes) : "unknown size";
        string remaining = progress.Remaining?.ToString(@"mm\:ss") ?? "--:--";
        this.statusLabel.Text = $"{FormatBytes(progress.DownloadedBytes)} / {total}";
        this.detailsLabel.Text =
            $"Elapsed {progress.Elapsed:hh\\:mm\\:ss} | ETA {remaining} | {FormatBytes((long)progress.BytesPerSecond)}/s";
    }

    private async Task InstallAsync()
    {
        if (this.apkPath == null)
            return;

        try
        {
            if (!AndroidUpdateInstaller.CanInstallFromThisSource())
            {
                bool openSettings = await this.DisplayAlertAsync(
                    "Allow app updates",
                    "Android requires permission to install updates from ModularAudience. Enable it in the next screen, return here, then tap Install update again.",
                    "Open settings",
                    "Cancel");
                if (openSettings)
                    AndroidUpdateInstaller.OpenInstallPermissionSettings();
                return;
            }

            AndroidUpdateInstaller.LaunchInstaller(this.apkPath);
        }
        catch (Exception ex)
        {
            await this.DisplayAlertAsync("Could not start installation", ex.ToString(), "OK");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}

internal static class AndroidUpdateInstaller
{
    private const string FileProviderAuthoritySuffix = ".updates";

    internal static bool CanInstallFromThisSource()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return true;
        Context context = global::Android.App.Application.Context;
        return context.PackageManager?.CanRequestPackageInstalls() == true;
    }

    internal static void OpenInstallPermissionSettings()
    {
        Context context = global::Android.App.Application.Context;
        var intent = new Intent(Settings.ActionManageUnknownAppSources);
        intent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
        (Platform.CurrentActivity ?? throw new InvalidOperationException("The Android activity is unavailable."))
            .StartActivity(intent);
    }

    internal static void LaunchInstaller(string apkPath)
    {
        Context context = global::Android.App.Application.Context;
        string authority = $"{context.PackageName}{FileProviderAuthoritySuffix}";
        Android.Net.Uri apkUri = FileProvider.GetUriForFile(
            context,
            authority,
            new Java.IO.File(apkPath));
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(apkUri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
