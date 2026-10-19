using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Shuush;

/// <summary>
/// Win32 entry points used for tray-icon resource management and registry-change
/// notifications and Teams window events.
/// </summary>
internal static class NativeMethods
{
    internal enum WinEvent : uint
    {
        Foreground = 0x0003,
        ObjectShow = 0x8002,
        ObjectHide = 0x8003,
        ObjectReorder = 0x8004,
        ObjectNameChange = 0x800C,
    }

    internal delegate void WinEventCallback(
        IntPtr hook, WinEvent eventType, IntPtr window, int objectId, int childId,
        uint threadId, uint eventTime);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(
        WinEvent eventMin, WinEvent eventMax, IntPtr module, WinEventCallback callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>
    /// Releases the icon handle produced by <see cref="System.Drawing.Bitmap.GetHicon"/>.
    /// Each GetHicon call allocates a user object; without DestroyIcon the app leaks
    /// one handle per tray-icon refresh.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Change types a registry-change notification can report.</summary>
    [Flags]
    internal enum RegNotifyFilter : uint
    {
        /// <summary>A subkey was added or deleted.</summary>
        Name = 0x00000001,

        /// <summary>A value was added, deleted, or changed.</summary>
        LastSet = 0x00000004,

        /// <summary>
        /// Deliver the notification independent of the calling thread's lifetime.
        /// Without this the kernel cancels the watch if the arming thread exits, which
        /// is fragile for a long-lived watcher; with it the watch survives.
        /// </summary>
        ThreadAgnostic = 0x10000000,
    }

    /// <summary>
    /// Asks the kernel to signal <paramref name="eventHandle"/> the next time the key
    /// (or, with <paramref name="watchSubtree"/>, any descendant) changes. The watch is
    /// one-shot: it must be re-armed after each signal. The key must stay open for the
    /// asynchronous notification to remain valid.
    /// </summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        RegNotifyFilter notifyFilter,
        SafeWaitHandle eventHandle,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
