namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class VocalCustomSamplesDialog
{
    private DataGridView mappingsGrid = null!;
    private DataGridViewTextBoxColumn ipaColumn = null!;
    private DataGridViewComboBoxColumn audioColumn = null!;
    private Button refreshSourcesButton = null!;
    private Label instructionsLabel = null!;
    private Label statusLabel = null!;

    private void InitializeComponent()
    {
        mappingsGrid = new DataGridView();
        ipaColumn = new DataGridViewTextBoxColumn();
        audioColumn = new DataGridViewComboBoxColumn();
        refreshSourcesButton = new Button();
        instructionsLabel = new Label();
        statusLabel = new Label();
        var topPanel = new FlowLayoutPanel();
        var bottomPanel = new Panel();
        ((System.ComponentModel.ISupportInitialize)mappingsGrid).BeginInit();
        SuspendLayout();

        topPanel.Dock = DockStyle.Top;
        topPanel.AutoSize = true;
        topPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        topPanel.Padding = new Padding(6);
        topPanel.WrapContents = false;
        instructionsLabel.AutoSize = true;
        instructionsLabel.Margin = new Padding(3, 8, 8, 0);
        instructionsLabel.Text = "Map an IPA sequence to audio shorter than one minute. Changes apply immediately.";
        refreshSourcesButton.AutoSize = true;
        refreshSourcesButton.Name = "refreshSourcesButton";
        refreshSourcesButton.Text = "Refresh sources";
        refreshSourcesButton.Click += RefreshSourcesButton_Click;
        topPanel.Controls.Add(instructionsLabel);
        topPanel.Controls.Add(refreshSourcesButton);

        mappingsGrid.AllowUserToAddRows = true;
        mappingsGrid.AllowUserToDeleteRows = true;
        mappingsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        mappingsGrid.Dock = DockStyle.Fill;
        mappingsGrid.Name = "mappingsGrid";
        mappingsGrid.AllowDrop = true;
        ipaColumn.HeaderText = "IPA substring";
        ipaColumn.Name = "Ipa";
        ipaColumn.FillWeight = 35;
        audioColumn.HeaderText = "Audio sample";
        audioColumn.Name = "Audio";
        audioColumn.FillWeight = 65;
        audioColumn.ValueType = typeof(Guid);
        mappingsGrid.Columns.AddRange([ipaColumn, audioColumn]);
        mappingsGrid.CellEndEdit += MappingsGrid_CellEndEdit;
        mappingsGrid.CellValueChanged += MappingsGrid_CellValueChanged;
        mappingsGrid.CurrentCellDirtyStateChanged += MappingsGrid_CurrentCellDirtyStateChanged;
        mappingsGrid.DefaultValuesNeeded += MappingsGrid_DefaultValuesNeeded;
        mappingsGrid.UserDeletingRow += MappingsGrid_UserDeletingRow;
        mappingsGrid.DataError += MappingsGrid_DataError;
        mappingsGrid.DragEnter += MappingsGrid_DragEnter;
        mappingsGrid.DragOver += MappingsGrid_DragOver;
        mappingsGrid.DragDrop += MappingsGrid_DragDrop;

        bottomPanel.Dock = DockStyle.Bottom;
        bottomPanel.Height = 30;
        bottomPanel.Padding = new Padding(6, 4, 6, 2);
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.AutoEllipsis = true;
        statusLabel.Text = "Enter IPA such as /tʃ/; drop audio from an Audio Collection to assign it.";
        bottomPanel.Controls.Add(statusLabel);

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(720, 420);
        Controls.Add(mappingsGrid);
        Controls.Add(bottomPanel);
        Controls.Add(topPanel);
        MinimumSize = new Size(560, 320);
        Name = "VocalCustomSamplesDialog";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Custom Samples";
        ((System.ComponentModel.ISupportInitialize)mappingsGrid).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }
}
