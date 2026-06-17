using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Shuush;

/// <summary>
/// Reads and toggles the Microsoft Teams microphone state through UI Automation.
///
/// Contract (verified against new Teams / WebView2): a button with
/// AutomationId "microphone-button" exists across ms-teams windows while in a
/// call. Name "Mute mic" means live/unmuted; "Unmute mic" means muted; the
/// button is absent when not in a call.
///
/// The button element MUST be re-found on every poll. WebView2 replaces its
/// accessibility nodes on re-render, and a cached node keeps returning the OLD
/// Name without throwing, which would silently miss mute changes.
///
/// All members must be called from a single thread: the UIA3 COM client and the
/// elements it produces are not shared across threads here.
/// </summary>
internal sealed class TeamsMonitor : IDisposable
{
    private readonly UIA3Automation automation = new();
    private readonly Dictionary<int, bool> teamsPidCache = new();
    private AutomationElement? cachedWindow;
    private long pidCacheStampMs = Environment.TickCount64;

    private const long PidCacheTtlMs = 30_000;

    /// <summary>
    /// Re-finds the microphone button and maps it to a <see cref="MuteState"/>.
    /// Returns <see cref="MuteState.NoCall"/> when no in-call button is present.
    /// </summary>
    public MuteState Poll()
    {
        AutomationElement? mic = this.FindMicrophoneButton();
        if (mic is null)
        {
            return MuteState.NoCall;
        }

        string name;
        try
        {
            name = mic.Name ?? string.Empty;
        }
        catch (Exception ex) when (IsTransientUiaError(ex))
        {
            return MuteState.NoCall;
        }

        // "Unmute mic" is the action offered while muted; "Mute mic" while live.
        return name.StartsWith("Unmute", StringComparison.OrdinalIgnoreCase)
            ? MuteState.Muted
            : MuteState.Live;
    }

    /// <summary>
    /// Presses the microphone button to toggle mute. Returns false when no in-call
    /// button is present. Prefers the Invoke pattern and falls back to the legacy
    /// accessible default action.
    /// </summary>
    public bool Toggle()
    {
        AutomationElement? mic = this.FindMicrophoneButton();
        if (mic is null)
        {
            return false;
        }

        try
        {
            var invoke = mic.Patterns.Invoke.PatternOrDefault;
            if (invoke is not null)
            {
                invoke.Invoke();
                return true;
            }

            var legacy = mic.Patterns.LegacyIAccessible.PatternOrDefault;
            if (legacy is not null)
            {
                legacy.DoDefaultAction();
                return true;
            }
        }
        catch (Exception ex) when (IsTransientUiaError(ex))
        {
            return false;
        }

        return false;
    }

    public void Dispose() => this.automation.Dispose();

    private static bool IsTransientUiaError(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException
            or System.Runtime.InteropServices.InvalidComObjectException
            or TimeoutException
            or UnauthorizedAccessException;

    private AutomationElement? FindMicrophoneButton()
    {
        // Fast path: the button is usually in the same window as last poll. Search
        // it first to avoid enumerating and process-checking every top-level window.
        // The window element is cached, never the button: the button must be
        // re-found each poll because WebView2 swaps its accessibility nodes.
        if (this.cachedWindow is not null)
        {
            AutomationElement? cachedMic = TryFindIn(this.cachedWindow);
            if (cachedMic is not null)
            {
                return cachedMic;
            }

            this.cachedWindow = null;
        }

        AutomationElement[] windows;
        try
        {
            windows = this.automation.GetDesktop().FindAllChildren();
        }
        catch (Exception ex) when (IsTransientUiaError(ex))
        {
            return null;
        }

        foreach (AutomationElement window in windows)
        {
            if (!this.IsTeamsWindow(window))
            {
                continue;
            }

            AutomationElement? mic = TryFindIn(window);
            if (mic is not null)
            {
                this.cachedWindow = window;
                return mic;
            }
        }

        return null;
    }

    private static AutomationElement? TryFindIn(AutomationElement window)
    {
        try
        {
            return window.FindFirstDescendant(cf => cf.ByAutomationId("microphone-button"));
        }
        catch (Exception ex) when (IsTransientUiaError(ex))
        {
            return null;
        }
    }

    private bool IsTeamsWindow(AutomationElement window)
    {
        int pid;
        try
        {
            pid = window.Properties.ProcessId.ValueOrDefault;
        }
        catch (Exception ex) when (IsTransientUiaError(ex))
        {
            return false;
        }

        if (pid == 0)
        {
            return false;
        }

        // Expire the whole cache periodically so a recycled PID cannot keep an old
        // classification and hide a real Teams window or waste a scan on another app.
        long now = Environment.TickCount64;
        if (now - this.pidCacheStampMs > PidCacheTtlMs)
        {
            this.teamsPidCache.Clear();
            this.pidCacheStampMs = now;
        }

        if (this.teamsPidCache.TryGetValue(pid, out bool cached))
        {
            return cached;
        }

        bool isTeams = false;
        try
        {
            using Process proc = Process.GetProcessById(pid);
            string name = proc.ProcessName;
            isTeams = name.Contains("teams", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // Process already exited.
        }
        catch (InvalidOperationException)
        {
        }

        this.teamsPidCache[pid] = isTeams;
        return isTeams;
    }
}
