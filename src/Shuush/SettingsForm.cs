using System.Drawing;

namespace Shuush;

/// <summary>
/// Settings dialog. Edits a clone of the live config; the caller reads
/// <see cref="UpdatedConfig"/> only when the dialog returns <see cref="DialogResult.OK"/>.
/// Controls are built in code to keep the dialog a single file.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown pollInput = new();
    private readonly NumericUpDown idleInput = new();
    private readonly CheckBox driveLedInput = new();
    private readonly CheckBox dimLedInput = new();
    private readonly CheckBox mirrorTrayInput = new();
    private readonly ComboBox mutedColorInput = new();
    private readonly ComboBox liveColorInput = new();
    private readonly CheckBox startupInput = new();

    private AppConfig working = new();

    public SettingsForm()
    {
        this.Text = "shuush settings";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.ClientSize = new Size(340, 320);
        this.ShowInTaskbar = false;
        this.BuildLayout();
    }

    /// <summary>The edited config, valid only after an OK result.</summary>
    public AppConfig UpdatedConfig => this.working;

    /// <summary>Populates the dialog from a config to edit.</summary>
    public void Initialize(AppConfig config)
    {
        this.working = config;
        this.pollInput.Value = Math.Clamp(config.PollIntervalMs, (int)this.pollInput.Minimum, (int)this.pollInput.Maximum);
        this.idleInput.Value = Math.Clamp(config.IdleIntervalMs, (int)this.idleInput.Minimum, (int)this.idleInput.Maximum);
        this.driveLedInput.Checked = config.DriveLed;
        this.dimLedInput.Checked = config.DimLed;
        this.mirrorTrayInput.Checked = config.MirrorTrayColor;
        this.mutedColorInput.SelectedItem = config.MutedColor;
        this.liveColorInput.SelectedItem = config.LiveColor;
        this.startupInput.Checked = StartupManager.IsEnabled();
    }

    private void BuildLayout()
    {
        int y = 16;
        const int LabelX = 16;
        const int FieldX = 180;
        const int RowH = 30;

        AddLabel("Poll interval (ms, in call)", LabelX, y);
        this.pollInput.Minimum = 200;
        this.pollInput.Maximum = 5000;
        this.pollInput.Increment = 50;
        this.pollInput.SetBounds(FieldX, y - 2, 140, 24);
        this.Controls.Add(this.pollInput);
        y += RowH;

        AddLabel("Idle interval (ms, no call)", LabelX, y);
        this.idleInput.Minimum = 200;
        this.idleInput.Maximum = 10000;
        this.idleInput.Increment = 250;
        this.idleInput.SetBounds(FieldX, y - 2, 140, 24);
        this.Controls.Add(this.idleInput);
        y += RowH;

        AddLabel("Muted color", LabelX, y);
        this.mutedColorInput.DropDownStyle = ComboBoxStyle.DropDownList;
        this.mutedColorInput.SetBounds(FieldX, y - 2, 140, 24);
        this.mutedColorInput.Items.AddRange(LedPalette.Names.ToArray());
        this.Controls.Add(this.mutedColorInput);
        y += RowH;

        AddLabel("Live color", LabelX, y);
        this.liveColorInput.DropDownStyle = ComboBoxStyle.DropDownList;
        this.liveColorInput.SetBounds(FieldX, y - 2, 140, 24);
        this.liveColorInput.Items.AddRange(LedPalette.Names.ToArray());
        this.Controls.Add(this.liveColorInput);
        y += RowH + 4;

        this.driveLedInput.Text = "Drive the MuteMe LED";
        this.driveLedInput.SetBounds(LabelX, y, 300, 22);
        this.Controls.Add(this.driveLedInput);
        y += 26;

        this.dimLedInput.Text = "Dim the LED";
        this.dimLedInput.SetBounds(LabelX, y, 300, 22);
        this.Controls.Add(this.dimLedInput);
        y += 26;

        this.mirrorTrayInput.Text = "Tray icon color follows mute state";
        this.mirrorTrayInput.SetBounds(LabelX, y, 300, 22);
        this.Controls.Add(this.mirrorTrayInput);
        y += 26;

        this.startupInput.Text = "Start with Windows";
        this.startupInput.SetBounds(LabelX, y, 300, 22);
        this.Controls.Add(this.startupInput);

        Button ok = new()
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
        };
        ok.SetBounds(this.ClientSize.Width - 180, this.ClientSize.Height - 38, 78, 26);
        ok.Click += this.OnOk;

        Button cancel = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
        };
        cancel.SetBounds(this.ClientSize.Width - 96, this.ClientSize.Height - 38, 78, 26);

        this.Controls.Add(ok);
        this.Controls.Add(cancel);
        this.AcceptButton = ok;
        this.CancelButton = cancel;
    }

    private void AddLabel(string text, int x, int y)
    {
        Label label = new()
        {
            Text = text,
            AutoSize = true,
        };
        label.SetBounds(x, y, 160, 22);
        this.Controls.Add(label);
    }

    private void OnOk(object? sender, EventArgs e)
    {
        this.working.PollIntervalMs = (int)this.pollInput.Value;
        this.working.IdleIntervalMs = (int)this.idleInput.Value;
        this.working.DriveLed = this.driveLedInput.Checked;
        this.working.DimLed = this.dimLedInput.Checked;
        this.working.MirrorTrayColor = this.mirrorTrayInput.Checked;
        this.working.MutedColor = this.mutedColorInput.SelectedItem as string ?? "Red";
        this.working.LiveColor = this.liveColorInput.SelectedItem as string ?? "Green";
        this.working.StartWithWindows = this.startupInput.Checked;
    }
}
