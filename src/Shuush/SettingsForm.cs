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
    private readonly CheckBox driveLedInput = new();
    private readonly CheckBox dimLedInput = new();
    private readonly CheckBox mirrorTrayInput = new();
    private readonly CheckBox autoMuteInput = new();
    private readonly ComboBox mutedColorInput = new();
    private readonly ComboBox liveColorInput = new();
    private readonly ComboBox noCallColorInput = new();
    private readonly ComboBox pausedColorInput = new();
    private readonly CheckBox startupInput = new();

    private AppConfig working = new();
    private int gridRow;
    private bool initializing;

    public SettingsForm()
    {
        this.Text = "shuush settings";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.AutoSize = true;
        this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        this.ShowInTaskbar = false;
        this.BuildLayout();
    }

    /// <summary>The edited config, valid only after an OK result.</summary>
    public AppConfig UpdatedConfig
    {
        get
        {
            this.UpdateWorkingFromInputs();
            return this.working;
        }
    }

    /// <summary>Raised when a mode color is changed and should be previewed immediately.</summary>
    public event Action<AppConfig, MuteState, bool>? PreviewChanged;

    /// <summary>Populates the dialog from a config to edit.</summary>
    public void Initialize(AppConfig config)
    {
        this.initializing = true;
        try
        {
            this.working = config;
            this.pollInput.Value = Math.Clamp(config.PollIntervalMs, (int)this.pollInput.Minimum, (int)this.pollInput.Maximum);
            this.driveLedInput.Checked = config.DriveLed;
            this.dimLedInput.Checked = config.DimLed;
            this.mirrorTrayInput.Checked = config.MirrorTrayColor;
            this.autoMuteInput.Checked = config.AutoMuteOnMeetingStart;
            this.mutedColorInput.SelectedItem = config.MutedColor;
            this.liveColorInput.SelectedItem = config.LiveColor;
            this.noCallColorInput.SelectedItem = config.NoCallColor;
            this.pausedColorInput.SelectedItem = config.PausedColor;
            this.startupInput.Checked = StartupManager.IsEnabled();
        }
        finally
        {
            this.initializing = false;
        }
    }

    private void BuildLayout()
    {
        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(14, 14, 14, 10),
        };

        // Column 0 sizes to the widest label so labels always get the room they need;
        // column 1 takes the rest for the fields.
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        this.pollInput.Minimum = 200;
        this.pollInput.Maximum = 5000;
        this.pollInput.Increment = 50;
        this.pollInput.Width = 90;
        this.AddFieldRow(grid, "Poll interval (ms, in call)", this.pollInput);

        this.mutedColorInput.DropDownStyle = ComboBoxStyle.DropDownList;
        this.mutedColorInput.Width = 150;
        this.mutedColorInput.Items.AddRange(LedPalette.Names.ToArray());
        this.mutedColorInput.SelectedIndexChanged += (_, _) => this.OnPreviewChanged(MuteState.Muted, isPaused: false);
        this.AddFieldRow(grid, "Muted color", this.mutedColorInput);

        this.liveColorInput.DropDownStyle = ComboBoxStyle.DropDownList;
        this.liveColorInput.Width = 150;
        this.liveColorInput.Items.AddRange(LedPalette.Names.ToArray());
        this.liveColorInput.SelectedIndexChanged += (_, _) => this.OnPreviewChanged(MuteState.Live, isPaused: false);
        this.AddFieldRow(grid, "Live color", this.liveColorInput);

        this.noCallColorInput.DropDownStyle = ComboBoxStyle.DropDownList;
        this.noCallColorInput.Width = 150;
        this.noCallColorInput.Items.AddRange(LedPalette.Names.ToArray());
        this.noCallColorInput.SelectedIndexChanged += (_, _) => this.OnPreviewChanged(MuteState.NoCall, isPaused: false);
        this.AddFieldRow(grid, "Not-in-call color", this.noCallColorInput);

        this.pausedColorInput.DropDownStyle = ComboBoxStyle.DropDownList;
        this.pausedColorInput.Width = 150;
        this.pausedColorInput.Items.AddRange(LedPalette.Names.ToArray());
        this.pausedColorInput.SelectedIndexChanged += (_, _) => this.OnPreviewChanged(MuteState.NoCall, isPaused: true);
        this.AddFieldRow(grid, "Paused color", this.pausedColorInput);

        this.autoMuteInput.Text = "Mute mic at the start of every meeting";
        this.AddCheckRow(grid, this.autoMuteInput);

        this.driveLedInput.Text = "Drive the MuteMe LED";
        this.AddCheckRow(grid, this.driveLedInput);

        this.dimLedInput.Text = "Dim the LED";
        this.AddCheckRow(grid, this.dimLedInput);

        this.mirrorTrayInput.Text = "Tray icon color follows mute state";
        this.AddCheckRow(grid, this.mirrorTrayInput);

        this.startupInput.Text = "Start with Windows";
        this.AddCheckRow(grid, this.startupInput);

        Button ok = new()
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(82, 27),
            Margin = new Padding(6, 0, 0, 0),
        };
        ok.Click += this.OnOk;

        Button cancel = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(82, 27),
            Margin = new Padding(6, 0, 0, 0),
        };

        // Right-to-left flow puts the first-added control at the right edge, so add
        // Cancel first to land OK to its left in the conventional [OK] [Cancel] order.
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(buttons, 0, this.gridRow);
        grid.SetColumnSpan(buttons, 2);
        this.gridRow++;

        this.AcceptButton = ok;
        this.CancelButton = cancel;
        this.Controls.Add(grid);
    }

    private void AddFieldRow(TableLayoutPanel grid, string text, Control field)
    {
        Label label = new()
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 16, 0),
        };
        field.Anchor = AnchorStyles.Left;
        field.Margin = new Padding(0, 4, 0, 4);

        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(label, 0, this.gridRow);
        grid.Controls.Add(field, 1, this.gridRow);
        this.gridRow++;
    }

    private void AddCheckRow(TableLayoutPanel grid, CheckBox box)
    {
        box.AutoSize = true;
        box.Anchor = AnchorStyles.Left;
        box.Margin = new Padding(0, 5, 0, 1);

        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(box, 0, this.gridRow);
        grid.SetColumnSpan(box, 2);
        this.gridRow++;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        this.UpdateWorkingFromInputs();
    }

    private void OnPreviewChanged(MuteState state, bool isPaused)
    {
        if (this.initializing)
        {
            return;
        }

        this.UpdateWorkingFromInputs();
        this.PreviewChanged?.Invoke(this.working.Clone(), state, isPaused);
    }

    private void UpdateWorkingFromInputs()
    {
        this.working.PollIntervalMs = (int)this.pollInput.Value;
        this.working.DriveLed = this.driveLedInput.Checked;
        this.working.DimLed = this.dimLedInput.Checked;
        this.working.MirrorTrayColor = this.mirrorTrayInput.Checked;
        this.working.AutoMuteOnMeetingStart = this.autoMuteInput.Checked;
        this.working.MutedColor = this.mutedColorInput.SelectedItem as string ?? "Red";
        this.working.LiveColor = this.liveColorInput.SelectedItem as string ?? "Green";
        this.working.NoCallColor = this.noCallColorInput.SelectedItem as string ?? "Off";
        this.working.PausedColor = this.pausedColorInput.SelectedItem as string ?? "Off";
        this.working.StartWithWindows = this.startupInput.Checked;
    }
}
