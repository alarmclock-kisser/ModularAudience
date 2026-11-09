using ModularAudience.Forms.Modules;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private bool miniLayout;
    private bool switchingMiniLayout;
    private readonly Dictionary<int, (SizeType Type, float Height)> normalRowStyles = [];
    private readonly Size normalLayoutThreshold = new(600, 400);
    private bool condensedLayout;
    private TabPage? settingsTab;
    private TableLayoutPanel? settingsLayout;
    private Control? normalActions;
    private readonly Dictionary<int, (SizeType Type, float Height)> expandedRowStyles = [];
    private WaveformPreview? thumbnailPopup;
    private string? hoveredThumbnailId;
    private CancellationTokenSource? popupCancellation;
    private int normalSplitterDistance = 760;

    private void MiniLayout_SizeChanged(object? sender, EventArgs e) => UpdateMiniLayout();

    private void UpdateMiniLayout()
    {
        if (rootLayout is null || switchingMiniLayout || resourcesReleased) return;
        UpdateCondensedLayout();
        bool compact = Width < normalLayoutThreshold.Width || Height < normalLayoutThreshold.Height;
        if (compact == miniLayout) return;
        switchingMiniLayout = true;
        rootLayout.SuspendLayout();
        try
        {
            miniLayout = compact;
            if (compact) normalSplitterDistance = mainSplit.SplitterDistance;
            foreach (int row in new[] { 0, 3, 4, 5, 6 })
            {
                var style = rootLayout.RowStyles[row];
                if (compact)
                {
                    normalRowStyles[row] = (style.SizeType, style.Height);
                    style.SizeType = SizeType.Absolute;
                    style.Height = 0;
                }
                else if (normalRowStyles.TryGetValue(row, out var saved))
                { style.SizeType = saved.Type; style.Height = saved.Height; }
                if (rootLayout.GetControlFromPosition(0, row) is { } control)
                    control.Visible = !compact && (!condensedLayout || row is 1 or 2);
            }
            // The same tables stay alive: selection, downloads and history survive layout switches.
            mainSplit.Panel1MinSize = compact ? 100 : condensedLayout ? 240 : 420;
            mainSplit.Panel2Collapsed = compact;
            resultsSummary.Visible = !compact;
            if (settingsTab is not null)
            {
                if (condensedLayout && !browsingTabs.TabPages.Contains(settingsTab)) browsingTabs.TabPages.Add(settingsTab);
            }
            queryPanel.GetControlFromPosition(1, 0)!.Visible = !compact;
            playlistButton.Visible = !compact;
            queryPanel.ColumnStyles[1].SizeType = compact ? SizeType.Absolute : SizeType.AutoSize;
            queryPanel.ColumnStyles[1].Width = 0;
            queryPanel.ColumnStyles[3].SizeType = compact ? SizeType.Absolute : SizeType.AutoSize;
            queryPanel.ColumnStyles[3].Width = 0;
            if (!compact)
                mainSplit.SplitterDistance = Math.Clamp(normalSplitterDistance, mainSplit.Panel1MinSize,
                    Math.Max(mainSplit.Panel1MinSize, mainSplit.Width - mainSplit.Panel2MinSize - mainSplit.SplitterWidth));
            rootLayout.Padding = new Padding(compact ? 3 : 8);
            HideThumbnailPopup();
        }
        finally { rootLayout.ResumeLayout(true); switchingMiniLayout = false; }
        FitResultColumns();
    }

    private void UpdateCondensedLayout()
    {
        bool condensed = Width < 1000 || Height < 780;
        if (condensed == condensedLayout) return;
        // Restore the mini-only row changes before changing the underlying normal layout.
        if (miniLayout)
            foreach (var pair in normalRowStyles)
            { rootLayout.RowStyles[pair.Key].SizeType = pair.Value.Type; rootLayout.RowStyles[pair.Key].Height = pair.Value.Height; }
        rootLayout.SuspendLayout();
        try
        {
            condensedLayout = condensed;
            normalActions ??= rootLayout.GetControlFromPosition(0, 4);
            if (settingsTab is null)
            {
                settingsTab = new TabPage("Options / actions") { UseVisualStyleBackColor = true };
                settingsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
                settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                settingsTab.Controls.Add(settingsLayout);
            }
            if (condensed)
            {
                foreach (int row in new[] { 0, 3, 4, 5, 6 })
                {
                    var style = rootLayout.RowStyles[row];
                    expandedRowStyles[row] = (style.SizeType, style.Height);
                    style.SizeType = SizeType.Absolute;
                    style.Height = 0;
                    if (rootLayout.GetControlFromPosition(0, row) is { } control) control.Visible = false;
                }
                settingsLayout!.Controls.Add(optionsGroup, 0, 0);
                settingsLayout.Controls.Add(normalActions!, 0, 1);
                optionsGroup.Visible = normalActions!.Visible = true;
                browsingTabs.TabPages.Add(settingsTab);
                mainSplit.Panel1MinSize = 240;
                mainSplit.Panel2MinSize = 220;
            }
            else
            {
                rootLayout.Controls.Add(optionsGroup, 0, 3);
                rootLayout.Controls.Add(normalActions!, 0, 4);
                browsingTabs.TabPages.Remove(settingsTab);
                foreach (var pair in expandedRowStyles)
                {
                    var style = rootLayout.RowStyles[pair.Key];
                    style.SizeType = pair.Value.Type;
                    style.Height = pair.Value.Height;
                    rootLayout.GetControlFromPosition(0, pair.Key)!.Visible = true;
                }
                mainSplit.Panel1MinSize = 420;
                mainSplit.Panel2MinSize = 270;
            }
            if (miniLayout)
            {
                foreach (int row in new[] { 0, 3, 4, 5, 6 })
                {
                    var style = rootLayout.RowStyles[row];
                    normalRowStyles[row] = (style.SizeType, style.Height);
                    style.SizeType = SizeType.Absolute;
                    style.Height = 0;
                }
            }
        }
        finally { rootLayout.ResumeLayout(true); }
    }

    private void HideThumbnailPopup()
    {
        hoveredThumbnailId = null;
        popupCancellation?.Cancel();
        popupCancellation = null;
        thumbnailPopup?.Hide();
    }

    private async void Results_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!Hints.HintPolicy.Enabled || !miniLayout || !thumbnailCheck.Checked || closing || !ContainsFocus) { HideThumbnailPopup(); return; }
        if (resultsList.GetItemAt(e.X, e.Y)?.Tag is not ModularAudience.Ytdlp.YtdlpVideo video)
        { HideThumbnailPopup(); return; }
        if (hoveredThumbnailId == video.Id) return;
        HideThumbnailPopup();
        hoveredThumbnailId = video.Id;
        var source = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        popupCancellation = source;
        var task = LoadPopupAsync();
        previewTasks.Add(task);
        try { await task; }
        finally
        {
            previewTasks.Remove(task);
            if (ReferenceEquals(popupCancellation, source)) popupCancellation = null;
            source.Dispose();
        }

        async Task LoadPopupAsync()
        {
            try
            {
                await Task.Delay(350, source.Token);
                if (!Hints.HintPolicy.Enabled || hoveredThumbnailId != video.Id || !miniLayout || closing) return;
                using var preview = await YoutubeThumbnailLoader.LoadAsync(thumbnailClient, video, _ => { }, source.Token);
                if (!Hints.HintPolicy.Enabled || hoveredThumbnailId != video.Id || !miniLayout || closing || resourcesReleased || !ContainsFocus) return;
                var point = resultsList.PointToScreen(new Point(e.X + 20, e.Y + 20));
                var bounds = Screen.FromPoint(point).WorkingArea;
                point.X = Math.Clamp(point.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - preview.Width));
                point.Y = Math.Clamp(point.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - preview.Height));
                thumbnailPopup ??= new WaveformPreview();
                thumbnailPopup.ShowWaveform(new Bitmap(preview), point);
            }
            catch (Exception error) when (error is OperationCanceledException or System.Net.Http.HttpRequestException or AggregateException or ArgumentException) { }
        }
    }
}
