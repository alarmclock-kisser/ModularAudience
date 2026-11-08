using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SampleModellerTests
{
    private static AudioObj Sample(int channels = 2) => new()
    {
        Name = "Original", Data = Enumerable.Range(0, 4800).SelectMany(i => Enumerable.Range(0, channels)
            .Select(c => (float)(0.25 * Math.Sin(2 * Math.PI * (c == 0 ? 440 : 880) * i / 48000)))).ToArray(),
        SampleRate = 48000, Channels = channels
    };
    [TestMethod]
    public void EveryEffectProducesFiniteChannelAlignedOutputWithoutMutatingSource()
    {
        using var sample = Sample(); var before = (float[])sample.Data.Clone();
        foreach (var kind in Enum.GetValues<SampleEffectKind>())
        {
            var effect = SampleEffect.Create(kind);
            if (kind is SampleEffectKind.DrawWaveform or SampleEffectKind.VolumeEnvelope)
                effect.Points = [new(0, 0), new(0.5, 1), new(1, 0)];
            using var result = SampleModellerDsp.Render(sample, new() { Effects = [effect] });
            Assert.IsTrue(result.Data.Length > 0 && result.Data.Length % 2 == 0, kind.ToString());
            Assert.IsTrue(result.Data.All(float.IsFinite), kind.ToString());
            CollectionAssert.AreEqual(before, sample.Data, kind.ToString());
        }
    }
    [TestMethod]
    public void GainRegionTrimReverseAndDrawRespectSignalAndChannels()
    {
        using var sample = new AudioObj { Data = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f], SampleRate = 8000, Channels = 2 };
        using var reversed = SampleModellerDsp.Render(sample, new() { Effects = [SampleEffect.Create(SampleEffectKind.Reverse)] });
        CollectionAssert.AreEqual(new[] { 0.7f, 0.8f, 0.5f, 0.6f, 0.3f, 0.4f, 0.1f, 0.2f }, reversed.Data);
        var gain = SampleEffect.Create(SampleEffectKind.Gain); gain.GainDb = 20 * Math.Log10(2); gain.StartPercent = 25; gain.EndPercent = 75;
        using var amplified = SampleModellerDsp.Render(sample, new() { Effects = [gain] });
        Assert.AreEqual(0.1f, amplified.Data[0]); Assert.AreEqual(0.6f, amplified.Data[2], 0.00001f); Assert.AreEqual(0.8f, amplified.Data[7]);
        var trim = SampleEffect.Create(SampleEffectKind.Trim); trim.StartPercent = 25; trim.EndPercent = 75;
        using var trimmed = SampleModellerDsp.Render(sample, new() { Effects = [trim] });
        CollectionAssert.AreEqual(sample.Data[2..6], trimmed.Data);
        var draw = SampleEffect.Create(SampleEffectKind.DrawWaveform); draw.Channel = 1; draw.Points = [new(0, -0.5), new(1, 0.5)];
        using var drawn = SampleModellerDsp.Render(sample, new() { Effects = [draw] });
        Assert.AreEqual(-0.5f, drawn.Data[0]); Assert.AreEqual(0.5f, drawn.Data[6]);
        for (int i = 1; i < sample.Data.Length; i += 2) Assert.AreEqual(sample.Data[i], drawn.Data[i]);
    }
    [TestMethod]
    public void FiltersAttenuateTheCorrectFrequencyAndTimeToolsHaveExactDuration()
    {
        using var sample = Sample(1);
        var low = SampleEffect.Create(SampleEffectKind.LowPass); low.FrequencyHz = 80;
        using var filtered = SampleModellerDsp.Render(sample, new() { Effects = [low] });
        double Energy(float[] data) => data.Skip(1000).Sum(v => v * (double)v);
        Assert.IsTrue(Energy(filtered.Data) < Energy(sample.Data) * 0.02);
        var stretch = SampleEffect.Create(SampleEffectKind.TimeStretch); stretch.Factor = 2;
        using var stretched = SampleModellerDsp.Render(sample, new() { Effects = [stretch] });
        Assert.AreEqual(sample.Data.Length * 2, stretched.Data.Length);
        var speed = SampleEffect.Create(SampleEffectKind.Varispeed); speed.Factor = 2;
        using var sped = SampleModellerDsp.Render(sample, new() { Effects = [speed] });
        Assert.AreEqual(sample.Data.Length / 2, sped.Data.Length);
        using var pitched = SampleModellerDsp.Render(sample, new() { Effects = [SampleEffect.Create(SampleEffectKind.PitchShift)] });
        Assert.AreEqual(sample.Data.Length, pitched.Data.Length);
    }
    [TestMethod]
    public void DisabledAndDryEffectsAreTransparentAndCancellationStopsProcessing()
    {
        using var sample = Sample();
        var effect = SampleEffect.Create(SampleEffectKind.Gain); effect.Enabled = false;
        using var disabled = SampleModellerDsp.Render(sample, new() { Effects = [effect] });
        CollectionAssert.AreEqual(sample.Data, disabled.Data);
        effect.Enabled = true; effect.MixPercent = 0;
        using var dry = SampleModellerDsp.Render(sample, new() { Effects = [effect] });
        CollectionAssert.AreEqual(sample.Data, dry.Data);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => SampleModellerDsp.Render(sample, new() { Effects = [effect] }, cts.Token));
    }
    [TestMethod]
    public void PresetsRoundTripEditDeleteAndRejectInvalidData()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sample-modeller-" + Guid.NewGuid().ToString("N"));
        var store = new SampleModellerPresetStore(directory);
        try
        {
            var settings = new SampleModellerSettings { Effects = [SampleEffect.Create(SampleEffectKind.PeakEq)] };
            store.Save("Test", settings); Assert.HasCount(1, store.Names());
            var loaded = store.Load("Test"); Assert.AreEqual(6d, loaded.Effects[0].GainDb);
            loaded.Effects[0].GainDb = -8; store.Save("Test", loaded);
            Assert.AreEqual(-8d, store.Load("Test").Effects[0].GainDb);
            store.Delete("Test"); Assert.HasCount(0, store.Names());
            Assert.ThrowsExactly<ArgumentException>(() => store.Save("../bad", settings));
            settings.Effects[0].GainDb = double.NaN;
            Assert.ThrowsExactly<ArgumentException>(() => settings.Validate());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [STATestMethod]
    public void PreviewProviderLoopsSelectedFramesAndAcceptsUpdatedAudio()
    {
        Type type = typeof(SampleWaveformModeller).GetNestedType("PreviewProvider", BindingFlags.NonPublic)!;
        var provider = (NAudio.Wave.ISampleProvider)Activator.CreateInstance(type, [1000, 2])!;
        type.GetProperty("Loop")!.SetValue(provider, true);
        type.GetMethod("Update")!.Invoke(provider, [new float[] { 0, 0, 0.4f, -0.4f, 0.4f, -0.4f, 0, 0 }, 1, 3]);
        var buffer = new float[64]; Assert.AreEqual(buffer.Length, provider.Read(buffer.AsSpan()));
        Assert.IsTrue(buffer.Where((_, i) => i % 2 == 0).All(v => v > 0));
        Assert.IsTrue(buffer.Where((_, i) => i % 2 == 1).All(v => v < 0));
        Assert.IsFalse((bool)type.GetProperty("Finished")!.GetValue(provider)!);
        type.GetProperty("Loop")!.SetValue(provider, false);
        provider.Read(buffer.AsSpan());
        Assert.IsTrue((bool)type.GetProperty("Finished")!.GetValue(provider)!);
        type.GetMethod("Update")!.Invoke(provider, [new float[8], 0, 4]);
        provider.Read(buffer.AsSpan()); Assert.IsTrue(buffer.All(v => v == 0));
    }

    [STATestMethod]
    public void EditorIsNonModalHistoryRestoresEffectsAndSaveCopiesOrReplacesOnlyItsSample()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var sample = Sample(); sample.Rename("Original");
        using var view = new AudioCollectionView([sample], "Modeller test");
        var audios = ((AudioCollection)typeof(AudioCollectionView).GetField("AudioC", flags)!.GetValue(view)!).Audios;
        using var editor = new SampleWaveformModeller(view, sample);
        object Field(string name) => typeof(SampleWaveformModeller).GetField(name, flags)!.GetValue(editor)!;
        object? Call(string name, params object[] args) => typeof(SampleWaveformModeller).GetMethod(name, flags)!.Invoke(editor, args);
        try
        {
            var menu = (ContextMenuStrip)typeof(AudioCollectionView).GetField("contextMenuStrip_audios", flags)!.GetValue(view)!;
            int index = menu.Items.IndexOfKey("menuToolStripItem_timeStretchSelected");
            Assert.AreEqual("Waveform Modeller", menu.Items[index + 1].Text);
            editor.Show(view); Assert.IsFalse(editor.Modal); Assert.IsTrue(view.Enabled);
            var settings = (SampleModellerSettings)Field("settings");
            settings.Effects.Add(SampleEffect.Create(SampleEffectKind.Invert)); Call("Commit");
            Call("History", false); Assert.HasCount(0, ((SampleModellerSettings)Field("settings")).Effects);
            Call("History", true); Assert.HasCount(1, ((SampleModellerSettings)Field("settings")).Effects);
            var plot = (Control)Field("plot");
            ((ComboBox)Field("gesture")).SelectedIndex = 1;
            Call("BeginStroke", plot, new MouseEventArgs(MouseButtons.Left, 1, plot.Width / 4, plot.Height / 4, 0));
            Call("ContinueStroke", plot, new MouseEventArgs(MouseButtons.Left, 1, plot.Width / 2, plot.Height / 8, 0));
            Call("EndStroke");
            Assert.HasCount(2, ((SampleModellerSettings)Field("settings")).Effects);
            Assert.IsTrue(((SampleModellerSettings)Field("settings")).Effects[1].Points.Count >= 2);
            Call("History", false); Assert.HasCount(1, ((SampleModellerSettings)Field("settings")).Effects);
            var timer = (System.Windows.Forms.Timer)Field("debounce"); timer.Stop();
            Task task = (Task)Call("RenderLatestAsync")!;
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(2); }
            task.GetAwaiter().GetResult();
            Assert.IsNotNull(typeof(SampleWaveformModeller).GetField("rendered", flags)!.GetValue(editor), ((Label)Field("status")).Text);
            var before = (float[])sample.Data.Clone();
            Call("CommitToCollection", true);
            Assert.HasCount(2, audios); CollectionAssert.AreEqual(before, sample.Data);
            Assert.IsTrue(audios[1].Name.Contains("Modelled")); Assert.IsFalse(editor.IsDisposed);
            Call("CommitToCollection", true); Assert.AreNotEqual(audios[1].Name, audios[2].Name);
            Call("CommitToCollection", false);
            Assert.AreEqual("Original", sample.Name);
            for (int i = 0; i < before.Length; i++) Assert.AreEqual(-before[i], sample.Data[i]);
            task = (Task)Call("SaveAsync", false)!;
            while (!task.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(2); }
            task.GetAwaiter().GetResult();
            Assert.IsTrue(editor.IsDisposed, "Normal Save must close the modeless editor.");
        }
        finally
        {
            var views = (System.Collections.IList)typeof(WindowMain).GetField("CollectionViews", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            views.Remove(view);
        }
    }
}
