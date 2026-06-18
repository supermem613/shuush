using System.Drawing;

namespace Shuush;

/// <summary>
/// The tray application. Owns the MuteMe device, a background poll thread that
/// reads Teams state through UI Automation, the colored tray icon, and the
/// context-menu flyout.
///
/// Threading: a single background thread owns the <see cref="TeamsMonitor"/> and
/// performs every UIA call, because the UIA client and its elements are used on
/// one thread only. The poll thread drives the LED directly (HID writes are
/// thread-safe) and marshals tray-icon updates back to the UI thread through the
/// captured <see cref="SynchronizationContext"/>.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon notifyIcon = new();
    private readonly ContextMenuStrip menu = new();
    private readonly MuteMeDevice muteMe = new();
    private readonly System.Windows.Forms.Timer bootstrap = new() { Interval = 1 };
    private readonly AutoResetEvent pollWake = new(false);

    private readonly ToolStripMenuItem statusItem = new("Teams: starting...") { Enabled = false };
    private readonly ToolStripMenuItem deviceItem = new("MuteMe: ...") { Enabled = false };
    private readonly ToolStripMenuItem toggleItem = new("Toggle mute");
    private readonly ToolStripMenuItem pauseItem = new("Pause") { CheckOnClick = false };
    private readonly ToolStripMenuItem startupItem = new("Start with Windows") { CheckOnClick = false };
    private readonly ToolStripMenuItem settingsItem = new("Settings...");
    private readonly ToolStripMenuItem exitItem = new("Exit");

    private volatile AppConfig config;
    private SynchronizationContext uiContext = null!;
    private Thread? pollThread;
    private volatile bool running;
    private volatile bool paused;
    private int toggleRequested;
    private int forceApply;
    private bool disposed;

    public TrayContext()
    {
        this.config = AppConfig.Load();

        this.BuildMenu();

        this.notifyIcon.Text = "shuush";
        this.notifyIcon.Icon = TrayIconRenderer.Create(Color.DimGray, false);
        this.notifyIcon.ContextMenuStrip = this.menu;
        this.notifyIcon.Visible = true;
        this.notifyIcon.DoubleClick += this.OnSettingsClick;

        this.muteMe.Tapped += this.OnDeviceTapped;

        // The WindowsForms SynchronizationContext is only installed once the
        // message loop is running, so capture it and start polling on the first
        // bootstrap tick rather than in the constructor.
        this.bootstrap.Tick += this.OnBootstrapTick;
        this.bootstrap.Start();
    }

    private void BuildMenu()
    {
        this.toggleItem.Click += this.OnToggleClick;
        this.pauseItem.Click += this.OnPauseClick;
        this.startupItem.Click += this.OnStartupClick;
        this.settingsItem.Click += this.OnSettingsClick;
        this.exitItem.Click += this.OnExitClick;

        this.startupItem.Checked = StartupManager.IsEnabled();

        this.menu.Items.Add(this.statusItem);
        this.menu.Items.Add(this.deviceItem);
        this.menu.Items.Add(new ToolStripSeparator());
        this.menu.Items.Add(this.toggleItem);
        this.menu.Items.Add(this.pauseItem);
        this.menu.Items.Add(new ToolStripSeparator());
        this.menu.Items.Add(this.startupItem);
        this.menu.Items.Add(this.settingsItem);
        this.menu.Items.Add(new ToolStripSeparator());
        this.menu.Items.Add(this.exitItem);
    }

    private void OnBootstrapTick(object? sender, EventArgs e)
    {
        this.bootstrap.Stop();
        this.uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        this.running = true;
        this.pollThread = new Thread(this.PollLoop)
        {
            IsBackground = true,
            Name = "shuush-poll",
        };
        this.pollThread.Start();
    }

    private void PollLoop()
    {
        try
        {
            using TeamsMonitor monitor = new();

            // A registry-change watcher wakes this loop the instant Teams acquires or
            // releases the microphone, so an idle loop blocks on the safety-scan backstop
            // and is woken early by a real call transition rather than re-reading the
            // consent store on a short timer.
            using MicActivityWatcher micWatcher = MicActivityWatcher.Create(() => this.pollWake.Set());

            MuteState last = MuteState.NoCall;
            bool haveLast = false;
            bool wasPaused = false;
            bool wasConnected = false;

            while (this.running)
            {
                bool force = Interlocked.Exchange(ref this.forceApply, 0) == 1;

                // A connect/disconnect transition must reapply so the LED and the
                // "MuteMe not found" status do not stay stale after a replug.
                this.muteMe.EnsureOpen();
                bool connected = this.muteMe.IsConnected;
                if (connected != wasConnected)
                {
                    force = true;
                    wasConnected = connected;
                }

                // Keepalive: the MuteMe stops streaming touch input when it stops
                // receiving output reports, so re-send the LED state every loop so a
                // physical tap is still detected during a call whose color is static.
                this.muteMe.Refresh();

                if (this.paused)
                {
                    this.muteMe.Set(MuteMeDevice.Off);
                    if (force || !wasPaused || !haveLast)
                    {
                        this.PostTrayUpdate(MuteState.NoCall, isPaused: true);
                    }

                    wasPaused = true;
                    haveLast = true;
                    this.pollWake.WaitOne(Timeout.Infinite);
                    continue;
                }

                if (Interlocked.Exchange(ref this.toggleRequested, 0) == 1)
                {
                    MuteState before = haveLast ? last : SafePoll(monitor);
                    if (before != MuteState.NoCall && monitor.Toggle())
                    {
                        // Instant feedback: drive the LED and tray to the predicted
                        // flipped state right away so a tap does not wait for a UIA
                        // round-trip. The confirm poll below corrects it on the rare
                        // chance Teams did not actually flip.
                        MuteState predicted = before == MuteState.Muted ? MuteState.Live : MuteState.Muted;
                        this.ApplyState(predicted);
                        last = predicted;
                        haveLast = true;

                        for (int i = 0; i < 25 && this.running; i++)
                        {
                            MuteState current = SafePoll(monitor);
                            if (current != MuteState.NoCall && current != before)
                            {
                                if (current != predicted)
                                {
                                    this.ApplyState(current);
                                    last = current;
                                }

                                break;
                            }

                            this.pollWake.WaitOne(40);
                        }
                    }
                }

                // Only walk the Teams accessibility tree when Teams is actually
                // capturing the microphone (in a call). When it is not, a full UIA scan
                // would burn CPU only to return NoCall, so report NoCall directly and let
                // the registry watcher wake us the moment a call begins.
                bool micActive = CallActivityProbe.IsTeamsMicActive();
                MuteState state = micActive ? SafePoll(monitor) : MuteState.NoCall;

                if (force || wasPaused || !haveLast || state != last)
                {
                    this.ApplyState(state);
                    last = state;
                    haveLast = true;
                }

                wasPaused = false;

                // In a call, poll fast for the UIA mute state. Idle, block until the
                // registry watcher signals a call start or a menu action wakes the loop,
                // so idle costs zero CPU with no timed wakeups.
                int interval = micActive ? this.config.PollIntervalMs : Timeout.Infinite;
                this.pollWake.WaitOne(interval);
            }
        }
        finally
        {
            // Turn the LED off and release the device on the thread that owns LED
            // writes, after the loop has fully exited.
            this.muteMe.Close();
        }
    }

    private static MuteState SafePoll(TeamsMonitor monitor)
    {
        try
        {
            return monitor.Poll();
        }
        catch (Exception)
        {
            return MuteState.NoCall;
        }
    }

    private void ApplyState(MuteState state)
    {
        AppConfig cfg = this.config;
        byte command = MuteMeDevice.Off;
        if (cfg.DriveLed)
        {
            command = state switch
            {
                MuteState.Muted => LedPalette.ToCommand(cfg.MutedColor),
                MuteState.Live => LedPalette.ToCommand(cfg.LiveColor),
                _ => MuteMeDevice.Off,
            };

            if (cfg.DimLed && command != MuteMeDevice.Off)
            {
                command |= MuteMeDevice.DimBit;
            }
        }

        this.muteMe.Set(command);
        this.PostTrayUpdate(state, isPaused: false);
    }

    private void PostTrayUpdate(MuteState state, bool isPaused)
    {
        this.uiContext.Post(_ => this.UpdateTray(state, isPaused), null);
    }

    private void UpdateTray(MuteState state, bool isPaused)
    {
        if (this.disposed)
        {
            return;
        }

        AppConfig cfg = this.config;
        Color fill;
        string label;
        if (isPaused)
        {
            fill = Color.FromArgb(120, 120, 120);
            label = "Paused";
        }
        else
        {
            label = state switch
            {
                MuteState.Muted => "Muted",
                MuteState.Live => "Live",
                _ => "Not in a call",
            };

            fill = !cfg.MirrorTrayColor
                ? Color.DimGray
                : state switch
                {
                    MuteState.Muted => LedPalette.ToColor(cfg.MutedColor),
                    MuteState.Live => LedPalette.ToColor(cfg.LiveColor),
                    _ => Color.FromArgb(110, 110, 110),
                };
        }

        Icon icon = TrayIconRenderer.Create(fill, isPaused);
        Icon? previous = this.notifyIcon.Icon;
        this.notifyIcon.Icon = icon;
        previous?.Dispose();

        bool connected = this.muteMe.IsConnected;
        string suffix = connected ? string.Empty : "  (MuteMe not found)";
        string text = $"shuush \u2014 {label}{suffix}";
        this.notifyIcon.Text = text.Length > 63 ? text[..63] : text;

        this.statusItem.Text = isPaused ? "Teams: paused" : $"Teams: {label}";
        this.deviceItem.Text = connected ? "MuteMe: connected" : "MuteMe: not found";
        this.toggleItem.Enabled = !isPaused && state != MuteState.NoCall;
        this.pauseItem.Checked = isPaused;
    }

    private void OnDeviceTapped() => this.RequestToggle();

    private void OnToggleClick(object? sender, EventArgs e) => this.RequestToggle();

    private void RequestToggle()
    {
        if (this.paused)
        {
            return;
        }

        Interlocked.Exchange(ref this.toggleRequested, 1);
        this.pollWake.Set();
    }

    private void OnPauseClick(object? sender, EventArgs e)
    {
        this.paused = !this.paused;
        Interlocked.Exchange(ref this.forceApply, 1);
        this.pollWake.Set();
    }

    private void OnStartupClick(object? sender, EventArgs e)
    {
        bool enable = !this.startupItem.Checked;
        StartupManager.SetEnabled(enable);
        this.startupItem.Checked = StartupManager.IsEnabled();
        this.config.StartWithWindows = this.startupItem.Checked;
        this.config.Save();
    }

    private void OnSettingsClick(object? sender, EventArgs e)
    {
        using SettingsForm form = new();
        form.Initialize(this.config.Clone());
        if (form.ShowDialog() == DialogResult.OK)
        {
            this.config = form.UpdatedConfig;
            this.config.Save();
            StartupManager.SetEnabled(this.config.StartWithWindows);
            this.startupItem.Checked = StartupManager.IsEnabled();
            Interlocked.Exchange(ref this.forceApply, 1);
            this.pollWake.Set();
        }
    }

    private void OnExitClick(object? sender, EventArgs e) => this.ExitThread();

    protected override void Dispose(bool disposing)
    {
        if (!this.disposed && disposing)
        {
            this.disposed = true;
            this.running = false;
            this.pollWake.Set();

            // Wait for the poll thread to actually exit before tearing down shared
            // state it touches (pollWake, the MuteMe device). If a UIA call is wedged
            // past the timeout, leave those objects for process teardown rather than
            // disposing them out from under a live thread.
            bool threadStopped = this.pollThread is null || this.pollThread.Join(4000);

            this.muteMe.Tapped -= this.OnDeviceTapped;
            if (this.pollThread is null)
            {
                // The poll loop never ran, so its finally never closed the device.
                this.muteMe.Close();
            }

            this.bootstrap.Dispose();
            if (threadStopped)
            {
                this.muteMe.Dispose();
                this.pollWake.Dispose();
            }

            this.notifyIcon.Visible = false;
            Icon? icon = this.notifyIcon.Icon;
            this.notifyIcon.Icon = null;
            icon?.Dispose();
            this.notifyIcon.Dispose();
            this.menu.Dispose();
        }

        base.Dispose(disposing);
    }
}
