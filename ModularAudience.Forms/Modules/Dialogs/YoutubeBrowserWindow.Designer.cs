using System.ComponentModel;
using ModularAudience.Ytdlp;

#nullable enable

namespace ModularAudience.Forms.Modules.Dialogs;

partial class YoutubeBrowserWindow
{
    private IContainer? components;
    private TableLayoutPanel rootLayout = null!;
    private TableLayoutPanel queryPanel = null!;
    private TableLayoutPanel detailsLayout = null!;
    private SplitContainer mainSplit = null!;
    private TabControl browsingTabs = null!;
    private TabPage resultsTab = null!;
    private TabPage downloadsTab = null!;
    private ListView downloadJobsList = null!;
    private GroupBox optionsGroup = null!;
    private TextBox queryText = null!;
    private ComboBox resultTypeCombo = null!;
    private NumericUpDown resultLimit = null!;
    private ListView resultsList = null!;
    private Label resultsSummary = null!;
    private PictureBox thumbnailPicture = null!;
    private CheckBox thumbnailCheck = null!;
    private TextBox descriptionText = null!;
    private TextBox activityLog = null!;
    private CheckBox saveCheck = null!;
    private ComboBox formatCombo = null!;
    private DomainUpDown audioQuality = null!;
    private Label audioQualityLabel = null!;
    private Button metadataButton = null!;
    private Button showRelatedButton = null!;
    private CheckBox waitForBpmCheck = null!;
    private ComboBox ipVersionCombo = null!;
    private ComboBox proxyModeCombo = null!;
    private TextBox proxyText = null!;
    private TextBox outputDirectory = null!;
    private Button folderButton = null!;
    private NumericUpDown cooldownSeconds = null!;
    private NumericUpDown maxDurationMinutes = null!;
    private NumericUpDown maxSizeMiB = null!;
    private NumericUpDown maxParallelDownloadsNumber = null!;
    private NumericUpDown retryCountNumber = null!;
    private CheckBox stretchCheck = null!;
    private Button stretchConfigButton = null!;
    private Label stretchLabel = null!;
    private Label storageLabel = null!;
    private Button searchButton = null!;
    private Button playlistButton = null!;
    private Button downloadButton = null!;
    private Button relatedButton = null!;
    private Button uploaderButton = null!;
    private Button openButton = null!;
    private Button copyButton = null!;
    private Button updateButton = null!;
    private CheckBox checkForUpdates = null!;
    private Button toolsFolderButton = null!;
    private Button cancelButton = null!;
    private Label statusLabel = null!;
    private ProgressBar operationProgress = null!;
    private Label batchProgressLabel = null!;
    private Label elapsedLabel = null!;
    private Label etaLabel = null!;
    private System.Windows.Forms.Timer progressTimer = null!;
    private ContextMenuStrip columnsMenu = null!;
    private ToolStripMenuItem[] columnMenuItems = null!;
    private ToolStripMenuItem playItem = null!;
    private CheckBox autoplayCheck = null!;
    private FolderBrowserDialog folderDialog = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseResources();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        SuspendLayout();
        InitializeRootLayout();
        InitializeSearchPanel();
        InitializeResultsPanel();
        InitializeOptionsPanel();
        InitializeActionPanel();
        InitializeStatusPanel();
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1180, 820);
        MinimumSize = new Size(420, 260);
        Text = "YouTube Browser — ModularAudience";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        MinimizeBox = true;
        MaximizeBox = true;
        AcceptButton = searchButton;
        Shown += Window_Shown;
        FormClosing += Window_FormClosing;
        Controls.Add(rootLayout);
        ResumeLayout(true);
        SizeChanged += MiniLayout_SizeChanged;
        LocationChanged += (sender, args) => HideThumbnailPopup();
        Deactivate += (sender, args) => HideThumbnailPopup();
        browsingTabs.SelectedIndexChanged += (sender, args) => HideThumbnailPopup();
        resultsList.MouseMove += Results_MouseMove;
        resultsList.MouseLeave += (sender, args) => HideThumbnailPopup();
        UpdateMiniLayout();
    }

    private void InitializeRootLayout()
    {
        rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = false,
            Size = new Size(1180, 820), Padding = new Padding(8), ColumnCount = 1, RowCount = 7
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var notice = new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 3),
            Text = "YouTube Browser — Authorized content only. Browser sessions require consent; no DRM or access-control bypass."
        };
        rootLayout.Controls.Add(notice, 0, 0);
    }

    private void InitializeSearchPanel()
    {
        queryPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1 };
        queryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int index = 1; index < 4; index++) queryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        queryText = new TextBox
        {
            Dock = DockStyle.Fill, PlaceholderText = "Search words or an HTTPS YouTube video / playlist URL",
            MaxLength = 4096, AccessibleName = "YouTube search words or URL", Margin = new Padding(0, 3, 6, 3)
        };
        resultLimit = new NumericUpDown
        {
            Minimum = 1, Maximum = 200, Value = 30, Width = 64,
            AccessibleName = "Maximum results (1 to 200)", Margin = new Padding(3, 3, 6, 3)
        };
        var limitPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        limitPanel.Controls.Add(new Label { Text = "Results", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
        limitPanel.Controls.Add(resultLimit);
        searchButton = DesignerButton("Search");
        playlistButton = DesignerButton("Open URL");
        playlistButton.AccessibleName = "Open YouTube video or playlist URL";
        var searchField = new Panel { Dock = DockStyle.Fill, Height = 29, Margin = Padding.Empty };
        var historyButton = new Button { Text = "▼", Name = "searchHistoryButton", Width = 25, Dock = DockStyle.Right,
            AccessibleName = "Show local search history", TabStop = false };
        resultTypeCombo = new ComboBox
        {
            Name = "resultTypeCombo", DropDownStyle = ComboBoxStyle.DropDownList, Width = 96,
            Dock = DockStyle.Right, AccessibleName = "Search result types"
        };
        resultTypeCombo.Items.AddRange(["All", "Videos", "Playlists", "Channels"]);
        resultTypeCombo.SelectedIndex = 0;
        resultTypeCombo.SelectedIndexChanged += ResultType_Changed;
        historyButton.Click += (_, _) => ShowSearchHistory();
        searchField.Controls.Add(queryText);
        searchField.Controls.Add(historyButton);
        searchField.Controls.Add(resultTypeCombo);
        queryPanel.Controls.Add(searchField, 0, 0);
        queryPanel.Controls.Add(limitPanel, 1, 0);
        queryPanel.Controls.Add(searchButton, 2, 0);
        queryPanel.Controls.Add(playlistButton, 3, 0);
        queryText.KeyDown += Query_KeyDown;
        searchButton.Click += Search_Click;
        playlistButton.Click += Playlist_Click;
        rootLayout.Controls.Add(queryPanel, 0, 1);
        var hints = new ToolTip(components!);
        hints.SetToolTip(queryText, "Search YouTube by words or paste a supported HTTPS video/playlist/channel URL. Enter starts the search. Use the arrow beside this field to recall up to 32 real search queries.");
        hints.SetToolTip(resultTypeCombo, "Changing this repeats the current search automatically. In Channel :: view the filter applies to that channel (for example its playlists). New channel views start on All.");
        hints.SetToolTip(resultLimit, "Maximum word-search or channel results. Explicit playlist navigation and playlist download enqueue expand the full playlist independently of this search limit.");
        hints.SetToolTip(searchButton, "Search or open the query. Results appear as each entry arrives; starting another search cancels the previous one.");
        hints.SetToolTip(playlistButton, "Open the supported YouTube URL entered in the search field. Playlist search hits can also be opened with a double-click.");
    }

    private void InitializeResultsPanel()
    {
        mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill, Size = new Size(1156, 340), SplitterWidth = 6,
            SplitterDistance = 760, Panel1MinSize = 420, Panel2MinSize = 270,
            Margin = new Padding(0, 0, 0, 4)
        };
        var listLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        listLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultsList = new ModularAudience.Forms.Controls.BufferedListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true,
            HideSelection = false, GridLines = true, AllowColumnReorder = true,
            AccessibleName = "YouTube search results", UseCompatibleStateImageBehavior = false,
            Margin = Padding.Empty, ShowItemToolTips = true
        };
        resultsList.Columns.AddRange([
            new ColumnHeader { Text = "Title", Width = 320 }, new ColumnHeader { Text = "Uploader", Width = 160 },
            new ColumnHeader { Text = "Duration", Width = 90 }, new ColumnHeader { Text = "Views", Width = 100 },
            new ColumnHeader { Text = "ID", Width = 120 }, new ColumnHeader { Text = "URL", Width = 320 },
            new ColumnHeader { Text = "Upload Date", Width = 110 }
        ]);
        resultsSummary = new Label
        {
            Text = "No results yet. Enter search words or a playlist URL.", AutoSize = true,
            Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0)
        };
        listLayout.Controls.Add(resultsList, 0, 0);
        listLayout.Controls.Add(resultsSummary, 0, 1);
        mainSplit.Panel1.Controls.Add(listLayout);
        InitializeDetailsPanel();
        InitializeColumnsMenu();
        resultsList.SelectedIndexChanged += Selection_Changed;
        resultsList.DoubleClick += Result_DoubleClick;
        resultsList.ColumnWidthChanging += Column_WidthChanging;
        resultsList.ColumnWidthChanged += ResultsColumn_WidthChanged;
        resultsList.ClientSizeChanged += ResultList_SizeChanged;
        resultsList.ColumnClick += Column_Click;
        browsingTabs = new TabControl
        {
            Name = "browsingTabs", Dock = DockStyle.Fill, AccessibleName = "YouTube results and downloads",
            Size = new Size(1156, 340), Margin = new Padding(0, 0, 0, 4)
        };
        resultsTab = new TabPage { Name = "resultsTab", Text = "Results", Size = new Size(1148, 312), UseVisualStyleBackColor = true };
        downloadsTab = new TabPage { Name = "downloadsTab", Text = "Downloads", Size = new Size(1148, 312), UseVisualStyleBackColor = true };
        resultsTab.Controls.Add(mainSplit);
        browsingTabs.TabPages.AddRange([resultsTab, downloadsTab]);
        InitializeDownloadsPanel();
        rootLayout.Controls.Add(browsingTabs, 0, 2);
    }

    private void InitializeDownloadsPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var notice = new Label
        {
            AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 6),
            Text = "Download percentages measure network transfer; conversion and audio import are separate phases."
        };
        downloadJobsList = new ListView
        {
            Name = "downloadJobsList", AccessibleName = "Download jobs", Dock = DockStyle.Fill,
            View = View.Details, OwnerDraw = true, FullRowSelect = true, HideSelection = false,
            MultiSelect = true, UseCompatibleStateImageBehavior = false, ShowItemToolTips = true, Margin = Padding.Empty
        };
        downloadJobsList.Columns.AddRange([
            new ColumnHeader { Text = "Title", Width = 320 }, new ColumnHeader { Text = "Phase", Width = 190 },
            new ColumnHeader { Text = "Download", Width = 160 }, new ColumnHeader { Text = "Elapsed", Width = 90 },
            new ColumnHeader { Text = "ETA", Width = 100 }, new ColumnHeader { Text = "Speed", Width = 110 },
            new ColumnHeader { Text = "Transferred", Width = 180 }
        ]);
        downloadJobsList.DrawColumnHeader += JobList_DrawColumnHeader;
        downloadJobsList.DrawItem += JobList_DrawItem;
        downloadJobsList.DrawSubItem += JobList_DrawSubItem;
        downloadJobsList.ClientSizeChanged += JobList_SizeChanged;
        InitializeDownloadJobsMenu();
        layout.Controls.Add(notice, 0, 0);
        layout.Controls.Add(downloadJobsList, 0, 1);
        downloadsTab.Controls.Add(layout);
    }

    private void InitializeDetailsPanel()
    {
        detailsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        thumbnailCheck = new CheckBox { Text = "Show selected-video thumbnail", Checked = true, AutoSize = true };
        thumbnailPicture = new PictureBox
        {
            Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = SystemColors.ControlLight,
            AccessibleName = "Selected video thumbnail", TabStop = false
        };
        metadataButton = DesignerButton("Full metadata");
        metadataButton.AccessibleName = "Load upload date and full metadata for the selected video";
        metadataButton.Click += Metadata_Click;
        showRelatedButton = DesignerButton("Show related");
        showRelatedButton.Enabled = false;
        showRelatedButton.AccessibleName = "Show related and next-up videos for the selected video";
        showRelatedButton.Click += ShowRelated_Click;
        var detailActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        detailActions.Controls.AddRange([metadataButton, showRelatedButton]);
        descriptionText = new TextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window, AccessibleName = "Selected video metadata and description"
        };
        detailsLayout.Controls.Add(thumbnailCheck, 0, 0);
        detailsLayout.Controls.Add(thumbnailPicture, 0, 1);
        detailsLayout.Controls.Add(descriptionText, 0, 2);
        detailsLayout.Controls.Add(detailActions, 0, 3);
        mainSplit.Panel2.Controls.Add(detailsLayout);
        thumbnailCheck.CheckedChanged += Selection_Changed;
    }

    private void InitializeColumnsMenu()
    {
        columnsMenu = new ContextMenuStrip(components!);
        var enqueueItem = new ToolStripMenuItem("Download enqueue");
        enqueueItem.Click += DownloadEnqueue_Click;
        var openItem = new ToolStripMenuItem("Open in browser (no download)");
        var copyItem = new ToolStripMenuItem("Copy selected URLs");
        var relatedItem = new ToolStripMenuItem("Show related / next-up");
        var channelItem = new ToolStripMenuItem("Navigate to Channel");
        var playlistItem = new ToolStripMenuItem("Navigate to Playlist", null, NavigateToPlaylist_Click);
        playItem = new ToolStripMenuItem("Play", null, PlayStop_Click);
        channelItem.Click += NavigateToChannel_Click;
        relatedItem.Click += ShowRelated_Click;
        openItem.Click += Open_Click;
        copyItem.Click += Copy_Click;
        columnsMenu.Items.AddRange([playItem, new ToolStripSeparator(), enqueueItem, openItem, relatedItem, copyItem, channelItem, playlistItem, new ToolStripSeparator()]);
        string[] titles = ["Title", "Uploader", "Duration", "Views", "ID", "URL", "Upload Date"];
        columnMenuItems = new ToolStripMenuItem[titles.Length];
        for (int index = 0; index < titles.Length; index++)
        {
            var item = new ToolStripMenuItem("Show " + titles[index])
            {
                CheckOnClick = true, Checked = index < 4 || index == 6, Enabled = index != 0
            };
            item.CheckedChanged += Column_CheckedChanged;
            columnMenuItems[index] = item;
            columnsMenu.Items.Add(item);
        }
        resultsList.ContextMenuStrip = columnsMenu;
        columnsMenu.Opening += (sender, args) =>
        {
            enqueueItem.Enabled = CanEnqueueDownloads && SelectedVideos().Any(video => !video.IsChannel);
            playItem.Text = IsPlayingVideo(SelectedVideo()) ? "Stop" : "Play";
            playItem.Enabled = !closing && !busy && binariesReady && SelectedVideo() is { IsVideo: true };
            openItem.Enabled = !closing && SelectedVideo() is not null;
            copyItem.Enabled = !closing && resultsList.SelectedItems.Count > 0;
            relatedItem.Enabled = CanBrowse && SelectedVideo() is { IsVideo: true };
            channelItem.Enabled = CanBrowse && SelectedVideo() is { } selected && (!selected.IsPlaylist || selected.ChannelUrl is not null);
            playlistItem.Enabled = CanBrowse && SelectedVideo() is { IsPlaylist: true };
        };
        resultsList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var item = resultsList.GetItemAt(e.X, e.Y);
            if (item is null || item.Selected) return;
            resultsList.SelectedItems.Clear();
            item.Selected = true;
        };
    }

    private void InitializeOptionsPanel()
    {
        optionsGroup = new GroupBox { Text = "Download and RAM import options", Dock = DockStyle.Fill, AutoSize = false, Height = 210 };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var numbers = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        saveCheck = new CheckBox { Text = "Save converted audio", AutoSize = true, Margin = new Padding(3, 6, 10, 0) };
        formatCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, AccessibleName = "Audio format" };
        formatCombo.Items.AddRange(["WAV", "MP3"]);
        formatCombo.SelectedIndex = 0;
        audioQuality = new DomainUpDown { Name = "audioQuality", ReadOnly = true, Width = 70, Wrap = false };
        audioQualityLabel = new Label { Text = "Bit depth", AutoSize = true, Margin = new Padding(6, 7, 3, 0) };
        audioQuality.SelectedItemChanged += AudioQuality_Changed;
        cooldownSeconds = DesignerNumber(0, 300, 5, "Import cooldown in seconds");
        maxDurationMinutes = DesignerNumber(1, 1440, 30, "Maximum video duration in minutes");
        maxSizeMiB = DesignerNumber(1, 16384, 512, "Maximum download size in MiB");
        maxParallelDownloadsNumber = DesignerNumber(1, YtdlpBatchRunner.MaximumParallelism, 2, "Max parallel downloads");
        maxParallelDownloadsNumber.Name = "maxParallelDownloadsNumber";
        retryCountNumber = DesignerNumber(0, YtdlpRetryBudget.MaximumRetries, 2, "Additional automatic retry attempts per job");
        retryCountNumber.Name = "retryCountNumber";
        retryCountNumber.ValueChanged += (_, _) => { if (!restoringSettings) SaveSettings(); };
        numbers.Controls.Add(saveCheck);
        AddDesignerField(numbers, "Format", formatCombo);
        numbers.Controls.Add(audioQualityLabel);
        numbers.Controls.Add(audioQuality);
        AddDesignerField(numbers, "Wait (s)", cooldownSeconds);
        waitForBpmCheck = new CheckBox
        {
            Text = "Wait for BPM tag", AutoSize = true, Margin = new Padding(3, 6, 3, 0),
            AccessibleName = "Wait for a BPM tag; zero Wait seconds means no time limit"
        };
        waitForBpmCheck.CheckedChanged += WaitForBpm_CheckedChanged;
        numbers.Controls.Add(waitForBpmCheck);
        AddDesignerField(numbers, "Minutes", maxDurationMinutes);
        AddDesignerField(numbers, "MiB", maxSizeMiB);
        AddDesignerField(numbers, "Retries", retryCountNumber);
        AddDesignerField(numbers, "Parallel", maxParallelDownloadsNumber);
        layout.Controls.Add(numbers, 0, 0);
        InitializeDirectoryRow(layout);
        InitializeStretchRow(layout);
        InitializeNetworkRow(layout);
        storageLabel = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(3, 0, 3, 5) };
        layout.Controls.Add(storageLabel, 0, 4);
        // GroupBox auto-size measures wrapped rows without their final width and reserves excess height.
        layout.SizeChanged += (_, _) => optionsGroup.Height = layout.Bottom + optionsGroup.Padding.Bottom;
        optionsGroup.Controls.Add(layout);
        rootLayout.Controls.Add(optionsGroup, 0, 3);
        saveCheck.CheckedChanged += Options_Changed;
        formatCombo.SelectedIndexChanged += Options_Changed;
        var help = new ToolTip(components!) { IsParameterHelp = true };
        help.SetToolTip(formatCombo, "Converted download format: WAV is uncompressed PCM, MP3 is lossy compressed audio. The quality field remembers separate settings for each format.");
        help.SetToolTip(audioQuality, "PCM bit depth for WAV or bitrate in kbps for MP3. The up arrow increases quality. Higher settings use more space and cannot restore detail absent from YouTube's source.");
        help.SetToolTip(cooldownSeconds, "Wait after conversion before RAM import. 0 + Wait for BPM tag: wait without a time limit until a valid tag arrives or you cancel. 0 without that checkbox: import immediately. Positive values are a maximum tag wait; timeout permits import with an explicit warning.");
        help.SetToolTip(waitForBpmCheck, "Wait for an external BPM tagger before importing. Enabling this multiplies the current wait by four; disabling divides it by four. This does not perform an extra download.");
        help.SetToolTip(maxDurationMinutes, "Maximum allowed video duration for each download. Longer items are rejected by the downloader rather than silently trimmed.");
        help.SetToolTip(maxSizeMiB, "Maximum permitted downloaded/converted file size per item in MiB. A failed limit check is reported for that job.");
        help.SetToolTip(retryCountNumber, "Additional automatic attempts before showing failure/recovery UI. 0 disables these retries. Attempts retain the same options; cancellation and errors after audio handoff are never retried automatically. Running batches keep their captured retry count.");
        help.SetToolTip(maxParallelDownloadsNumber, "Maximum concurrent downloads plus conversion. Once conversion finishes, the slot is released: BPM-tag waits, cooldown and serialized audio import do not count toward this limit.");
        help.SetToolTip(ipVersionCombo, "Network address family used for YouTube requests. Automatic follows normal resolution; IPv4 or IPv6 forces that family without disabling TLS verification.");
        help.SetToolTip(proxyModeCombo, "Use the environment/default proxy, a direct connection, or an explicit HTTP(S) proxy. Explicit mode requires a valid proxy URL; credentials are not stored here.");
        help.SetToolTip(saveCheck, "Keep the original converted audio on disk in the output folder. Any time stretch is applied only to the RAM audio object, not the saved original.");
    }

    private void InitializeNetworkRow(TableLayoutPanel parent)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        ipVersionCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, AccessibleName = "Network IP version" };
        ipVersionCombo.Items.AddRange(["Automatic", "IPv4", "IPv6"]);
        ipVersionCombo.SelectedIndex = 0;
        proxyModeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, AccessibleName = "Proxy routing" };
        proxyModeCombo.Items.AddRange(["Environment / default", "Direct (no proxy)", "Explicit HTTP(S) proxy"]);
        proxyModeCombo.SelectedIndex = 0;
        proxyText = new TextBox { Width = 235, MaxLength = 2048, PlaceholderText = "https://proxy.example:8080", AccessibleName = "Explicit proxy URL without credentials", Enabled = false };
        proxyModeCombo.SelectedIndexChanged += (_, _) => proxyText.Enabled = proxyModeCombo.SelectedIndex == 2;
        AddDesignerField(row, "Network", ipVersionCombo);
        AddDesignerField(row, "Proxy", proxyModeCombo);
        row.Controls.Add(proxyText);
        var resetNetwork = DesignerButton("Reset network");
        resetNetwork.Click += ResetNetwork_Click;
        row.Controls.Add(resetNetwork);
        parent.Controls.Add(row, 0, 3);
    }

    private void InitializeDownloadJobsMenu()
    {
        var menu = new ContextMenuStrip(components!);
        var cancel = new ToolStripMenuItem("Cancel selected", null, CancelJobs_Click);
        var retry = new ToolStripMenuItem("Retry selected", null, RetryJobs_Click);
        var delete = new ToolStripMenuItem("Delete selected", null, DeleteJobs_Click);
        menu.Items.AddRange([cancel, retry, delete]);
        menu.Opening += (_, _) =>
        {
            var jobs = SelectedDownloadJobs();
            cancel.Enabled = jobs.Any(job => !job.Terminal && !job.HandedOff && !job.CancelRequested) && !closing;
            retry.Enabled = !busy && !closing && binariesReady && jobs.Any(CanRetryJob);
            delete.Enabled = !closing && jobs.Any(job => job.Terminal);
        };
        downloadJobsList.ContextMenuStrip = menu;
        downloadJobsList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var item = downloadJobsList.GetItemAt(e.X, e.Y);
            if (item is null || item.Selected) return;
            downloadJobsList.SelectedItems.Clear();
            item.Selected = true;
        };
    }

    private void InitializeDirectoryRow(TableLayoutPanel parent)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = "Output folder", AutoSize = true, Margin = new Padding(3, 7, 5, 0) }, 0, 0);
        outputDirectory = new TextBox { Dock = DockStyle.Fill, MaxLength = 2048, AccessibleName = "Saved audio output folder" };
        folderButton = DesignerButton("Browse...");
        folderDialog = new FolderBrowserDialog { Description = "Choose a folder for original converted audio", UseDescriptionForTitle = true };
        components!.Add(folderDialog);
        folderButton.Click += Folder_Click;
        row.Controls.Add(outputDirectory, 1, 0);
        row.Controls.Add(folderButton, 2, 0);
        parent.Controls.Add(row, 0, 1);
    }

    private void InitializeStretchRow(TableLayoutPanel parent)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        stretchCheck = new CheckBox { Text = "Time stretch imported audio", AutoSize = true, Margin = new Padding(3, 6, 6, 0) };
        stretchConfigButton = DesignerButton("Configure stretch...");
        stretchLabel = new Label { Text = "Time stretch is off", AutoSize = true, Margin = new Padding(6, 7, 0, 0) };
        row.Controls.AddRange([stretchCheck, stretchConfigButton, stretchLabel]);
        stretchCheck.CheckedChanged += Stretch_CheckedChanged;
        stretchConfigButton.Click += StretchConfig_Click;
        parent.Controls.Add(row, 0, 2);
    }

    private void InitializeActionPanel()
    {
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 3, 0, 3) };
        downloadButton = DesignerButton("Download + import");
        openButton = DesignerButton("Browser");
        copyButton = DesignerButton("Copy URL(s)");
        relatedButton = DesignerButton("Related search");
        autoplayCheck = new CheckBox
        {
            Name = "autoplayCheck", Text = "Autoplay", AutoSize = true,
            AccessibleName = "Play the first related video when the current audio ends",
            Margin = new Padding(6, 8, 6, 3)
        };
        autoplayCheck.CheckedChanged += Autoplay_CheckedChanged;
        uploaderButton = DesignerButton("Uploader search");
        var columnButton = DesignerButton("Columns...");
        updateButton = DesignerButton("Update tools");
        checkForUpdates = new CheckBox { Text = "Check for Updates", AutoSize = true, Name = "checkForUpdates", Margin = new Padding(6, 8, 6, 3) };
        checkForUpdates.CheckedChanged += CheckForUpdates_Changed;
        toolsFolderButton = DesignerButton("Tools folder...");
        toolsFolderButton.AccessibleName = "Choose the persistent folder for YouTube downloader tools";
        toolsFolderButton.Click += ToolsFolder_Click;
        cancelButton = DesignerButton("Cancel");
        foreach (Control action in new Control[] { downloadButton, openButton, copyButton, relatedButton, uploaderButton, columnButton,
            checkForUpdates, updateButton, toolsFolderButton, cancelButton }) action.Margin = new Padding(2, 3, 2, 3);
        actions.Controls.AddRange([downloadButton, openButton, copyButton, relatedButton,
            uploaderButton, autoplayCheck, columnButton, checkForUpdates, updateButton, toolsFolderButton, cancelButton]);
        downloadButton.Click += Download_Click;
        openButton.Click += Open_Click;
        copyButton.Click += Copy_Click;
        relatedButton.Click += Related_Click;
        uploaderButton.Click += Uploader_Click;
        columnButton.Click += (sender, args) => columnsMenu.Show(columnButton, new Point(0, columnButton.Height));
        updateButton.Click += Update_Click;
        cancelButton.Click += Cancel_Click;
        rootLayout.Controls.Add(actions, 0, 4);
    }

    private void InitializeStatusPanel()
    {
        activityLog = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window, AccessibleName = "Bounded YouTube operation activity log"
        };
        rootLayout.Controls.Add(activityLog, 0, 5);
        var status = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2 };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        status.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        status.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLabel = new Label
        {
            Text = "Waiting for the initial tool release check...", Dock = DockStyle.Fill,
            AutoEllipsis = true, Height = 24, TextAlign = ContentAlignment.MiddleLeft
        };
        operationProgress = new ProgressBar
        {
            Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Height = 18,
            Maximum = 1000, AccessibleName = "YouTube operation is running"
        };
        batchProgressLabel = new Label
        {
            Name = "batchProgressLabel", Text = "No active batch", AutoSize = true,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AccessibleName = "Batch progress"
        };
        var timing = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, MinimumSize = new Size(230, 27) };
        elapsedLabel = new Label
        {
            Name = "elapsedLabel", Text = "Elapsed: 00:00:00", AutoSize = true, AccessibleName = "Batch elapsed time"
        };
        etaLabel = new Label
        {
            Name = "etaLabel", Text = "ETA: unknown", AutoSize = true, AccessibleName = "Batch estimated time remaining"
        };
        timing.Controls.AddRange([elapsedLabel, etaLabel]);
        status.Controls.Add(statusLabel, 0, 0);
        status.SetColumnSpan(statusLabel, 3);
        status.Controls.Add(operationProgress, 0, 1);
        status.Controls.Add(batchProgressLabel, 1, 1);
        status.Controls.Add(timing, 2, 1);
        progressTimer = new System.Windows.Forms.Timer(components!) { Interval = 500 };
        progressTimer.Tick += ProgressTimer_Tick;
        rootLayout.Controls.Add(status, 0, 6);
    }

    private static Button DesignerButton(string text) => new()
    {
        Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(3, 1, 3, 1), UseVisualStyleBackColor = true
    };

    private static NumericUpDown DesignerNumber(int minimum, int maximum, int value, string accessibleName) => new()
    {
        Minimum = minimum, Maximum = maximum, Value = value, Width = 66, AccessibleName = accessibleName
    };

    private static void AddDesignerField(FlowLayoutPanel row, string text, Control control)
    {
        row.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(6, 6, 2, 0) });
        row.Controls.Add(control);
    }
}
