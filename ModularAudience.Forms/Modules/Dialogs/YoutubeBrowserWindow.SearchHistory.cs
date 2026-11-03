namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private YoutubeSearchHistory? searchHistory;
    private ContextMenuStrip? searchHistoryMenu;
    private bool searchSuggestionsDirty;
    private void InitializeSearchHistory()
    {
        try { this.searchHistory = new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { this.AppendLog("Unable to load local search history: " + error.Message); }
        this.RefreshSearchSuggestions();
        this.queryText.Leave += (_, _) =>
        {
            // Rebuilding Shell autocomplete while Enter or a suggestion click is being
            // dispatched can block the UI in native autocomplete/COM code.
            if (this.searchSuggestionsDirty && this.IsHandleCreated && !this.IsDisposed)
                this.BeginInvoke((Action)(() =>
                {
                    if (!this.IsDisposed && !this.resourcesReleased && !this.queryText.ContainsFocus)
                        this.RefreshSearchSuggestions();
                }));
        };
    }
    private void RefreshSearchSuggestions()
    {
        var suggestions = new AutoCompleteStringCollection();
        suggestions.AddRange(this.searchHistory?.Entries.ToArray() ?? []);
        this.queryText.AutoCompleteMode = AutoCompleteMode.Suggest;
        this.queryText.AutoCompleteSource = AutoCompleteSource.CustomSource;
        this.queryText.AutoCompleteCustomSource = suggestions;
        this.searchSuggestionsDirty = false;
    }
    private void RememberSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            this.searchHistory ??= new();
            this.searchHistory.Add(query);
            this.searchSuggestionsDirty = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { this.AppendLog("Unable to save local search history: " + error.Message); }
    }
    private void ShowSearchHistory()
    {
        if (this.closing || this.IsDisposed || this.resourcesReleased) return;
        if (this.searchHistoryMenu?.Visible == true)
        {
            this.searchHistoryMenu.Close();
            return;
        }
        if (this.searchHistory?.Entries.Count is not > 0) return;
        // Keep the dropdown alive until the browser is disposed. Disposing from Closed
        // destroys its native handle while ToolStrip is still finishing dismissal.
        var menu = this.searchHistoryMenu ??= new ContextMenuStrip { ShowImageMargin = false };
        while (menu.Items.Count > 0)
        {
            var oldItem = menu.Items[0];
            menu.Items.RemoveAt(0);
            oldItem.Dispose();
        }
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
        menu.Show(this.queryText, new Point(0, this.queryText.Height));
    }
}
