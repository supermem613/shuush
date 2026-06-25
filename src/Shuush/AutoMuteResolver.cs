namespace Shuush;

/// <summary>
/// Decides whether to auto-mute the microphone at the start of a meeting.
///
/// A meeting start is an observed not-in-call -> live edge: the previously
/// applied state was <see cref="MuteState.NoCall"/> and this poll resolved to
/// <see cref="MuteState.Live"/>. Muting only on that edge, and only while live,
/// is what makes the action fire once per meeting instead of every poll, and
/// never fight a manual unmute later in the same call.
///
/// Two starts are deliberately ignored because shuush did not continuously
/// observe them, so forcing mute would surprise the user: launching straight
/// into an already-running call (no prior applied state) and the first poll
/// after unpausing (detection had been stopped).
///
/// The intent is sticky: <see cref="ArmOrHoldPending"/> holds it across transient
/// UI Automation misses so one failed toggle at join time does not lose the mute
/// for the rest of the call.
/// </summary>
internal static class AutoMuteResolver
{
    /// <summary>
    /// Returns true when the opt-in auto-mute should flip a just-started meeting
    /// to muted. <paramref name="enabled"/> is the user setting,
    /// <paramref name="haveLast"/> and <paramref name="last"/> describe the
    /// previously applied state, <paramref name="wasPaused"/> is true on the first
    /// poll after unpausing, and <paramref name="state"/> is the state resolved for
    /// this poll.
    /// </summary>
    public static bool ShouldMuteAtMeetingStart(bool enabled, bool haveLast, bool wasPaused, MuteState last, MuteState state)
    {
        return enabled
            && haveLast
            && !wasPaused
            && last == MuteState.NoCall
            && state == MuteState.Live;
    }

    /// <summary>
    /// Carries the auto-mute intent forward across polls so a single transient
    /// <c>Toggle()</c> miss at join time does not permanently skip the mute. The
    /// intent is armed on the observed meeting-start edge and held while the call
    /// is still live and unmuted, and clears itself the moment the call reads muted
    /// (the action succeeded) or not-in-call (the call ended). Because both arming
    /// and holding require a live state, a true result always means "still live and
    /// owing a mute," so the caller can toggle whenever this returns true and let a
    /// confirmed mute clear the intent on the next poll.
    /// </summary>
    public static bool ArmOrHoldPending(bool enabled, bool haveLast, bool wasPaused, MuteState last, MuteState state, bool pending)
    {
        bool arm = ShouldMuteAtMeetingStart(enabled, haveLast, wasPaused, last, state);
        bool held = pending && state == MuteState.Live;
        return arm || held;
    }
}
