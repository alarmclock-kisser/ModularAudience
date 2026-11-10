using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules;
using System.Reflection;
using System.Windows.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TextEditHistoryTests
{
    [STATestMethod]
    public void TypedWordsUndoAndRedoAsGroupsWithoutToggling()
    {
        using var editor = new TextHistoryTextBox();
        foreach (char character in "hello world")
            editor.AppendText(character.ToString());

        Assert.IsTrue(editor.ProcessHistoryCommand(Keys.Control | Keys.Z));
        Assert.AreEqual("hello ", editor.Text);
        Assert.IsTrue(editor.ProcessHistoryCommand(Keys.Control | Keys.Z));
        Assert.AreEqual(string.Empty, editor.Text);
        Assert.IsTrue(editor.ProcessHistoryCommand(Keys.Control | Keys.Z));
        Assert.AreEqual(string.Empty, editor.Text);

        Assert.IsTrue(editor.ProcessHistoryCommand(Keys.Control | Keys.Y));
        Assert.AreEqual("hello ", editor.Text);
        Assert.IsTrue(editor.ProcessHistoryCommand(Keys.Control | Keys.Y));
        Assert.AreEqual("hello world", editor.Text);
        Assert.IsTrue(editor.ProcessHistoryCommand(Keys.Control | Keys.Y));
        Assert.AreEqual("hello world", editor.Text);
    }

    [STATestMethod]
    public void NewEditAfterUndoDiscardsRedoBranch()
    {
        using var editor = new TextHistoryTextBox();
        editor.AppendText("first ");
        editor.AppendText("second");
        Assert.IsTrue(editor.UndoText());
        Assert.AreEqual("first ", editor.Text);

        editor.AppendText("third");
        Assert.IsFalse(editor.RedoText());
        Assert.AreEqual("first third", editor.Text);
    }

    [STATestMethod]
    public void BackspaceGroupsAWordAndItsLeadingSpace()
    {
        using var editor = new TextHistoryTextBox { Text = "hello world" };
        editor.ResetHistory();
        editor.SelectionStart = editor.TextLength;

        for (int i = 0; i < 6; i++)
        {
            RaiseKeyEvent(editor, "OnKeyDown", Keys.Back);
            int position = editor.SelectionStart;
            editor.Select(position - 1, 1);
            editor.SelectedText = string.Empty;
            RaiseKeyEvent(editor, "OnKeyUp", Keys.Back);
        }

        Assert.AreEqual("hello", editor.Text);
        Assert.IsTrue(editor.ProcessHistoryCommand(Keys.Control | Keys.Z));
        Assert.AreEqual("hello world", editor.Text);
    }

    private static void RaiseKeyEvent(TextBox editor, string methodName, Keys key)
    {
        MethodInfo method = typeof(Control).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(editor, [new KeyEventArgs(key)]);
    }
}
