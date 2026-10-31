namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private YoutubeSearchHistory? searchHistory;
    private void InitializeSearchHistory()
    {
        try { this.searchHistory = new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { this.AppendLog("Unable to load local search history: " + error.Message); }
        this.RefreshSearchSuggestions();
    }
    private void RefreshSearchSuggestions()
    {
        var suggestions = new AutoCompleteStringCollection();
        suggestions.AddRange(this.searchHistory?.Entries.ToArray() ?? []);
        this.queryText.AutoCompleteMode = AutoCompleteMode.Suggest;
        this.queryText.AutoCompleteSource = AutoCompleteSource.CustomSource;
        this.queryText.AutoCompleteCustomSource = suggestions;
    }
    private void RememberSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            this.searchHistory ??= new();
            this.searchHistory.Add(query);
            this.RefreshSearchSuggestions();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { this.AppendLog("Unable to save local search history: " + error.Message); }
    }
    private void ShowSearchHistory()
    {
        if (this.searchHistory?.Entries.Count is not > 0) return;
        var menu = new ContextMenuStrip(this.components) { ShowImageMargin = false };
        foreach (string query in this.searchHistory.Entries)
        {
            var item = new ToolStripMenuItem(query.Replace("&", "&&")) { ToolTipText = query };
            item.Click += (_, _) =>
            {
                this.queryText.Text = query;
                this.queryText.SelectionStart = query.Length;
                this.queryText.Focus();
                this.Search(false);
            };
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(this.queryText, new Point(0, this.queryText.Height));
    }
}
