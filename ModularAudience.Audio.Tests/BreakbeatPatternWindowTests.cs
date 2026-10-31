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
