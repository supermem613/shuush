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
}
