#nullable enable

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class InstrumentSampleSynthDialog
{
    private System.ComponentModel.IContainer? components;
    private Button button_createOctave = null!;
    private Button button_ok = null!;
    private ContextMenuStrip octaveContextMenu = null!;
    private readonly List<ToolStripMenuItem> octaveOffsetItems = [];
    private readonly List<ToolStripMenuItem> octaveStepItems = [];

    private void InitializeSynthActionControls()
    {
        this.components = new System.ComponentModel.Container();
        this.button_createOctave = new Button
        {
            Name = "button_createOctave",
            Text = "Create Octave",
            AutoSize = true,
            UseVisualStyleBackColor = true
        };
        this.button_ok = new Button
        {
            Name = "button_ok",
            Text = "OK",
            AutoSize = true,
            UseVisualStyleBackColor = true
        };
        this.octaveContextMenu = new ContextMenuStrip(this.components);
        ToolStripMenuItem offsetMenu = new("Offset");
        foreach ((string label, double value) in OctaveOffsets)
        {
            ToolStripMenuItem item = new(label) { Tag = value };
            item.Click += (_, _) => this.SetOctaveOffset((double)item.Tag!);
            this.octaveOffsetItems.Add(item);
            offsetMenu.DropDownItems.Add(item);
        }

        ToolStripMenuItem stepMenu = new("Step");
        foreach ((string label, double value) in OctaveSteps)
        {
            ToolStripMenuItem item = new(label) { Tag = value };
            item.Click += (_, _) => this.SetOctaveStep((double)item.Tag!);
            this.octaveStepItems.Add(item);
            stepMenu.DropDownItems.Add(item);
        }

        this.octaveContextMenu.Items.Add(offsetMenu);
        this.octaveContextMenu.Items.Add(stepMenu);
        this.octaveContextMenu.Opening += (_, _) => this.UpdateOctaveMenuChecks();
        this.button_createOctave.ContextMenuStrip = this.octaveContextMenu;
        this.button_createOctave.Click += async (_, _) => await this.CreateOctaveAsync();
        this.button_ok.Click += (_, _) => this.Confirm();
        this.tips.SetToolTip(this.button_createOctave,
            "Click to render an octave of individual notes. Right-click to choose its starting transpose and semitone step.");
    }

}
