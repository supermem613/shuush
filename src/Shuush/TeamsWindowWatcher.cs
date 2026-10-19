using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Shuush;

/// <summary>
/// Wakes detection when Teams changes its windows or accessibility tree, even
/// when Windows has not recorded microphone use. Own on the UI message thread.
/// </summary>
internal sealed class TeamsWindowWatcher : NativeWindow, IDisposable
{
    private readonly Action onChanged;
    private readonly Func<int, bool> isTeamsProcess;
    private readonly StringBuilder processImage = new();
    private GCHandle callbackRoot;
    private readonly List<IntPtr> hooks = new();
    private readonly Dictionary<int, (Process Process, List<IntPtr> Hooks)> owners = new();
    private uint shellMessage;
    private bool shellRegistered;

    public TeamsWindowWatcher(Action onChanged, Func<int, bool>? isTeamsProcess = null)
    {
        this.onChanged = onChanged;
        this.isTeamsProcess = isTeamsProcess ?? this.IsTeamsProcess;
        NativeMethods.WinEventCallback callback = this.OnWinEvent;
        this.callbackRoot = GCHandle.Alloc(callback);
        try
        {
            this.hooks.Add(Register(NativeMethods.WinEvent.Foreground, callback));
            this.shellMessage = NativeMethods.RegisterWindowMessage("SHELLHOOK");
            if (this.shellMessage == 0)
            {
                throw new InvalidOperationException("Could not register the shell window-change message.");
            }
            this.CreateHandle(new CreateParams());
            this.shellRegistered = NativeMethods.RegisterShellHookWindow(this.Handle);
            if (!this.shellRegistered)
            {
                throw new InvalidOperationException("Could not register the Teams window-creation watcher.");
            }
            // Enumerate once at startup. Global discovery events find later processes.
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    if (isTeamsProcess?.Invoke(process.Id)
                        ?? process.ProcessName.Contains("teams", StringComparison.OrdinalIgnoreCase))
                    {
                        this.WatchProcess(process.Id, callback);
                    }
                }
            }
        }
        catch
        {
            this.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (this.shellRegistered)
        {
            if (!NativeMethods.DeregisterShellHookWindow(this.Handle))
            {
                throw new InvalidOperationException("Could not remove the Teams window-creation watcher.");
            }
            this.shellRegistered = false;
        }
        if (this.Handle != IntPtr.Zero)
        {
            this.DestroyHandle();
        }
        while (this.hooks.Count > 0)
        {
            int index = this.hooks.Count - 1;
            IntPtr hook = this.hooks[index];
            Unhook(ref hook);
            this.hooks.RemoveAt(index);
        }
        foreach (var owner in this.owners.Values)
        {
            owner.Process.Dispose();
        }
        this.owners.Clear();
        if (this.callbackRoot.IsAllocated)
        {
            this.callbackRoot.Free();
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (this.shellMessage != 0 && (uint)message.Msg == this.shellMessage
            && message.WParam.ToInt64() == NativeMethods.ShellWindowCreated)
        {
            DetectionEvents.Log.ShellWindowCreated(message.LParam.ToInt64());
            this.OnWindowDiscovered(message.LParam);
        }
        base.WndProc(ref message);
    }

    private void OnWindowDiscovered(IntPtr window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        if (processId != 0 && this.isTeamsProcess((int)processId))
        {
            this.WatchProcess((int)processId, (NativeMethods.WinEventCallback)this.callbackRoot.Target!);
            this.onChanged();
        }
    }

    private static IntPtr Register(
        NativeMethods.WinEvent eventType, NativeMethods.WinEventCallback callback, uint processId = 0)
    {
        // WINEVENT_OUTOFCONTEXT (0) delivers callbacks on this thread's message loop.
        // Filter before native-to-managed delivery. A range also delivers high-rate
        // location events even when the managed callback immediately discards them.
        IntPtr hook = NativeMethods.SetWinEventHook(eventType, eventType, IntPtr.Zero, callback, processId, 0, 0);
        return hook != IntPtr.Zero
            ? hook
            : throw new InvalidOperationException("Could not register the Teams window-change watcher.");
    }

    private void WatchProcess(int processId, NativeMethods.WinEventCallback callback)
    {
        if (this.owners.TryGetValue(processId, out var existing) && !existing.Process.HasExited)
        {
            return;
        }

        // Keep a process handle so a reused PID cannot hide a new Teams instance.
        foreach (var expired in this.owners.Where(pair => pair.Value.Process.HasExited).ToArray())
        {
            foreach (IntPtr registered in expired.Value.Hooks)
            {
                IntPtr hook = registered;
                Unhook(ref hook);
                this.hooks.Remove(registered);
            }
            expired.Value.Process.Dispose();
            this.owners.Remove(expired.Key);
        }

        Process? owner = null;
        try
        {
            owner = Process.GetProcessById(processId);
            if (owner.HasExited)
            {
                owner.Dispose();
                Trace.TraceWarning("Window owner {0} exited before event registration.", processId);
                return;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            owner?.Dispose();
            Trace.TraceWarning("Cannot watch window owner {0}: {1}", processId, ex.Message);
            return;
        }

        var registeredHooks = new List<IntPtr>();
        this.owners.Add(processId, (owner, registeredHooks));
        foreach (NativeMethods.WinEvent eventType in new[]
        {
            NativeMethods.WinEvent.ObjectShow,
            NativeMethods.WinEvent.ObjectHide,
            NativeMethods.WinEvent.ObjectReorder,
            NativeMethods.WinEvent.ObjectNameChange,
        })
        {
            IntPtr hook = Register(eventType, callback, (uint)processId);
            this.hooks.Add(hook);
            registeredHooks.Add(hook);
        }
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
        DetectionEvents.Log.WindowEventReceived((int)eventType, window.ToInt64(), objectId, childId);
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
            if (eventType is NativeMethods.WinEvent.Foreground or NativeMethods.WinEvent.ObjectShow)
            {
                this.WatchProcess((int)processId, (NativeMethods.WinEventCallback)this.callbackRoot.Target!);
            }
            this.onChanged();
        }
    }

    private bool IsTeamsProcess(int processId)
    {
        // Process.ProcessName can enumerate system-wide process information.
        // Query only this window owner and retain buffer capacity, not PID identity.
        using var process = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation, false, (uint)processId);
        if (process.IsInvalid)
        {
            Trace.TraceWarning("Cannot open window owner {0}: Win32 error {1}.",
                processId, Marshal.GetLastPInvokeError());
            return false;
        }

        while (true)
        {
            this.processImage.Clear();
            int capacity = this.processImage.Capacity;
            if (NativeMethods.QueryFullProcessImageName(process, 0, this.processImage, ref capacity))
            {
                string name = Path.GetFileNameWithoutExtension(this.processImage.ToString());
                return name.Contains("teams", StringComparison.OrdinalIgnoreCase);
            }

            int error = Marshal.GetLastPInvokeError();
            if (error == NativeMethods.ErrorInsufficientBuffer)
            {
                this.processImage.EnsureCapacity(checked(this.processImage.Capacity * 2));
                continue;
            }

            Trace.TraceWarning("Cannot identify window owner {0}: Win32 error {1}.", processId, error);
            return false;
        }
    }
}
