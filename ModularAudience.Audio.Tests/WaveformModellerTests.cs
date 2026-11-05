using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class WaveformModellerTests
{
    [TestMethod]
    [DataRow("sin(τ*x)", 0.25, 1)]
    [DataRow("2(x+1)", 0.5, 3)]
    [DataRow("-2^2", 0, -4)]
    [DataRow("2^3^2", 0, 512)]
    [DataRow("clamp(2*x,-1,1)", 0.75, 1)]
    [DataRow("log10(100)+sign(-x)", 0.5, 1)]
    public void ParsesMathematicalNotation(string text, double x, double expected)
    { Assert.AreEqual(expected, WaveExpression.Compile(text)(x), 1e-8); }

    [TestMethod]
    public void RejectsUnknownCodeAndInvalidWaveforms()
    {
        Assert.Throws<FormatException>(() => WaveExpression.Compile("File.Delete(x)"));
        Assert.Throws<FormatException>(() => WaveExpression.Compile("sin(x"));
        Assert.Throws<ArgumentException>(() => WaveformModel.BuildCycle(new() { Equation = "1/0" }));
        Assert.Throws<FormatException>(() => WaveExpression.Compile(new string('-', 200) + "x"));
    }
    [TestMethod]
    public void DrawingFourierEquationReconstructsOriginalCycle()
    {
        var cycle = Enumerable.Range(0, 2048).Select(i => 0.6 * Math.Sin(2 * Math.PI * i / 2048) + 0.2 * Math.Cos(6 * Math.PI * i / 2048)).ToArray();
        string equation = WaveformModel.FourierEquation(cycle, 8);
        var parsed = WaveExpression.Compile(equation);
        for (int i = 0; i < cycle.Length; i++) Assert.AreEqual(cycle[i], parsed(i / 2048.0), 1e-7);
    }
    [TestMethod]
    public void RendersRequestedLengthChannelsPitchAndEnvelope()
    {
        using var audio = WaveformModel.Render(new() { Frequency = 440, Duration = 0.5, SampleRate = 48000, Channels = 3, Gain = 0.4 });
        Assert.AreEqual(72000, audio.Data.Length); Assert.AreEqual(0.5, audio.Duration.TotalSeconds);
        Assert.IsTrue(audio.Data.Max(Math.Abs) <= 0.401f);
        Assert.AreEqual(0f, audio.Data[0]); Assert.AreEqual(0f, audio.Data[^1]);
        int crosses = 0;
        for (int i = 1; i < 24000; i++) if (audio.Data[(i - 1) * 3] < 0 && audio.Data[i * 3] >= 0) crosses++;
        Assert.AreEqual(220, crosses, 2);
    }
    [TestMethod]
    public void LiveUpdateDoesNotRestartPhaseOrCreateUnboundedSamples()
    {
        var settings = new WaveformSettings();
        var table = WaveformModel.BandLimitedTable(WaveformModel.BuildCycle(settings), 220, 44100);
        var live = new WaveformLiveProvider(table, 220, 0.2);
        float[] buffer = new float[4096]; live.Read(buffer);
        live.Update(table.Select(v => -v).ToArray(), 330, 0.3); live.Read(buffer);
        Assert.IsTrue(buffer.All(v => float.IsFinite(v) && Math.Abs(v) <= 0.301f));
        Assert.IsTrue(buffer.Zip(buffer.Skip(2)).Max(pair => Math.Abs(pair.First - pair.Second)) < 0.05f);
    }
    [TestMethod]
    public void PresetRoundTripPreservesEquationAndDrawing()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var model = new WaveformSettings { Equation = "0.5*sin(tau*x)", Frequency = 123, Duration = 2.5,
                DrawnCycle = Enumerable.Range(0, 2048).Select(i => Math.Sin(2 * Math.PI * i / 2048)).ToArray() };
            model.Save(path); var loaded = WaveformSettings.Load(path);
            Assert.AreEqual(model.Equation, loaded.Equation); Assert.AreEqual(123d, loaded.Frequency);
            CollectionAssert.AreEqual(model.DrawnCycle, loaded.DrawnCycle);
        }
        finally { File.Delete(path); }
    }
    [STATestMethod]
    public void ModellerConstructsAndPlotsWithoutAudioDevice()
    {
        using var modeller = new WaveformModeller();
        _ = modeller.Handle;
        using var image = new System.Drawing.Bitmap(modeller.Width, modeller.Height);
        modeller.DrawToBitmap(image, modeller.ClientRectangle);
    }

    [TestMethod]
    public void BandLimitingRemovesAboveNyquistHarmonicsAndRenderCanCancel()
    {
        // The equation itself exceeds unity, so construct the bounded cycle explicitly here.
        double[] cycle = Enumerable.Range(0, 2048).Select(i => 0.6 * Math.Sin(2 * Math.PI * i / 2048)
            + 0.3 * Math.Sin(20 * Math.PI * i / 2048)).ToArray();
        float[] table = WaveformModel.BandLimitedTable(cycle, 5000, 44100);
        double harmonic = 0;
        for (int i = 0; i < table.Length; i++) harmonic += table[i] * Math.Sin(20 * Math.PI * i / table.Length);
        Assert.IsTrue(Math.Abs(harmonic / table.Length * 2) < 1e-6);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => WaveformModel.Render(new(), cts.Token));
    }
}
