using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using System.Runtime.InteropServices;
using ModularAudience.Generators;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class BreakbeatPatternWindowTests
{
    [STATestMethod]
    public void TrackButtonsSupportMultipleSolosMutePriorityClearAndUndo()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = new BreakbeatPatternEditorDialog([new bool[16], new bool[16], new bool[16]], [sample, sample, sample],
            ["One", "Two", "Three"], 1, 16, 0, 120, [new(0, 0, 64), new(1, 0, 64), new(2, 0, 64)], initialGridResolution: 16);
        Type type = typeof(BreakbeatPatternEditorDialog);
        object? Call(string name, params object[] args) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, args);
        void Click(int row, int button)
        {
            var bounds = (Rectangle)Call("TrackButtonBounds", row, button)!;
            Assert.AreEqual(true, Call("TryClickTrackButtons", new MouseEventArgs(MouseButtons.Left, 1, bounds.X + 1, bounds.Y + 1, 0)));
        }
        bool Audible(int row) => (bool)Call("IsTrackAudible", row)!;
        Assert.IsTrue(Audible(0) && Audible(1) && Audible(2));
        Click(0, 0); Assert.IsTrue(Audible(0)); Assert.IsFalse(Audible(1));
        Click(1, 0); Assert.IsTrue(Audible(0) && Audible(1)); Assert.IsFalse(Audible(2));
        Click(0, 1); Assert.IsFalse(Audible(0), "Mute must override Solo."); Assert.IsTrue(Audible(1));
        Click(1, 2);
        var notes = (List<BreakbeatPatternNote>)type.GetField("notes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        Assert.IsFalse(notes.Any(n => n.TrackIndex == 1));
        Assert.AreEqual(2, notes.Count);
        Call("UndoEditorAction"); Assert.AreEqual(3, notes.Count);
        Assert.IsFalse(Audible(0)); Assert.IsTrue(Audible(1));
        Call("UndoEditorAction"); Assert.IsTrue(Audible(0));
    }

    [STATestMethod]
    public void LiveSelectionDoesNotSkipStillSelectedQuarterAndTempoMappingKeepsMusicalPosition()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = new BreakbeatPatternEditorDialog([new bool[16]], [sample], ["Test"], 1, 16, 0, 120);
        using var current = new AudioObj { Data = new float[44100 * 4], Channels = 2, SampleRate = 44100 };
        using var replacement = new AudioObj { Data = new float[44100 * 2], Channels = 2, SampleRate = 44100 };
        Type type = typeof(BreakbeatPatternEditorDialog);
        type.GetField("previewAudio", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, current);
        current.SetPosition(11025); // middle of quarter zero at 120 BPM
        type.GetMethod("TrySkipDeselectedQuartersImmediately", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(editor, [new[] { 0, 1, 2, 3 }, new[] { 0, 2, 3 }]);
        Assert.AreEqual(11025L, current.Position, "Removing a future quarter must not skip the current one.");
        long mapped = (long)type.GetMethod("MapLoopSwapSamplePosition", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(editor, [current, replacement, new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 3 }])!;
        Assert.AreEqual(11024L, mapped, 2L, "Doubling BPM must preserve the offset within the musical quarter.");
        type.GetField("previewAudio", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, null);
    }

    [STATestMethod]
    public void HeldRightButtonErasesNotesAcrossTracksAndUndoRestoresWholeGesture()
    {
        using var source = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = new BreakbeatPatternEditorDialog([new bool[16], new bool[16]], [source, source], ["One", "Two"],
            1, 16, 0, 120, [new(0, 0, 64), new(0, 128, 64), new(1, 256, 64)], initialGridResolution: 16);
        Type type = typeof(BreakbeatPatternEditorDialog);
        object? Call(string name, params object[] args) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, args);
        var picture = (Control)type.GetField("pictureBox_pattern", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        _ = editor.Handle; _ = picture.Handle;
        Rectangle grid = (Rectangle)Call("GetGridBounds")!;
        Point At(int row, double step) => new(grid.Left + (int)(grid.Width * step / 16), grid.Top + (int)(grid.Height * (row + 0.5) / 2));
        var first = At(0, 0.5); var second = At(0, 2.5); var third = At(1, 4.5);
        Call("pictureBox_pattern_MouseDown", picture, new MouseEventArgs(MouseButtons.Right, 1, first.X, first.Y, 0));
        Call("pictureBox_pattern_MouseMove", picture, new MouseEventArgs(MouseButtons.Right, 0, second.X, second.Y, 0));
        Call("pictureBox_pattern_MouseMove", picture, new MouseEventArgs(MouseButtons.Right, 0, third.X, third.Y, 0));
        Call("pictureBox_pattern_MouseUp", picture, new MouseEventArgs(MouseButtons.Right, 1, third.X, third.Y, 0));
        var notes = (List<BreakbeatPatternNote>)type.GetField("notes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        Assert.HasCount(0, notes);
        Call("UndoEditorAction");
        Assert.HasCount(3, notes);
    }

    [STATestMethod]
    public void RerollReplacesOnlyLastGeneratedNotesAndIsUndoableAndRemappable()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        var existing = new BreakbeatPatternNote(0, 0, 24);
        using var editor = new BreakbeatPatternEditorDialog([new bool[16], new bool[16]], [sample, sample], ["Outside", "Generate"],
            1, 16, 0, 120, [existing], initialGridResolution: 16);
        Type type = typeof(BreakbeatPatternEditorDialog);
        object options = Activator.CreateInstance(type.Assembly.GetType("ModularAudience.Forms.Modules.Dialogs.BreakbeatGenerateOptions")!)!;
        void Set(string key, object value) => options.GetType().GetProperty(key)!.SetValue(options, value);
        Set("DensityPercent", 100); Set("AmenFactorPercent", 0); Set("Seed", 123);
        Set("AllowPitchedNotes", true); Set("PitchChancePercent", 100); Set("PitchRangeSemitones", 12f);
        type.GetMethod("GeneratePatternInArea", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, [1, 1, 4, 11, options]);
        var notes = (List<BreakbeatPatternNote>)type.GetField("notes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var first = notes.ToArray();
        var addedAfter = new BreakbeatPatternNote(0, 240, 24);
        notes.Add(addedAfter);
        type.GetMethod("CommitHistoryAction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
        type.GetMethod("RerollLastGenerate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
        Assert.IsTrue(notes.Contains(first.Single(n => n.TrackIndex == 0))); Assert.IsTrue(notes.Contains(addedAfter));
        Assert.IsFalse(notes.Any(n => first.Any(old => old.TrackIndex == 1 && ReferenceEquals(old, n))));
        Assert.IsTrue(notes.Any(n => n.TrackIndex == 1));
        type.GetMethod("UndoEditorAction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
        CollectionAssert.AreEquivalent(first.Append(addedAfter).ToArray(), notes.ToArray());
        var scheme = ModularAudience.Forms.ControlsConfig.DefaultControlScheme.Create();
        var binding = scheme.Find("Generate.Reroll")!;
        Assert.AreEqual(Keys.R, binding.PrimaryKey);
        binding.PrimaryKey = Keys.F8;
        var resolver = new ModularAudience.Forms.ControlsConfig.ControlActionResolver(scheme);
        Assert.AreEqual("Generate.Reroll", resolver.ResolveKey(ModularAudience.Forms.ControlsConfig.BindingScope.Global, Keys.F8));
        Assert.IsNull(resolver.ResolveKey(ModularAudience.Forms.ControlsConfig.BindingScope.Global, Keys.R));
    }

    [STATestMethod]
    public void SaveWorksAfterOriginalSamplesAreDisposedAndWithoutGeneratorHandler()
    {
        var source = new AudioObj { Data = Enumerable.Repeat(0.2f, 4800).ToArray(), SampleRate = 48000, Channels = 1 };
        using var editor = new BreakbeatPatternEditorDialog([new bool[16]], [source], ["Owned sample"], 1, 16, 0, 120,
            [new BreakbeatPatternNote(0, 0, 24)]);
        source.Dispose();
        var type = typeof(BreakbeatPatternEditorDialog);
        MethodInfo save = type.GetMethod("SaveToCollectionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Task work = (Task)save.Invoke(editor, null)!;
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!work.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(5); }
        Assert.IsTrue(work.IsCompleted, "Standalone save must complete.");
        work.GetAwaiter().GetResult();
        var collection = (ModularAudience.Forms.AudioCollectionView)type.GetField("savedCollection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        try
        {
            var audios = (AudioCollection)collection.GetType().GetField("AudioC", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(collection)!;
            Assert.HasCount(1, audios.Audios);
            Assert.IsTrue(audios.Audios[0].Data.Any(sample => Math.Abs(sample) > 0.01));
        }
        finally { collection.Close(); collection.Dispose(); }
    }

    [STATestMethod]
    public void CollectionDropAddsMultipleIndependentTracksAndPreservesExistingNotes()
    {
        using var original = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var first = new AudioObj { Name = "First dropped", Data = [0.2f, 0.3f], SampleRate = 48000, Channels = 1, Bpm = 95 };
        using var second = new AudioObj { Name = "Second dropped", Data = [0.4f, 0.5f], SampleRate = 48000, Channels = 1 };
        using var editor = new BreakbeatPatternEditorDialog([new bool[16]], [original], ["Existing"], 1, 16, 0, 120,
            [new BreakbeatPatternNote(0, 0, 24)]);
        var type = typeof(BreakbeatPatternEditorDialog);
        var picture = (Control)type.GetField("pictureBox_pattern", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var toolbar = (Control)type.GetField("panel_controls", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        editor.CreateControl();
        var data = new DataObject();
        data.SetData(typeof(AudioObj[]), new[] { first, second });
        data.SetData(typeof(List<AudioObj>), new List<AudioObj> { first, second });
        Point target = picture.PointToScreen(new Point(20, picture.Height + 20));
        var enter = new DragEventArgs(data, 0, target.X, target.Y, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.None);
        typeof(Control).GetMethod("OnDragEnter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(toolbar, [enter]);
        Assert.AreEqual(DragDropEffects.Copy, enter.Effect);
        typeof(Control).GetMethod("OnDragDrop", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(toolbar, [enter]);
        var samples = (List<AudioObj>)type.GetField("samples", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        Assert.HasCount(3, samples);
        Assert.AreEqual("First dropped", samples[1].Name);
        Assert.AreEqual("Second dropped", samples[2].Name);
        Assert.AreEqual(95f, samples[1].Bpm);
        Assert.AreNotSame(first.Data, samples[1].Data);
        CollectionAssert.AreEqual(first.Data, samples[1].Data);
        var notes = (List<BreakbeatPatternNote>)type.GetField("notes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        Assert.HasCount(1, notes);
        Assert.AreEqual(0, notes[0].TrackIndex);
        Assert.IsTrue(editor.AllowDrop);
        Assert.IsTrue(toolbar.AllowDrop);
    }

    [STATestMethod]
    public void InterleavedGenerationIgnoresUnselectedTracksButBlocksSelectedTrackOverlaps()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        var existing = new BreakbeatPatternNote(0, 0, BreakbeatGenerator_V2.PatternTicksPerBar);
        using var editor = new BreakbeatPatternEditorDialog(
            [new bool[16], new bool[16], new bool[16]], [sample, sample, sample], ["Outside", "Selected 1", "Selected 2"],
            1, 16, 0, 120, [existing]);
        Type optionsType = typeof(BreakbeatPatternEditorDialog).Assembly.GetType("ModularAudience.Forms.Modules.Dialogs.BreakbeatGenerateOptions")!;
        object options = Activator.CreateInstance(optionsType)!;
        optionsType.GetProperty("Interleaved")!.SetValue(options, true);
        var placed = new List<BreakbeatPatternNote>();
        MethodInfo place = typeof(BreakbeatPatternEditorDialog).GetMethod("TryPlaceGeneratedNote", BindingFlags.NonPublic | BindingFlags.Instance)!;
        bool Place(int row, int column) => (bool)place.Invoke(editor, [row, column, 1, 0f, 100f, 1, options, placed, 1, 2])!;
        Assert.IsTrue(Place(1, 0), "The full-bar note on unselected track 0 must not block generation.");
        Assert.IsFalse(Place(2, 0), "Selected tracks must still interleave with each other.");
        Assert.IsTrue(Place(2, 1), "The next non-overlapping step must remain available.");
        Assert.HasCount(2, placed);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int style);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr window, bool revert);
    [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint command, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [STATestMethod]
    public void EditorNativeMaximizeCommandIsEnabledAndMaximizes()
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        using var editor = new BreakbeatPatternEditorDialog([new bool[16]], [sample], ["Test"], 1, 16, 0, 120);
        editor.ShowInTaskbar = false;
        editor.Show();
        Application.DoEvents();
        int style = GetWindowLong(editor.Handle, -16);
        uint state = GetMenuState(GetSystemMenu(editor.Handle, false), 0xF030, 0);
        Console.WriteLine($"MaximizeBox={editor.MaximizeBox} style={style:X8} SC_MAXIMIZE={state:X8} Minimum={editor.MinimumSize} Maximum={editor.MaximumSize}");
        Assert.IsTrue(editor.MaximizeBox);
        Assert.AreNotEqual(0, style & 0x10000, "The native window must have WS_MAXIMIZEBOX.");
        Assert.AreEqual(0u, state & 3, "SC_MAXIMIZE must not be disabled or greyed out.");
        SendMessage(editor.Handle, 0x112, (IntPtr)0xF030, IntPtr.Zero);
        Application.DoEvents();
        Assert.AreEqual(FormWindowState.Maximized, editor.WindowState);
        editor.WindowState = FormWindowState.Normal;
        // Reproduce a style update after creation, which CreateParams alone cannot protect.
        int currentStyle = GetWindowLong(editor.Handle, -16);
        SetWindowLong(editor.Handle, -16, currentStyle & ~0x10000);
        Assert.AreNotEqual(0, GetWindowLong(editor.Handle, -16) & 0x10000,
            "Later native style updates must not disable the maximize caption button.");
        typeof(Control).GetMethod("RecreateHandle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(editor, null);
        Application.DoEvents();
        Assert.AreNotEqual(0, GetWindowLong(editor.Handle, -16) & 0x10000);
        Assert.AreEqual(0u, GetMenuState(GetSystemMenu(editor.Handle, false), 0xF030, 0) & 3);
        SendMessage(editor.Handle, 0x112, (IntPtr)0xF030, IntPtr.Zero);
        Assert.AreEqual(FormWindowState.Maximized, editor.WindowState);
        editor.WindowState = FormWindowState.Normal;
    }
}
