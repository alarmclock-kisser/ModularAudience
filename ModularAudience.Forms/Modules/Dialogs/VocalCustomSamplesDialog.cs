using ModularAudience.Audio;
using ModularAudience.Audio.Vocal;
using ModularAudience.Forms;
using System.Globalization;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class VocalCustomSamplesDialog : Form
{
    private sealed record AudioChoice(Guid ChoiceKey, string DisplayName, AudioObj? Source, Guid? CachedEntryId);

    private List<AudioChoice> audioChoices = [];
    private bool refreshing;

    internal event EventHandler? MappingsChanged;

    public VocalCustomSamplesDialog()
    {
        InitializeComponent();
        RefreshGrid();
    }

    private void RefreshSourcesButton_Click(object? sender, EventArgs e)
    {
        mappingsGrid.EndEdit();
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        refreshing = true;
        try
        {
            mappingsGrid.Rows.Clear();
            audioChoices = BuildAudioChoices();
            audioColumn.DataSource = audioChoices;
            audioColumn.DisplayMember = nameof(AudioChoice.DisplayName);
            audioColumn.ValueMember = nameof(AudioChoice.ChoiceKey);
            foreach (VocalCustomSampleEntrySnapshot entry in VocalCustomSampleStore.GetEntries())
            {
                int rowIndex = mappingsGrid.Rows.Add(
                    entry.IpaSubstring,
                    entry.AudioChoiceId ?? Guid.Empty);
                mappingsGrid.Rows[rowIndex].Tag = entry.Id;
                SetRowValidation(mappingsGrid.Rows[rowIndex], entry.IpaSubstring);
            }
        }
        finally
        {
            refreshing = false;
        }
        UpdateStatus();
    }

    private static List<AudioChoice> BuildAudioChoices()
    {
        List<AudioChoice> choices = [new AudioChoice(Guid.Empty, "(No sample)", null, null)];
        HashSet<AudioObj> seen = new(ReferenceEqualityComparer.Instance);
        IEnumerable<AudioObj> openAudio = WindowMain.CollectionViews
            .Where(view => !view.IsDisposed && !view.Disposing)
            .SelectMany(view => view.AudioC.Audios)
            .Concat(WindowMain.TrackViews
                .Where(view => !view.IsDisposed && !view.Disposing)
                .Select(view => view.OriginalAudio));

        foreach (AudioObj audio in openAudio)
        {
            if (!seen.Add(audio) || !VocalCustomSampleStore.IsEligibleAudio(audio)) continue;
            double duration = audio.Data.Length / (double)(audio.Channels * audio.AdjustedSampleRate);
            string name = string.IsNullOrWhiteSpace(audio.Name) ? "Unnamed audio" : audio.Name;
            choices.Add(new AudioChoice(
                Guid.NewGuid(),
                $"{name} ({duration.ToString("0.##", CultureInfo.InvariantCulture)} s)",
                audio,
                null));
        }

        choices.AddRange(VocalCustomSampleStore.GetCachedChoices()
            .Select(choice => new AudioChoice(
                choice.ChoiceId, choice.DisplayName, null, choice.EntryId)));
        return choices;
    }

    private void MappingsGrid_DefaultValuesNeeded(object? sender, DataGridViewRowEventArgs e)
    {
        if (refreshing) return;
        e.Row.Tag = VocalCustomSampleStore.Add();
        e.Row.Cells["Audio"].Value = Guid.Empty;
    }

    private void MappingsGrid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (refreshing || e.RowIndex < 0 || e.ColumnIndex != ipaColumn.Index) return;
        DataGridViewRow row = mappingsGrid.Rows[e.RowIndex];
        if (row.IsNewRow) return;
        try
        {
            Guid id = EnsureEntry(row);
            string value = row.Cells["Ipa"].Value?.ToString() ?? string.Empty;
            VocalCustomSampleStore.SetIpaSubstring(id, value);
            SetRowValidation(row, value);
            UpdateStatus();
            MappingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException error)
        {
            statusLabel.Text = error.Message;
        }
    }

    private void MappingsGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (refreshing || e.RowIndex < 0 || e.ColumnIndex != audioColumn.Index) return;
        DataGridViewRow row = mappingsGrid.Rows[e.RowIndex];
        if (row.IsNewRow || row.Cells["Audio"].Value is not Guid choiceKey) return;
        try
        {
            Guid id = EnsureEntry(row);
            VocalCustomSampleEntrySnapshot? existing = VocalCustomSampleStore.GetEntries()
                .FirstOrDefault(entry => entry.Id == id);
            if (existing?.AudioChoiceId == choiceKey) return;
            if (choiceKey == Guid.Empty)
            {
                VocalCustomSampleStore.ClearAudio(id);
            }
            else
            {
                AudioChoice? choice = audioChoices.FirstOrDefault(candidate => candidate.ChoiceKey == choiceKey);
                if (choice?.Source is not null)
                    VocalCustomSampleStore.SetAudio(id, choice.Source);
                else if (choice?.CachedEntryId is Guid cachedId)
                    VocalCustomSampleStore.SetAudioFromEntry(id, cachedId);
                else
                    throw new InvalidOperationException("The selected audio source is no longer available.");
            }
            RefreshGrid();
            MappingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            statusLabel.Text = error.Message;
            RefreshGrid();
        }
    }

    private void MappingsGrid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (mappingsGrid.IsCurrentCellDirty
            && mappingsGrid.CurrentCell is DataGridViewComboBoxCell)
            mappingsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void MappingsGrid_UserDeletingRow(object? sender, DataGridViewRowCancelEventArgs e)
    {
        DataGridViewRow? row = e.Row;
        if (row?.Tag is not Guid id) return;
        VocalCustomSampleStore.Remove(id);
        MappingsChanged?.Invoke(this, EventArgs.Empty);
        UpdateStatus();
    }

    private void MappingsGrid_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        statusLabel.Text = "Select an audio source from the list or refresh the available sources.";
    }

    private void MappingsGrid_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data is not null && GetDraggedAudio(e.Data) is not null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void MappingsGrid_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data is not null && GetDraggedAudio(e.Data) is not null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void MappingsGrid_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data is null || GetDraggedAudio(e.Data) is not AudioObj audio) return;
        Point clientPoint = mappingsGrid.PointToClient(new Point(e.X, e.Y));
        DataGridView.HitTestInfo hit = mappingsGrid.HitTest(clientPoint.X, clientPoint.Y);
        Guid entryId;
        if (hit.RowIndex >= 0 && hit.RowIndex < mappingsGrid.Rows.Count
            && !mappingsGrid.Rows[hit.RowIndex].IsNewRow)
        {
            entryId = EnsureEntry(mappingsGrid.Rows[hit.RowIndex]);
        }
        else
        {
            entryId = VocalCustomSampleStore.Add();
        }

        try
        {
            VocalCustomSampleStore.SetAudio(entryId, audio);
            RefreshGrid();
            MappingsChanged?.Invoke(this, EventArgs.Empty);
            int rowIndex = mappingsGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(row => row.Tag is Guid id && id == entryId)?.Index ?? -1;
            if (rowIndex >= 0) mappingsGrid.CurrentCell = mappingsGrid.Rows[rowIndex].Cells["Ipa"];
            statusLabel.Text = "Audio sample assigned. Enter an IPA substring for this row.";
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            statusLabel.Text = error.Message;
        }
    }

    private static AudioObj? GetDraggedAudio(IDataObject data)
    {
        IEnumerable<AudioObj>? audios = null;
        if (data.GetDataPresent(typeof(AudioObj[])))
            audios = data.GetData(typeof(AudioObj[])) as AudioObj[];
        else if (data.GetDataPresent(typeof(List<AudioObj>)))
            audios = data.GetData(typeof(List<AudioObj>)) as List<AudioObj>;
        else if (data.GetDataPresent(typeof(IEnumerable<AudioObj>)))
            audios = data.GetData(typeof(IEnumerable<AudioObj>)) as IEnumerable<AudioObj>;
        else if (data.GetDataPresent(typeof(AudioObj)))
            audios = data.GetData(typeof(AudioObj)) is AudioObj single ? [single] : null;

        return audios?.FirstOrDefault(VocalCustomSampleStore.IsEligibleAudio);
    }

    private Guid EnsureEntry(DataGridViewRow row)
    {
        if (row.Tag is Guid id) return id;
        id = VocalCustomSampleStore.Add();
        row.Tag = id;
        return id;
    }

    private void SetRowValidation(DataGridViewRow row, string value)
    {
        row.ErrorText = string.IsNullOrWhiteSpace(value)
            || VocalSynth.IsValidCustomSampleIpa(value)
                ? string.Empty
                : "Enter a valid IPA sequence, for example /tʃ/. This row is not active until the IPA is valid.";
    }

    private void UpdateStatus()
    {
        int activeMappings = VocalCustomSampleStore.GetActiveMappingCount();
        statusLabel.Text = $"{activeMappings} active mapping{(activeMappings == 1 ? string.Empty : "s")}. "
            + "Drop audio onto a row to assign or replace its sample; drop below the rows to add one.";
    }
}
