using Android.Content;
using System.Diagnostics;

namespace ModularAudience.VocalSynth.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilterAttribute(new[] { "android.intent.action.MY_PACKAGE_REPLACED" })]
public sealed class AppUpdatedReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null)
            return;

        try
        {
            Intent? launchIntent = context.PackageManager?.GetLaunchIntentForPackage(
                context.PackageName ?? string.Empty);
            if (launchIntent == null)
                return;

            launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            context.StartActivity(launchIntent);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Could not restart ModularAudience after its update: {ex}");
        }
    }
}
