using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Generators;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class BreakbeatPatternNameTemplateTests
{
    [TestMethod]
    public void Format_RepeatsNoteGroupInTrackAndTimeOrder_AndFormatsBpmOnce()
    {
        BreakbeatPatternNameTemplate template = Parse("{nnn_ppp_d }[{bbb}]");
        BreakbeatPatternNote[] notes =
        [
            new(1, 256, 256, PitchSemitones: -3),
            new(0, 0, 512),
            new(1, 0, 256, PitchSemitones: -4)
        ];

        string result = template.Format(notes, ["audio", "snare"], ticksPerCell: 256, bpm: 100m);

        Assert.AreEqual("aud_0_2 sna_-4_1 sna_-3_1 [100]", result);
    }

    [TestMethod]
    public void Format_UsesUnicodePitchFractionsAndKeepsSignificantBpmDecimals()
    {
        BreakbeatPatternNameTemplate template = Parse("{ppp}[{bbbb}]");
        BreakbeatPatternNote note = new(0, 0, 64, PitchSemitones: 3.25f);

        string result = template.Format([note], ["sample"], ticksPerCell: 64, bpm: 100.5m);

        Assert.AreEqual("+3¼[100.5]", result);
    }

    [TestMethod]
    public void Format_PitchTokenWidthsReturnSignThenWholePitchThenFullPitch()
    {
        BreakbeatPatternNameTemplate template = Parse("{p_pp_ppp}");
        BreakbeatPatternNote note = new(0, 0, 64, PitchSemitones: -3.25f);

        string result = template.Format([note], ["sample"], ticksPerCell: 64, bpm: 100m);

        Assert.AreEqual("-_-3_-3¼", result);
    }

    [TestMethod]
    public void Format_UsesSlashFractionAndExactFractionalCellDurationWhenNoGlyphMatches()
    {
        BreakbeatPatternNameTemplate template = Parse("{ppp_d}");
        BreakbeatPatternNote note = new(0, 0, 96, PitchSemitones: 3.13f);

        string result = template.Format([note], ["sample"], ticksPerCell: 64, bpm: 100m);

        Assert.AreEqual("+3 13/100_1.5", result);
    }

    [TestMethod]
    [DataRow("{nnn_ppp_d")]
    [DataRow("nnn}")]
    [DataRow("{nnn_x_d}")]
    [DataRow("{pppp}")]
    [DataRow("{nnn_bbb}")]
    [DataRow("{nnn}{ppp}")]
    [DataRow("{{nnn}}")]
    public void TryParse_RejectsMalformedOrUnsupportedVariables(string text)
    {
        Assert.IsFalse(BreakbeatPatternNameTemplate.TryParse(text, out _, out string error));
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void RecordSuccessfulTemplate_MovesSelectedSchemaToFrontAndCapsHistory()
    {
        BreakbeatPatternEditorNameSettings settings = new();
        settings.RecordSuccessfulTemplate("first");
        settings.RecordSuccessfulTemplate("second");
        settings.RecordSuccessfulTemplate("first");

        CollectionAssert.AreEqual(new[] { "first", "second" }, settings.RecentTemplates);

        for (int index = 0; index < 20; index++)
        {
            settings.RecordSuccessfulTemplate($"template-{index}");
        }

        Assert.AreEqual(BreakbeatPatternEditorNameSettings.MaximumRecentTemplates, settings.RecentTemplates.Count);
        Assert.AreEqual("template-19", settings.RecentTemplates[0]);
    }

    [TestMethod]
    public void Settings_SaveAndLoad_PersistCurrentTextAndRecentTemplates()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ModularAudienceTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        BreakbeatPatternEditorNameSettings settings = new()
        {
            Template = "{nnn_ppp_d }[{bbb}]"
        };
        settings.RecordSuccessfulTemplate(settings.Template);
        settings.Template = "{invalid";

        try
        {
            settings.Save(path);

            BreakbeatPatternEditorNameSettings loaded = BreakbeatPatternEditorNameSettings.Load(path);

            Assert.AreEqual(settings.Template, loaded.Template);
            CollectionAssert.AreEqual(settings.RecentTemplates, loaded.RecentTemplates);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static BreakbeatPatternNameTemplate Parse(string text)
    {
        if (!BreakbeatPatternNameTemplate.TryParse(text, out BreakbeatPatternNameTemplate? template, out string error))
        {
            Assert.Fail(error);
        }

        return template!;
    }
}
