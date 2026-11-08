using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternEditorDialog
{
    private void GeneratePresetRow(int row, int rowStart, int rowEnd, int colStart, int colEnd,
        BreakbeatGenerateOptions options, BreakbeatTrackPreset preset, Random random, List<BreakbeatPatternNote> placed)
    {
        int minimumDuration = BreakbeatGenerator_V2.GetMinimumNoteDurationTicks(this.samples, row, (float)this.Bpm, this.currentResolution);
        foreach (var hit in BreakbeatTrackPresets.Generate(preset, options, colStart, colEnd, this.currentResolution, random))
        {
            int end = (colEnd + 1) * this.GetTicksPerStep();
            if (!hit.Kinds.AllowTruncated && !hit.Kinds.AllowTimeStretch && !hit.Kinds.AllowVarispeed
                && minimumDuration > end - hit.Column * this.GetTicksPerStep()) continue;
            int before = placed.Count;
            if (!this.TryPlaceGeneratedNote(row, hit.Column, hit.Length, hit.Pitch, hit.Volume,
                minimumDuration, hit.Kinds, placed, rowStart, rowEnd)) continue;
            // A plain sample may be longer than the requested gate. Never silently trim it when trimming is forbidden.
            var note = placed[before];
            if ((long)note.StartTick + note.DurationTicks > end)
            {
                this.notes.Remove(note); placed.RemoveAt(before);
                if (!options.AllowTruncated && !note.TimeStretch && !note.Varispeed) continue;
                var bounded = note with { DurationTicks = end - note.StartTick, HardResized = true };
                this.notes.Add(bounded); placed.Add(bounded);
            }
        }
    }
}
