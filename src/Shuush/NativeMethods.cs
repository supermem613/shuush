using System.Runtime.InteropServices;

namespace Shuush;

/// <summary>
/// Win32 entry points used for tray-icon resource management.
/// </summary>
internal static class NativeMethods
{
    /// <summary>
    /// Releases the icon handle produced by <see cref="System.Drawing.Bitmap.GetHicon"/>.
    /// Each GetHicon call allocates a user object; without DestroyIcon the app leaks
    /// one handle per tray-icon refresh.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr handle);
}
