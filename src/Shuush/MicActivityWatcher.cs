using Microsoft.Win32;

namespace Shuush;

/// <summary>
/// Event-driven detection of microphone capture. Windows updates LastUsedTimeStop
/// under the ConsentStore\microphone key whenever an app starts or stops capturing
/// the microphone, so a registry-change notification on that key tells us the instant
/// a Teams call begins or ends without waking on a timer.
///
/// A dedicated background thread arms <see cref="NativeMethods.RegNotifyChangeKeyValue"/>
/// asynchronously, blocks on the kernel event, raises <c>onChanged</c> when it fires,
/// then re-arms. While blocked the thread consumes no CPU, which lets the poll loop
/// idle on a long backstop instead of re-reading the registry every couple of seconds.
/// </summary>
internal sealed class MicActivityWatcher : IDisposable
{
    private readonly Action onChanged;
    private readonly RegistryKey key;
    private readonly ManualResetEvent changed = new(false);
    private readonly ManualResetEvent stop = new(false);
    private readonly Thread thread;
    private volatile bool disposed;

    private MicActivityWatcher(RegistryKey key, Action onChanged)
    {
        this.key = key;
        this.onChanged = onChanged;
        this.thread = new Thread(this.Loop)
        {
            IsBackground = true,
            Name = "shuush-mic-watch",
        };
        this.thread.Start();
    }

    /// <summary>
    /// Opens the microphone consent key and starts watching it. The default read access
    /// used by OpenSubKey includes KEY_NOTIFY, which is all the watch needs; the key is
    /// created if absent so the watcher always has a valid key to arm on.
    /// </summary>
    public static MicActivityWatcher Create(Action onChanged)
    {
        RegistryKey key = Registry.CurrentUser.OpenSubKey(CallActivityProbe.MicrophoneKeyPath)
            ?? Registry.CurrentUser.CreateSubKey(CallActivityProbe.MicrophoneKeyPath);
        return new MicActivityWatcher(key, onChanged);
    }

    private void Loop()
    {
        WaitHandle[] waits = { this.changed, this.stop };
        while (!this.disposed)
        {
            this.changed.Reset();

            // Watch the whole subtree: each app's LastUsedTimeStop lives in a subkey,
            // and the NonPackaged Teams build nests one level deeper still.
            int rc = NativeMethods.RegNotifyChangeKeyValue(
                this.key.Handle,
                watchSubtree: true,
                NativeMethods.RegNotifyFilter.LastSet
                    | NativeMethods.RegNotifyFilter.Name
                    | NativeMethods.RegNotifyFilter.ThreadAgnostic,
                this.changed.SafeWaitHandle,
                asynchronous: true);

            // Stop the thread instead of spinning on a handle the kernel rejected. The
            // poll loop's safety-scan backstop still detects calls without these wakes.
            if (rc != 0)
            {
                return;
            }

            if (WaitHandle.WaitAny(waits) == 1 || this.disposed)
            {
                return;
            }

            this.onChanged();
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.stop.Set();
        this.thread.Join(2000);
        this.key.Dispose();
        this.changed.Dispose();
        this.stop.Dispose();
    }
}
