namespace ModularAudience.Forms.Modules;

public partial class TrackView
{
    private CheckBox? checkBox_join;
    private bool suppressJoin;
    internal MixTrackView? JoinedMix { get; private set; }
    private void InitializeJoinControls()
    {
        this.checkBox_sync.Text = "Sync";
        this.checkBox_join = new CheckBox { Text = "Join", Name = "checkBox_join", AutoSize = true,
            Font = this.checkBox_sync.Font, Location = new Point(62, this.checkBox_sync.Top), TabStop = false };
        this.Controls.Add(this.checkBox_join);
        this.checkBox_join.CheckedChanged += (_, _) =>
        {
            if (this.suppressJoin) return;
            if (this.checkBox_join.Checked)
            {
                var mix = MixTrackView.OpenMixes.LastOrDefault() ?? new MixTrackView(); mix.Join(this);
            }
            else this.LeaveCurrentMix();
        };
        var menu = new ContextMenuStrip(); this.checkBox_join.ContextMenuStrip = menu;
        menu.Opening += (_, _) =>
        {
            while (menu.Items.Count > 0) { var old = menu.Items[0]; menu.Items.RemoveAt(0); old.Dispose(); }
            menu.Items.Add("Create new Mix-TrackView", null, (_, _) => new MixTrackView().Join(this));
            foreach (var mix in MixTrackView.OpenMixes)
            {
                var item = new ToolStripMenuItem(mix.Arrangement.Name) { Checked = ReferenceEquals(mix, this.JoinedMix) };
                item.Click += (_, _) => mix.Join(this); menu.Items.Add(item);
            }
            menu.Items.Add("Open Mix project…", null, async (_, _) => { var mix = new MixTrackView(); mix.Show(); await mix.OpenProjectAsync(); });
        };
        this.FormClosed += (_, _) => menu.Dispose();
        this.toolTip_trackView.SetToolTip(this.checkBox_join, "Join a multi-track arrangement. Right-click to choose/create a mix. Original audio is preserved.");
    }
    internal void SetJoinedMix(MixTrackView? mix)
    {
        JoinedMix = mix; suppressJoin = true;
        if (checkBox_join != null) checkBox_join.Checked = mix != null;
        suppressJoin = false;
    }
    internal void LeaveCurrentMix()
    {
        var mix = JoinedMix; if (mix == null || mix.IsDisposed) { SetJoinedMix(null); return; }
        mix.Detach(this); SetJoinedMix(null);
        if (!IsDisposed && !Disposing) Show();
    }
}
