using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private bool collectionImportPending;

    /// <summary>Open a frozen collection selection: first track sets the grid, all dialogs precede all remaining DSP work.</summary>
    internal async Task JoinCollectionSelectionAsync(IEnumerable<AudioObj> audios)
    {
        if (collectionImportPending || busy || closing || released || this.windowGroup.IsBlocked) { return; }
        if (Arrangement.Lanes.Any(lane => lane.Clips.Count > 0))
            throw new InvalidOperationException("A collection selection must open in a new mix.");
        AudioObj[] selected = audios.DistinctBy(audio => audio.Id).ToArray();
        if (selected.Length == 0) { return; }
        if (selected.Any(audio => audio.IsDisposed || audio.Data.Length == 0 || audio.SampleRate <= 0 || audio.Channels <= 0))
            throw new InvalidOperationException("A selected track no longer has available audio samples.");

        collectionImportPending = true;
        List<TrackView> pending = [];
        try
        {
            TrackView CreateView(AudioObj audio)
            {
                AudioCollection? collection = WindowMain.CollectionViews
                    .FirstOrDefault(view => !view.IsDisposed && !view.Disposing && view.AudioC.Audios.Contains(audio))?.AudioC;
                var view = new TrackView(audio, collection, showOnCreate: false);
                pending.Add(view);
                return view;
            }

            TrackView first = CreateView(selected[0]);
            await this.JoinAsync(first);
            if (closing || released || !ReferenceEquals(first.JoinedMix, this)) { return; }
            pending.Remove(first); // Already committed; cancellation leaves this first lane in place.

            // TrackView snapshots preserve the selected input even if a bag changes during
            // mix-local modeless dialogs. They also retain the existing Join/Detach lifecycle.
            foreach (AudioObj audio in selected.Skip(1)) { CreateView(audio); }
            List<(TrackView View, PlaylistStretchSettings? Settings)> plans = [];
            foreach (TrackView view in pending)
            {
                AudioObj audio = view.OriginalAudio;
                float sourceBpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm;
                PlaylistStretchSettings? settings = null;
                if (float.IsFinite(sourceBpm) && sourceBpm > 0 && Math.Abs(sourceBpm - Arrangement.Bpm) > 0.1)
                {
                    status.Text = $"Configure {audio.Name}: {sourceBpm:0.##} → {Arrangement.Bpm:0.##} BPM…";
                    settings = await this.ConfigureImportStretchAsync(audio, matchMixTempo: true);
                    if (closing || released) { return; }
                    if (settings == null) { status.Text = "Import cancelled — first track retained."; return; }
                }
                plans.Add((view, settings));
            }
            if (plans.Count == 0) { return; }

            using var staged = new MixArrangement { Bpm = Arrangement.Bpm };
            List<(TrackView View, MixSource Source)> prepared = [];
            bool completed = false;
            await this.RunAsync(async token =>
            {
                foreach (var plan in plans)
                {
                    token.ThrowIfCancellationRequested();
                    if (closing || released || plan.View.IsDisposed) { return; }
                    AudioObj audio = plan.View.OriginalAudio;
                    var original = new MixSource
                    {
                        Name = audio.Name, Data = (float[])audio.Data.Clone(), SampleRate = audio.SampleRate,
                        Channels = audio.Channels, Bpm = audio.Bpm > 0 ? audio.Bpm : audio.ScannedBpm
                    };
                    staged.Sources.Add(original.Id, original);
                    MixSource source = original;
                    if (plan.Settings != null)
                    {
                        status.Text = $"Preparing {prepared.Count + 2}/{selected.Length}: {audio.Name} → {Arrangement.Bpm:0.##} BPM…";
                        source = await MixTempoOrigins.PrepareAsync(staged, original, Arrangement.Bpm,
                            Arrangement.Bpm, ClipTempoMode.TimeStretch, plan.Settings, token);
                        staged.Sources.Add(source.Id, source);
                    }
                    prepared.Add((plan.View, source));
                }
                token.ThrowIfCancellationRequested();
                if (closing || released || prepared.Any(item => item.View.IsDisposed)) { return; }
                foreach (var source in staged.Sources) { Arrangement.Sources.Add(source.Key, source.Value); }
                foreach (var item in prepared) { this.AttachJoinedTrack(item.View, item.Source); pending.Remove(item.View); }
                completed = true;
            });
            if (completed && !closing && !released)
            {
                this.FinishJoiningTracks();
                status.Text = $"Joined {selected.Length} tracks at {Arrangement.Bpm:0.##} BPM.";
            }
        }
        finally
        {
            // Uncommitted hidden windows must never reappear after Cancel, failure or mix close.
            foreach (TrackView view in pending) { if (!view.IsDisposed) { view.Close(); } }
            collectionImportPending = false;
        }
    }
}
