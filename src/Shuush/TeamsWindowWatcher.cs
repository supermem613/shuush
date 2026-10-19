using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Shuush;

/// <summary>
/// Wakes detection when Teams changes its windows or accessibility tree, even
/// when Windows has not recorded microphone use. Own on the UI message thread.
/// </summary>
internal sealed class TeamsWindowWatcher : IDisposable
{
    private readonly Action onChanged;
    private readonly Func<int, bool> isTeamsProcess;
    private GCHandle callbackRoot;
    private IntPtr foregroundHook;
    private IntPtr windowHook;

    public TeamsWindowWatcher(Action onChanged, Func<int, bool>? isTeamsProcess = null)
    {
        this.onChanged = onChanged;
        this.isTeamsProcess = isTeamsProcess ?? IsTeamsProcess;
        NativeMethods.WinEventCallback callback = this.OnWinEvent;
        this.callbackRoot = GCHandle.Alloc(callback);
        try
        {
            this.foregroundHook = Register(NativeMethods.WinEvent.Foreground, NativeMethods.WinEvent.Foreground, callback);
            this.windowHook = Register(NativeMethods.WinEvent.ObjectShow, NativeMethods.WinEvent.ObjectNameChange, callback);
        }
        catch
        {
            this.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Unhook(ref this.foregroundHook);
        Unhook(ref this.windowHook);
        if (this.callbackRoot.IsAllocated)
        {
            this.callbackRoot.Free();
        }
    }

    private static IntPtr Register(
        NativeMethods.WinEvent first, NativeMethods.WinEvent last, NativeMethods.WinEventCallback callback)
    {
        // WINEVENT_OUTOFCONTEXT (0) delivers callbacks on this thread's message loop.
        IntPtr hook = NativeMethods.SetWinEventHook(first, last, IntPtr.Zero, callback, 0, 0, 0);
        return hook != IntPtr.Zero
            ? hook
            : throw new InvalidOperationException("Could not register the Teams window-change watcher.");
    }

    private static void Unhook(ref IntPtr hook)
    {
        if (hook == IntPtr.Zero)
        {
            return;
        }

        if (!NativeMethods.UnhookWinEvent(hook))
        {
            throw new InvalidOperationException("Could not remove the Teams window-change watcher on its owning thread.");
        }

        hook = IntPtr.Zero;
    }

    private void OnWinEvent(
        IntPtr hook, NativeMethods.WinEvent eventType, IntPtr window, int objectId, int childId,
        uint threadId, uint eventTime)
    {
        if (window == IntPtr.Zero
            || eventType is not (NativeMethods.WinEvent.Foreground
                or NativeMethods.WinEvent.ObjectShow
                or NativeMethods.WinEvent.ObjectHide
                or NativeMethods.WinEvent.ObjectReorder
                or NativeMethods.WinEvent.ObjectNameChange))
        {
            return;
        }

        // Child accessibility events matter: the window can appear before its
        // microphone button, and that later toolbar change must wake detection.
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        if (processId != 0 && this.isTeamsProcess((int)processId))
        {
            this.onChanged();
        }
    }

    private static bool IsTeamsProcess(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName.Contains("teams", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Trace.TraceWarning("Cannot identify window owner {0}: {1}", processId, ex.Message);
            return false;
        }
    }
}
