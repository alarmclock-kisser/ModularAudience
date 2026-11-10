using System.Text.Json;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternEditorDialog
{
    private static readonly string TrackNameFontPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience", "BreakbeatTrackNameFont.json");

    private float trackNameFontSize = 10f;

    private void LoadTrackNameFontSize()
    {
        try
        {
            if (File.Exists(TrackNameFontPath))
            {
                var doc = JsonSerializer.Deserialize<Dictionary<string, float>>(File.ReadAllText(TrackNameFontPath));
                if (doc != null && doc.TryGetValue("FontSize", out float v) && v is >= 6f and <= 24f)
                    trackNameFontSize = v;
            }
        }
        catch { }
    }

    private void SaveTrackNameFontSize()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TrackNameFontPath)!);
            File.WriteAllText(TrackNameFontPath, JsonSerializer.Serialize(new Dictionary<string, float> { ["FontSize"] = trackNameFontSize }));
        }
        catch { }
    }

    private void CycleTrackNameFontSize(int delta)
    {
        float[] sizes = { 6f, 7f, 8f, 9f, 10f, 11f, 12f, 14f, 16f };
        int idx = Array.FindIndex(sizes, s => Math.Abs(s - trackNameFontSize) < 0.5f);
        if (idx < 0) idx = Array.FindIndex(sizes, s => s >= trackNameFontSize);
        if (idx < 0) idx = sizes.Length - 1;
        idx = Math.Clamp(idx + (delta > 0 ? 1 : -1), 0, sizes.Length - 1);
        trackNameFontSize = sizes[idx];
        SaveTrackNameFontSize();
        this.pictureBox_pattern.Invalidate();
    }
}