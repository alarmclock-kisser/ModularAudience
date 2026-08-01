namespace ModularAudience.Forms.Modules.Dialogs;

partial class ProgressDialog
{
    private System.ComponentModel.IContainer components = null;
    private Label label_operation;
    private ProgressBar progressBar;
    private Label label_percent;
    private Label label_elapsed;
    private Label label_proposed;
    private System.Windows.Forms.Timer timer_elapsed;
    private System.Windows.Forms.Timer timer_close;

    protected override void Dispose(bool disposing)
    {
        if (disposing && this.components != null)
        {
            this.components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.label_operation = new Label();
        this.progressBar = new ProgressBar();
        this.label_percent = new Label();
        this.label_elapsed = new Label();
        this.label_proposed = new Label();
        this.timer_elapsed = new System.Windows.Forms.Timer(this.components);
        this.timer_close = new System.Windows.Forms.Timer(this.components);
        this.SuspendLayout();
        // 
        // label_operation
        // 
        this.label_operation.AutoSize = true;
        this.label_operation.Location = new Point(12, 12);
        this.label_operation.Name = "label_operation";
        this.label_operation.Size = new Size(70, 15);
        this.label_operation.TabIndex = 0;
        this.label_operation.Text = "Processing...";
        // 
        // progressBar
        // 
        this.progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        this.progressBar.Location = new Point(12, 38);
        this.progressBar.Name = "progressBar";
        this.progressBar.Size = new Size(376, 23);
        this.progressBar.TabIndex = 1;
        // 
        // label_percent
        // 
        this.label_percent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.label_percent.Location = new Point(329, 42);
        this.label_percent.Name = "label_percent";
        this.label_percent.Size = new Size(59, 15);
        this.label_percent.TabIndex = 2;
        this.label_percent.Text = "0%";
        this.label_percent.TextAlign = ContentAlignment.MiddleRight;
        // 
        // label_elapsed
        // 
        this.label_elapsed.AutoSize = true;
        this.label_elapsed.Location = new Point(12, 76);
        this.label_elapsed.Name = "label_elapsed";
        this.label_elapsed.Size = new Size(100, 15);
        this.label_elapsed.TabIndex = 3;
        this.label_elapsed.Text = "Elapsed: 00:00.000";
        // 
        // label_proposed
        // 
        this.label_proposed.AutoSize = true;
        this.label_proposed.Location = new Point(12, 99);
        this.label_proposed.Name = "label_proposed";
        this.label_proposed.Size = new Size(145, 15);
        this.label_proposed.TabIndex = 4;
        this.label_proposed.Text = "Proposed time: --:--.---";
        // 
        // timer_elapsed
        // 
        this.timer_elapsed.Interval = 50;
        this.timer_elapsed.Tick += this.timer_elapsed_Tick;
        // 
        // timer_close
        // 
        this.timer_close.Tick += this.timer_close_Tick;
        // 
        // ProgressDialog
        // 
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(400, 132);
        this.Controls.Add(this.label_proposed);
        this.Controls.Add(this.label_elapsed);
        this.Controls.Add(this.label_percent);
        this.Controls.Add(this.progressBar);
        this.Controls.Add(this.label_operation);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Name = "ProgressDialog";
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Text = "Progress";
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
