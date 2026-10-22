using ModularAudience.Audio;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private YoutubeBrowserWindow? youtubeBrowser;
    private AudioCollectionView? youtubeCollection;

    private void BrowseYoutube_Click(object? sender, EventArgs e)
    {
        try
        {
            if (youtubeBrowser is null || youtubeBrowser.IsDisposed)
                youtubeBrowser = new YoutubeBrowserWindow(ImportYoutubeAudio);
            if (youtubeBrowser.WindowState == FormWindowState.Minimized)
                youtubeBrowser.WindowState = FormWindowState.Normal;
            youtubeBrowser.Show(this);
            youtubeBrowser.BringToFront();
            youtubeBrowser.Activate();
        }
        catch (Exception error)
        {
            LogManager.LogError($"Unable to open the YouTube browser: {error}");
            WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "YouTube browser error", error);
        }
    }

    private void ImportYoutubeAudio(AudioObj audio)
    {
        if (IsDisposed || Disposing)
            throw new InvalidOperationException("The main window is closing; the audio cannot be imported.");
        if (youtubeCollection is null || youtubeCollection.IsDisposed || youtubeCollection.Disposing)
            youtubeCollection = new AudioCollectionView([], "YouTube Audio Collection");
        var collection = youtubeCollection;
        collection.Show();
        int number = GetCollectionNumber(collection);
        AudioCollectionTags[audio.Id] = number;
        try
        {
            collection.AudioC.Audios.Add(audio);
        }
        catch
        {
            collection.AudioC.Audios.Remove(audio);
            AudioCollectionTags.Remove(audio.Id);
            throw;
        }
        LogManager.LogSuccess($"YouTube audio imported: {audio.Name}");
    }
}
