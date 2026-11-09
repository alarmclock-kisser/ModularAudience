namespace ModularAudience.Forms.Modules.Dialogs;

internal static class MixProjectMenu
{
    internal static ToolStripMenuItem CreateItem(Func<Task>? open = null)
    {
        var item = new ToolStripMenuItem("Open Project (.mamix)") { Name = "openMixProject" };
        item.Click += async (_, _) => await (open?.Invoke() ?? OpenNewAsync()); return item;
    }
    internal static OpenFileDialog CreateOpenDialog() => new()
    {
        Title = "Open Mix Project", Filter = "ModularAudience mix (*.mamix)|*.mamix", DefaultExt = "mamix",
        InitialDirectory = MixTrackView.DefaultProjectsDirectory, Multiselect = false, CheckFileExists = true, RestoreDirectory = true
    };
    internal static async Task OpenNewAsync()
    {
        var mix = new MixTrackView(); mix.Show();
        try { await mix.OpenProjectAsync(); }
        catch (OperationCanceledException) { }
        finally { if (!mix.IsDisposed && !mix.HasLoadedProject) mix.Dispose(); }
    }
}
