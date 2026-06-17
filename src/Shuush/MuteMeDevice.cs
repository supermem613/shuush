using HidSharp;

namespace Shuush;

/// <summary>
/// Controls the MuteMe USB button: drives the LED and reports physical taps.
///
/// Wire protocol for the MuteMe Original / USB-C (VID 0x20A0, PID 0x42DA): a
/// 2-byte output report [0x00, cmd]. The command byte is a bitmask, not an 8-bit
/// RGB value: red=0x01 green=0x02 blue=0x04 dim=0x10 blink=0x20; 0x00 is off.
/// Input reports arrive whenever the touch state changes; the last byte is
/// non-zero while a finger is on the pad. A tap is reported on release so a held
/// button does not repeat-toggle.
/// </summary>
internal sealed class MuteMeDevice : IDisposable
{
    public const byte Off = 0x00;
    public const byte Red = 0x01;
    public const byte Green = 0x02;
    public const byte Blue = 0x04;
    public const byte Yellow = 0x03;
    public const byte Purple = 0x05;
    public const byte Cyan = 0x06;
    public const byte White = 0x07;
    public const byte DimBit = 0x10;
    public const byte BlinkBit = 0x20;

    private const int Vid = 0x20A0;
    private const int Pid = 0x42DA;

    // Byte 4 of the input report is the touch code: 0x01 is a touch-present sample
    // repeated while the pad is held, and 0x02 is the single click marker the
    // firmware emits once when the pad is released. Toggling on the click marker
    // gives exactly one toggle per press regardless of how the 0x01 samples jitter.
    private const byte ClickCode = 0x02;

    private readonly object gate = new();
    private HidDevice? device;
    private HidStream? stream;
    private Thread? readThread;
    private volatile bool running;
    private byte lastWritten = 0xFF; // sentinel forces the first write through
    private byte desiredCommand = Off; // intended LED state, re-sent as a keepalive

    /// <summary>Raised on the read thread when the pad is tapped and released.</summary>
    public event Action? Tapped;

    /// <summary>Whether the device is currently open.</summary>
    public bool IsConnected
    {
        get
        {
            lock (this.gate)
            {
                return this.stream is not null;
            }
        }
    }

    /// <summary>
    /// Opens the device if it is not already open. Safe to call repeatedly: it is
    /// the reconnect path when the button is plugged in after start, or replugged.
    /// </summary>
    public bool EnsureOpen()
    {
        lock (this.gate)
        {
            if (this.stream is not null)
            {
                return true;
            }

            HidDevice? dev = DeviceList.Local.GetHidDevices(Vid, Pid).FirstOrDefault();
            if (dev is null)
            {
                return false;
            }

            if (!dev.TryOpen(out HidStream opened))
            {
                return false;
            }

            opened.ReadTimeout = 500;
            this.device = dev;
            this.stream = opened;
            this.lastWritten = 0xFF;
            this.running = true;
            this.readThread = new Thread(this.ReadLoop)
            {
                IsBackground = true,
                Name = "muteme-read",
            };
            this.readThread.Start();

            // The device does not stream touch input until it has received an output
            // report, so send the current LED state immediately to start reporting.
            this.WriteLocked(this.desiredCommand);
            return true;
        }
    }

    /// <summary>
    /// Sets the LED to a command byte. Idempotent: a repeated identical command is
    /// skipped so a steady state does not spam the USB endpoint.
    /// </summary>
    public void Set(byte command)
    {
        lock (this.gate)
        {
            this.desiredCommand = command;
            if (this.stream is null || command == this.lastWritten)
            {
                return;
            }

            this.WriteLocked(command);
        }
    }

    /// <summary>
    /// Re-sends the current LED command. The device only streams touch input while
    /// it keeps receiving output reports, so the poll loop calls this periodically
    /// to keep tap detection alive even when the LED color is not changing.
    /// </summary>
    public void Refresh()
    {
        lock (this.gate)
        {
            if (this.stream is null)
            {
                return;
            }

            this.WriteLocked(this.desiredCommand);
        }
    }

    private void WriteLocked(byte command)
    {
        try
        {
            this.WriteReport(command);
            this.lastWritten = command;
        }
        catch (IOException)
        {
            this.DropLocked();
        }
        catch (ObjectDisposedException)
        {
            this.DropLocked();
        }
    }

    /// <summary>Turns the LED off and closes the device.</summary>
    public void Close()
    {
        lock (this.gate)
        {
            if (this.stream is not null)
            {
                try
                {
                    this.WriteReport(Off);
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }

            this.DropLocked();
        }
    }

    public void Dispose() => this.Close();

    private void WriteReport(byte command)
    {
        HidStream target = this.stream!;
        int length = this.device!.GetMaxOutputReportLength();
        if (length < 2)
        {
            length = 2;
        }

        byte[] report = new byte[length];
        report[0] = 0x00; // report id
        report[1] = command;
        target.Write(report);
    }

    private void ReadLoop()
    {
        HidStream target;
        int length;
        lock (this.gate)
        {
            if (this.stream is null)
            {
                return;
            }

            target = this.stream;
            length = Math.Max(1, this.device!.GetMaxInputReportLength());
        }

        byte[] buffer = new byte[length];
        bool deviceLost = false;
        while (this.running)
        {
            int read;
            try
            {
                read = target.Read(buffer);
            }
            catch (TimeoutException)
            {
                continue; // lets the loop re-check the running flag for shutdown
            }
            catch (IOException)
            {
                deviceLost = true;
                break;
            }
            catch (ObjectDisposedException)
            {
                break; // deliberate shutdown disposed the stream
            }

            if (read <= 0)
            {
                continue;
            }

            // Byte 4 carries the touch code, but bytes 0..3 are always zero, so scan
            // for the first non-zero byte. 0x01 repeats while the pad is held; 0x02
            // is the single click marker on release. Fire one tap per click marker.
            byte code = 0;
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] != 0)
                {
                    code = buffer[i];
                    break;
                }
            }

            if (code == ClickCode)
            {
                this.Tapped?.Invoke();
            }
        }

        // An unexpected read failure means the button was unplugged. Drop the
        // device state so IsConnected reflects reality and EnsureOpen reconnects.
        if (deviceLost)
        {
            lock (this.gate)
            {
                if (this.stream == target)
                {
                    this.DropLocked();
                }
            }
        }
    }

    private void DropLocked()
    {
        this.running = false;
        try
        {
            this.stream?.Dispose();
        }
        catch (IOException)
        {
        }

        this.stream = null;
        this.device = null;
        this.lastWritten = 0xFF;
    }
}
