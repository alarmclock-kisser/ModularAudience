using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private void ShowRenderMenu(Control button, Point point)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Mix (one track)", null, async (_, _) => await ExportAsync());
        menu.Items.Add("Separate (per track)", null, async (_, _) => await ExportTracksAsync(null));
        var track = new ToolStripMenuItem("Render Track #");
        var tracks = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        for (int i = 0; i < Arrangement.Lanes.Count; i++) tracks.Items.Add(new LaneItem(Arrangement.Lanes[i].Id, $"{i + 1}. {Arrangement.Lanes[i].Name}"));
        tracks.SelectedIndexChanged += async (_, _) =>
        { if (tracks.SelectedItem is LaneItem item) { menu.Close(); await ExportTracksAsync(item.Id); } };
        track.DropDownItems.Add(tracks); menu.Items.Add(track);
        int count = SelectedParts().Count;
        if (count > 0) menu.Items.Add($"Selection ({count})", null, async (_, _) => await ExportSelectionAsync());
        menu.Closed += (_, _) => { if (!IsDisposed) BeginInvoke((Action)menu.Dispose); };
        menu.Show(button, point);
    }
    private MixArrangement RenderSnapshot()
    {
        var snapshot = new MixArrangement(); snapshot.Restore(Arrangement.Capture());
        foreach (var source in Arrangement.Sources) snapshot.Sources.Add(source.Key, source.Value);
        return snapshot;
    }
    private MixArrangement SelectionRenderSnapshot()
    {
        var selected = RenderSnapshot(); var parts = SelectedParts();
        selected.Lanes.Clear();
        if (parts.Count == 0) return selected;
        double first = parts.Min(p => p.Clip.Start);
        foreach (var group in parts.GroupBy(p => p.Row))
        {
            var original = Arrangement.Lanes[group.Key];
            var lane = new MixLane { Name = original.Name, Gain = original.Gain, AutoCrossfade = original.AutoCrossfade };
            foreach (var part in group) { var copy = CloneClip(part.Clip); copy.Start -= first; lane.Clips.Add(copy); }
            selected.Lanes.Add(lane);
        }
        return selected;
    }
    private async Task ExportSelectionAsync()
    {
        if (busy || SelectedParts().Count == 0) return;
        using var snapshot = SelectionRenderSnapshot();
        await RunAsync(async token =>
        {
            var audio = await MixRenderer.RenderAsync(snapshot, token); audio.Rename(Arrangement.Name + " - Selection");
            if (closing || released || token.IsCancellationRequested) { audio.Dispose(); return; }
            new AudioCollectionView([audio], audio.Name).Show();
        });
    }
    private async Task ExportTracksAsync(Guid? laneId)
    {
        if (busy) return;
        using var snapshot = RenderSnapshot();
        var lanes = snapshot.Lanes.Where(l => laneId == null || l.Id == laneId).ToArray();
        await RunAsync(async token =>
        {
            var outputs = new List<AudioObj>();
            try
            {
                foreach (var lane in lanes)
                {
                    token.ThrowIfCancellationRequested();
                    snapshot.Lanes = [lane]; lane.Mute = lane.Solo = false;
                    var audio = await MixRenderer.RenderAsync(snapshot, token); audio.Rename(Arrangement.Name + " - " + lane.Name); outputs.Add(audio);
                }
                if (closing || released || token.IsCancellationRequested) return;
                new AudioCollectionView(outputs, Arrangement.Name + " - Tracks").Show(); outputs.Clear();
            }
            finally { foreach (var audio in outputs) audio.Dispose(); }
        });
    }
}
