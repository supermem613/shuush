namespace Shuush;

/// <summary>
/// Decides the mute state to apply from a single poll, given that the cheap
/// microphone-activity registry gate already knows whether Teams is in a call.
///
/// UI Automation is the source of truth for live vs muted, but it reads from a
/// WebView2 tree that periodically replaces its accessibility nodes. During a
/// re-render the "microphone-button" element is momentarily unfindable and a poll
/// returns <see cref="MuteState.NoCall"/> even though the call is still up. The
/// registry gate does not blink like that. So while the mic is active, a single
/// NoCall poll is a transient miss, not a real call end. Holding the last in-call
/// state across that miss is what stops the LED and tray from flickering to the
/// not-in-call color and back.
/// </summary>
internal static class CallStateResolver
{
    /// <summary>
    /// Resolves the state to apply. <paramref name="micActive"/> is the registry
    /// gate, <paramref name="polled"/> is this poll's UIA result, and
    /// <paramref name="last"/> is the previously applied state when
    /// <paramref name="haveLast"/> is set.
    /// </summary>
    public static MuteState Resolve(bool micActive, MuteState polled, MuteState last, bool haveLast)
    {
        bool transientMiss = micActive
            && polled == MuteState.NoCall
            && haveLast
            && last != MuteState.NoCall;

        return transientMiss ? last : polled;
    }

    /// <summary>
    /// Chooses the UIA result that <see cref="Resolve"/> will see. The ConsentStore
    /// mic gate is an optimization, not the source of truth: a Live or Muted toolbar
    /// reading must not be replaced with NoCall when the registry is idle.
    /// </summary>
    public static MuteState SelectPolled(bool micActive, MuteState uia)
    {
        _ = micActive;
        return uia;
    }

    /// <summary>
    /// Chooses the poll-loop wait. Idle must stay on the configured poll interval so
    /// a missed ConsentStore still re-scans UIA. Infinite sleep never recovers.
    /// </summary>
    public static int NextWaitMs(bool micActive, int pollIntervalMs)
    {
        _ = micActive;
        return pollIntervalMs;
    }
}
