using ModularAudience.Audio;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternEditorDialog
{
    internal async Task<IReadOnlyList<BreakbeatPatternMixTrack>> RenderTracksForMixAsync(
        CancellationToken cancellationToken = default)
    {
        BreakbeatPatternNote[] allNotes = this.ApplyTrackAudibility(
            this.GetNotesWithoutCoveredRetriggers());
        IReadOnlyList<AudioObj> samples = this.samples.ToArray();
        IReadOnlyList<BreakbeatTrackSettings> settings = this.trackSettings
            .Select(NormalizeTrackSettings)
            .ToArray();
        var result = new List<BreakbeatPatternMixTrack>();
        try
        {
            var instrumentSettings = this.GetInstrumentSettingsByTrack();
            for (int track = 0; track < samples.Count; track++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BreakbeatPatternNote[] trackNotes = allNotes
                    .Where(note => note.TrackIndex == track)
                    .ToArray();
                if (trackNotes.Length == 0)
                {
                    continue;
                }

                AudioObj? rendered = await BreakbeatPatternInstrumentRenderer.RenderAsync(
                    trackNotes,
                    samples,
                    instrumentSettings,
                    settings,
                    this.bars,
                    (float)this.Bpm,
                    this.currentResolution,
                    this.swing,
                    "Pattern Editor Track",
                    cancellationToken,
                    this.instrumentSampleCache,
                    this.noteRenderCache);
                if (rendered is null)
                {
                    continue;
                }

                string name = track < this.rowLabels.Count
                    ? this.rowLabels[track]
                    : $"Pattern track {track + 1}";
                rendered.Rename(name);
                result.Add(new BreakbeatPatternMixTrack(track, name, rendered));
            }

            return result;
        }
        catch
        {
            foreach (BreakbeatPatternMixTrack track in result)
            {
                track.Audio.Dispose();
            }
            throw;
        }
    }
}

internal sealed record BreakbeatPatternMixTrack(int TrackIndex, string Name, AudioObj Audio);
