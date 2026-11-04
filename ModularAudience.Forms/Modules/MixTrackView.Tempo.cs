using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private bool tempoChangePending;
    private void SetTempoControl(double value)
    {
        restoring = true;
        try { bpm.Value = (decimal)value; }
        finally { restoring = false; }
    }
    private async Task RequestMixTempoChangeAsync(double target)
    {
        double initial = Arrangement.Bpm;
        if (tempoChangePending || busy || closing) { SetTempoControl(initial); return; }
        if (Math.Abs(initial - target) < 0.0001) { SetTempoControl(initial); return; }
        tempoChangePending = true;
        try
        {
            bool hasAudio = Arrangement.Lanes.Any(l => l.Clips.Count > 0);
            var answer = hasAudio ? MessageBox.Show(this,
                $"Change mix tempo from {initial:0.##} to {target:0.##} BPM?\n\nYes: reimport/time-stretch existing audio to the new tempo.\nNo: change the beat grid only.\nCancel: keep the current tempo.",
                "Mix tempo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) : DialogResult.No;
            if (answer == DialogResult.Cancel) { SetTempoControl(initial); return; }
            if (answer == DialogResult.Yes)
            {
                var ids = Arrangement.Lanes.SelectMany(l => l.Clips).Select(c => c.SourceId).Distinct().ToArray();
                var sources = ids.Select(id => Arrangement.Sources[id]).ToArray();
                // This dialog collects common algorithm settings, not a common initial BPM.
                using var dummy = new AudioObj { Name = "All mix tracks", Data = [0f], Channels = 1, SampleRate = 44100, Bpm = (float)initial };
                using var dialog = new TimeStretchDialog(null, [dummy]) { IsConfigureMode = true };
                dialog.SetConfigurationTempo((float)initial, (float)target);
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ConfirmedSettings == null) { SetTempoControl(initial); return; }
                target = dialog.ConfirmedSettings.TargetBpm;
                var settings = dialog.ConfirmedSettings;
                var prepared = new Dictionary<Guid, MixSource>();
                bool completed = false;
                liveUpdateCancellation?.Cancel();
                await RunAsync(async token =>
                {
                    foreach (var source in sources)
                    {
                        token.ThrowIfCancellationRequested();
                        // Sources without tagged tempo are treated as being at the old grid BPM.
                        var input = new MixSource { Name = source.Name, Data = source.Data, Channels = source.Channels,
                            SampleRate = source.SampleRate, Bpm = source.Bpm > 0 && float.IsFinite(source.Bpm) ? source.Bpm : (float)initial };
                        status.Text = $"Time-stretching {source.Name}: {input.Bpm:0.##} → {target:0.##} BPM…";
                        prepared[source.Id] = await MixRenderer.PrepareSourceAsync(input, target, ClipTempoMode.TimeStretch, token, settings);
                    }
                    token.ThrowIfCancellationRequested();
                    ApplyPreparedTempo(initial, target, prepared);
                    completed = true;
                });
                if (!completed) { SetTempoControl(initial); return; }
            }
            else Arrangement.Bpm = target;
            SetTempoControl(Arrangement.Bpm);
            Changed();
        }
        finally { tempoChangePending = false; }
    }
    private void ApplyPreparedTempo(double initial, double target, IReadOnlyDictionary<Guid, MixSource> prepared)
    {
        foreach (var pair in prepared) Arrangement.Sources.Add(pair.Value.Id, pair.Value);
        foreach (var clip in Arrangement.Lanes.SelectMany(l => l.Clips))
        {
            if (!prepared.TryGetValue(clip.SourceId, out var replacement)) continue;
            var old = Arrangement.Sources[clip.SourceId];
            double sourceBpm = old.Bpm > 0 && float.IsFinite(old.Bpm) ? old.Bpm : initial;
            double ratio = sourceBpm / target;
            clip.SourceOffset *= ratio;
            clip.SourceDuration = Math.Min(clip.SourceDuration * ratio, Math.Max(0, replacement.Duration - clip.SourceOffset));
            clip.Start *= initial / target;
            clip.FadeIn *= initial / target; clip.FadeOut *= initial / target;
            clip.SourceId = replacement.Id; clip.TempoMode = ClipTempoMode.Original;
        }
        Arrangement.LoopStart *= initial / target; Arrangement.LoopEnd *= initial / target;
        caret *= initial / target;
        Arrangement.Bpm = target;
    }
}
