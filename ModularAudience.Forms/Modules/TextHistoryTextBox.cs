namespace ModularAudience.Forms.Modules;

internal sealed class TextHistoryTextBox : TextBox
{
    private readonly TextEditHistory history;

    public TextHistoryTextBox()
    {
        history = new TextEditHistory(this);
    }

    public void ResetHistory() => history.Reset();
    public bool UndoText() => history.Undo();
    public bool RedoText() => history.Redo();

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (ProcessHistoryCommand(keyData)) return true;
        return base.ProcessCmdKey(ref message, keyData);
    }

    internal bool ProcessHistoryCommand(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            history.Undo();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Y))
        {
            history.Redo();
            return true;
        }
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) history.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class TextHistoryRichTextBox : RichTextBox
{
    private readonly TextEditHistory history;

    public TextHistoryRichTextBox()
    {
        history = new TextEditHistory(this);
    }

    internal bool ProcessHistoryCommand(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            history.Undo();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Y))
        {
            history.Redo();
            return true;
        }
        return false;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (ProcessHistoryCommand(keyData)) return true;
        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0102 && message.WParam == (IntPtr)'\r' && !ReadOnly)
        {
            SelectedText = Environment.NewLine;
            return;
        }

        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) history.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class TextEditHistory : IDisposable
{
    private const int MaxUndoSteps = 8192;
    private const long GroupDelayMilliseconds = 900;
    private readonly TextBoxBase editor;
    private readonly LinkedList<TextEdit> undo = new();
    private readonly Stack<TextEdit> redo = new();
    private string previousText;
    private Keys pendingKey;
    private int pendingSelectionStart, pendingSelectionLength;
    private bool pendingKeyboardEdit;
    private bool pendingModified, applying, disposed;

    private enum EditKind { Insert, Backspace, ForwardDelete, Other }

    private sealed record TextEdit(
        int Start,
        string Removed,
        string Inserted,
        EditKind Kind,
        long ChangedAt,
        bool CanGroup);

    public TextEditHistory(TextBoxBase editor)
    {
        this.editor = editor;
        previousText = editor.Text;
        editor.TextChanged += HandleTextChanged;
        editor.KeyDown += HandleKeyDown;
        editor.KeyUp += HandleKeyUp;
    }

    public void Reset()
    {
        ThrowIfDisposed();
        undo.Clear();
        redo.Clear();
        previousText = editor.Text;
        pendingKey = Keys.None;
        pendingKeyboardEdit = false;
        pendingModified = false;
    }

    public bool Undo()
    {
        ThrowIfDisposed();
        if (undo.Last is not { } last) return false;
        undo.RemoveLast();
        Apply(last.Value, undoing: true);
        redo.Push(last.Value);
        return true;
    }

    public bool Redo()
    {
        ThrowIfDisposed();
        if (redo.Count == 0) return false;
        TextEdit edit = redo.Pop();
        Apply(edit, undoing: false);
        undo.AddLast(edit);
        return true;
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        pendingKey = e.KeyCode;
        pendingSelectionStart = editor.SelectionStart;
        pendingSelectionLength = editor.SelectionLength;
        pendingKeyboardEdit = true;
        pendingModified = e.Control || e.Alt;
    }

    private void HandleKeyUp(object? sender, KeyEventArgs e)
    {
        pendingKey = Keys.None;
        pendingKeyboardEdit = false;
        pendingModified = false;
    }

    private void HandleTextChanged(object? sender, EventArgs e)
    {
        string currentText = editor.Text;
        if (applying)
        {
            previousText = currentText;
            return;
        }

        if (currentText == previousText) return;
        TextEdit edit = pendingKeyboardEdit
            && TryCreateKeyboardEdit(previousText, currentText, out TextEdit keyboardEdit)
                ? keyboardEdit
                : CreateEdit(previousText, currentText);
        previousText = currentText;
        if (CanMerge(undo.Last?.Value, edit))
            undo.Last!.Value = Merge(undo.Last.Value, edit);
        else
        {
            undo.AddLast(edit);
            if (undo.Count > MaxUndoSteps) undo.RemoveFirst();
        }
        redo.Clear();
        pendingKey = Keys.None;
        pendingKeyboardEdit = false;
        pendingModified = false;
    }

    private bool TryCreateKeyboardEdit(string oldText, string newText, out TextEdit edit)
    {
        int start = pendingSelectionStart;
        int removedLength = pendingSelectionLength;
        int lengthDifference = oldText.Length - newText.Length;
        if (removedLength == 0 && pendingKey == Keys.Back)
        {
            if (lengthDifference <= 0) { edit = null!; return false; }
            removedLength = lengthDifference;
            start -= removedLength;
        }
        else if (removedLength == 0 && pendingKey == Keys.Delete)
        {
            if (lengthDifference <= 0) { edit = null!; return false; }
            removedLength = lengthDifference;
        }

        int insertedLength = newText.Length - (oldText.Length - removedLength);
        if (start < 0 || removedLength < 0 || start + removedLength > oldText.Length
            || insertedLength < 0 || start + insertedLength > newText.Length)
        {
            edit = null!;
            return false;
        }

        edit = CreateEditAt(start, oldText, newText, start + removedLength, start + insertedLength);
        return true;
    }

    private TextEdit CreateEdit(string oldText, string newText)
    {
        int start = 0;
        int sharedLength = Math.Min(oldText.Length, newText.Length);
        while (start < sharedLength && oldText[start] == newText[start]) start++;

        int oldEnd = oldText.Length;
        int newEnd = newText.Length;
        while (oldEnd > start && newEnd > start && oldText[oldEnd - 1] == newText[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }

        return CreateEditAt(start, oldText, newText, oldEnd, newEnd);
    }

    private TextEdit CreateEditAt(int start, string oldText, string newText, int oldEnd, int newEnd)
    {
        string removed = oldText.Substring(start, oldEnd - start);
        string inserted = newText.Substring(start, newEnd - start);
        EditKind kind = removed.Length == 0
            ? EditKind.Insert
            : inserted.Length != 0 ? EditKind.Other
            : pendingKey == Keys.Back ? EditKind.Backspace
            : pendingKey == Keys.Delete ? EditKind.ForwardDelete
            : EditKind.Other;
        bool canGroup = !pendingModified && (kind == EditKind.Insert
            ? inserted.Length == 1
            : (kind is EditKind.Backspace or EditKind.ForwardDelete) && removed.Length == 1);
        return new TextEdit(start, removed, inserted, kind, Environment.TickCount64, canGroup);
    }

    private static bool CanMerge(TextEdit? previous, TextEdit current)
    {
        if (previous is null || !previous.CanGroup || !current.CanGroup
            || current.ChangedAt - previous.ChangedAt > GroupDelayMilliseconds)
            return false;

        return current.Kind switch
        {
            EditKind.Insert => previous.Kind == EditKind.Insert
                && current.Start == previous.Start + previous.Inserted.Length
                && (!char.IsWhiteSpace(previous.Inserted[^1])
                    || current.Inserted.All(char.IsWhiteSpace)),
            EditKind.Backspace => previous.Kind == EditKind.Backspace
                && current.Start + current.Removed.Length == previous.Start
                && !previous.Removed.Any(char.IsWhiteSpace),
            EditKind.ForwardDelete => previous.Kind == EditKind.ForwardDelete
                && current.Start == previous.Start
                && (!previous.Removed.Any(char.IsWhiteSpace)
                    || current.Removed.All(char.IsWhiteSpace)),
            _ => false
        };
    }

    private static TextEdit Merge(TextEdit previous, TextEdit current)
    {
        return current.Kind switch
        {
            EditKind.Insert => previous with
            {
                Inserted = string.Concat(previous.Inserted, current.Inserted),
                ChangedAt = current.ChangedAt
            },
            EditKind.Backspace => previous with
            {
                Start = current.Start,
                Removed = string.Concat(current.Removed, previous.Removed),
                ChangedAt = current.ChangedAt
            },
            EditKind.ForwardDelete => previous with
            {
                Removed = string.Concat(previous.Removed, current.Removed),
                ChangedAt = current.ChangedAt
            },
            _ => current
        };
    }

    private void Apply(TextEdit edit, bool undoing)
    {
        string replacement = undoing ? edit.Removed : edit.Inserted;
        int replacedLength = undoing ? edit.Inserted.Length : edit.Removed.Length;
        int start = Math.Clamp(edit.Start, 0, editor.TextLength);
        int length = Math.Min(replacedLength, editor.TextLength - start);
        applying = true;
        try
        {
            editor.Select(start, length);
            editor.SelectedText = replacement;
            int caret = Math.Min(start + replacement.Length, editor.TextLength);
            editor.Select(caret, 0);
            previousText = editor.Text;
        }
        finally
        {
            applying = false;
            pendingKey = Keys.None;
            pendingKeyboardEdit = false;
            pendingModified = false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        editor.TextChanged -= HandleTextChanged;
        editor.KeyDown -= HandleKeyDown;
        editor.KeyUp -= HandleKeyUp;
        undo.Clear();
        redo.Clear();
        previousText = string.Empty;
    }
}
