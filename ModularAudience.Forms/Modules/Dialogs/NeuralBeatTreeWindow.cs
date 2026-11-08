using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class NeuralBeatTreeWindow : Form
{
    private readonly AsyncBeatGraphEngine engine;
    private readonly Func<bool> canEdit;
    private readonly Func<string, BeatPatternRecord?, Task> audition;
    private readonly Action changed;
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersWidth = 100, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None };
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox name = new() { Width = 200, MaxLength = 200 };
    private readonly Label info = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(3), MaximumSize = new Size(1400, 0) };
    private readonly NumericUpDown mutation = new() { Minimum = 0, Maximum = 5, DecimalPlaces = 2, Value = .5m, Width = 70 };
    private BeatEngineSnapshot snapshot = null!;
    private bool filling;

    public NeuralBeatTreeWindow(AsyncBeatGraphEngine engine, Func<bool> canEdit,
        Func<string, BeatPatternRecord?, Task> audition, Action changed)
    {
        this.engine = engine; this.canEdit = canEdit; this.audition = audition; this.changed = changed;
        Text = "Neural Beat Engine — Training tree"; Size = new Size(1100, 760); MinimumSize = new Size(800, 550);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), RowCount = 3, ColumnCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        mode.Items.AddRange(["Sample-slot weights", "Hit-count / rest weights"]); mode.SelectedIndex = 0;
        tools.Controls.AddRange([Button("Refresh", (_, _) => RefreshTree()), mode, name,
            Button("Rename branch", (_, _) => Edit(() => { if (BranchId() is { } id) engine.RenameBranch(id, name.Text); })),
            new Label { Text = "Mutation", AutoSize = true, Margin = new Padding(4, 8, 3, 0) }, mutation,
            Button("Fork / remix", Fork_Click), Button("Trim branch...", Trim_Click), Button("Use / audition", Audition_Click)]);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1050, 600), SplitterDistance = 300, Panel1MinSize = 180, Panel2MinSize = 400 };
        split.Panel1.Controls.Add(tree); split.Panel2.Controls.Add(grid);
        root.Controls.Add(tools, 0, 0); root.Controls.Add(info, 0, 1); root.Controls.Add(split, 0, 2); Controls.Add(root);
        tree.AfterSelect += (_, _) => ShowSelection(); mode.SelectedIndexChanged += (_, _) => ShowSelection();
        grid.CellEndEdit += Grid_CellEndEdit;
        grid.DataError += (_, e) => { e.ThrowException = false; info.Text = "Enter a finite numeric weight within the session's configured bounds."; };
        RefreshTree();
    }

    private static Button Button(string text, EventHandler handler)
    { var button = new Button { Text = text, AutoSize = true }; button.Click += handler; return button; }
    private string? BranchId() => tree.SelectedNode?.Tag switch { BeatBranchSnapshot branch => branch.Id,
        BeatPatternRecord => (tree.SelectedNode.Parent?.Tag as BeatBranchSnapshot)?.Id, _ => null };

    internal void RefreshTree()
    {
        if (IsDisposed) return;
        string? selected = tree.SelectedNode?.Name;
        snapshot = engine.Snapshot();
        tree.BeginUpdate();
        try
        {
            tree.Nodes.Clear();
            var nodes = snapshot.Branches.ToDictionary(branch => branch.Id, branch => new TreeNode(
                $"{branch.Name} · r{branch.Revision} · {branch.FeedbackCount} ratings") { Name = branch.Id, Tag = branch });
            foreach (var branch in snapshot.Branches)
            {
                var node = nodes[branch.Id];
                foreach (var record in branch.Patterns)
                    node.Nodes.Add(new TreeNode($"Pattern {record.CreatedUtc.ToLocalTime():HH:mm:ss} · r{record.Revision} · {(record.Feedback is { } rating ? rating.ToString("P0") : "unrated")}")
                    { Name = record.Id, Tag = record });
                if (branch.ParentId is { } parent && nodes.TryGetValue(parent, out var owner)) owner.Nodes.Add(node);
                else tree.Nodes.Add(node);
            }
            tree.Nodes[0].Expand();
            tree.SelectedNode = selected is null ? tree.Nodes[0] : tree.Nodes.Find(selected, true).FirstOrDefault() ?? tree.Nodes[0];
        }
        finally { tree.EndUpdate(); }
        ShowSelection();
    }

    private void ShowSelection()
    {
        if (snapshot is null || BranchId() is not { } id) return;
        var branch = snapshot.Branches.First(item => item.Id == id);
        var record = tree.SelectedNode?.Tag as BeatPatternRecord;
        filling = true;
        try
        {
            name.Text = branch.Name;
            grid.Columns.Clear(); grid.Rows.Clear();
            bool density = mode.SelectedIndex == 1 && record is null;
            int columns = record is not null ? snapshot.Config.SampleCount : density ? snapshot.Config.MaximumHits + 1 : snapshot.Config.SampleCount;
            for (int column = 0; column < columns; column++)
            {
                var cell = new DataGridViewTextBoxColumn { Name = "c" + column, HeaderText = density ? column == 0 ? "Rest" : $"{column} hits" : $"Slot {column + 1}", Width = 85,
                    ValueType = typeof(float), ReadOnly = record is not null };
                grid.Columns.Add(cell);
            }
            grid.RowCount = snapshot.Config.TotalSteps;
            for (int step = 0; step < snapshot.Config.TotalSteps; step++)
            {
                grid.Rows[step].HeaderCell.Value = $"{step / (snapshot.Config.BeatsPerBar * snapshot.Config.StepsPerBeat) + 1}:{step % (snapshot.Config.BeatsPerBar * snapshot.Config.StepsPerBeat) + 1}";
                for (int column = 0; column < columns; column++)
                {
                    var cell = grid[column, step];
                    float value = record is not null ? record.Pattern[column][step] ? 1 : 0 : (density ? branch.DensityWeights : branch.Weights)[step][column];
                    cell.Value = value;
                    cell.Style.Format = record is not null ? "0" : "0.000";
                    cell.Style.BackColor = record is not null ? value > 0 ? Color.CornflowerBlue : Color.WhiteSmoke :
                        value >= 0 ? Color.FromArgb(230 - (int)Math.Min(100, value * 10), 245, 230) : Color.FromArgb(250, 230 - (int)Math.Min(100, -value * 10), 230);
                }
            }
            info.Text = record is null
                ? $"{branch.Id} · parent: {branch.ParentId ?? "none"} · mutation {branch.Mutation:0.##} · {branch.Patterns.Count} patterns · {branch.ManualEditCount} manual edits. Edit cells to tune logits ({snapshot.Config.MinWeight}…{snapshot.Config.MaxWeight}). Higher values are more likely. Rest/density and slot choices are learned separately."
                : $"Pattern {record.Id} · feedback: {(record.Feedback is { } rating ? rating.ToString("P0") : "not rated")} · {record.Pattern.Sum(row => row.Count(hit => hit))} hits. Read-only inspection; Fork creates a new branch seeded from this exact pattern. Use / audition plays it with the current sample slots.";
        }
        finally { filling = false; }
    }

    private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (filling || tree.SelectedNode?.Tag is not BeatBranchSnapshot branch) return;
        Edit(() =>
        {
            if (!float.TryParse(Convert.ToString(grid[e.ColumnIndex, e.RowIndex].Value), out float value)) throw new ArgumentException("Enter a numeric weight.");
            engine.SetWeight(branch.Id, e.RowIndex, e.ColumnIndex, value, mode.SelectedIndex == 1);
        });
    }

    private void Edit(Action action)
    {
        if (!canEdit()) { RefreshTree(); info.Text = "Stop playback / generation before editing the training tree."; return; }
        try { action(); changed(); RefreshTree(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Training tree"); RefreshTree(); }
    }

    private async void Fork_Click(object? sender, EventArgs e)
    {
        if (!canEdit() || BranchId() is not { } id) return;
        try
        {
            string child = tree.SelectedNode?.Tag is BeatPatternRecord record ? engine.ForkPattern(id, record.Id) :
                await engine.CreateRemixNodeAsync(id, (float)mutation.Value);
            changed(); RefreshTree(); tree.SelectedNode = tree.Nodes.Find(child, true).First();
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Fork training branch"); }
    }

    private void Trim_Click(object? sender, EventArgs e)
    {
        if (BranchId() is not { } id || id == "root") return;
        if (MessageBox.Show(this, "Remove this branch and all descendants? Parent weights are preserved; the removed branches are not unlearned from other branches.",
            "Trim training branch", MessageBoxButtons.OKCancel) == DialogResult.OK) Edit(() => engine.TrimBranch(id));
    }

    private async void Audition_Click(object? sender, EventArgs e)
    {
        if (!canEdit() || BranchId() is not { } id) return;
        try { await audition(id, tree.SelectedNode?.Tag as BeatPatternRecord); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Inspect beat pattern"); }
    }
}
