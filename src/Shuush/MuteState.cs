namespace Shuush;

/// <summary>
/// The mute state of Microsoft Teams as observed through UI Automation.
/// </summary>
internal enum MuteState
{
    /// <summary>No active call: the meeting microphone button is absent.</summary>
    NoCall,

    /// <summary>In a call and unmuted (microphone button reads "Mute mic").</summary>
    Live,

    /// <summary>In a call and muted (microphone button reads "Unmute mic").</summary>
    Muted,
}
