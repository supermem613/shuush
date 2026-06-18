# shuush

A MuteMe button that actually knows when you're muted in Microsoft Teams,
living quietly in your system tray.

When Microsoft shipped the "new" Teams, hardware mute buttons stopped following
your mute state. The old integrations leaned on either screen reading or the
Teams local "Third-party app API" on `ws://localhost:8124`, and **that local API
is being retired on 2026-06-30 with no replacement.** `shuush` takes a different,
durable path: it reads your real mute and in-call state directly from the Teams
window through Windows **UI Automation**, and drives the MuteMe LED over USB.
Tap the button and it toggles your Teams mute back.

- **Muted in a call** -> solid red, red tray icon
- **Live in a call** (unmuted) -> solid green, green tray icon
- **No call** -> LED off, grey tray icon
- **Tap the button** -> toggles Teams mute

No vendor app, no cloud, no admin rights, no fragile pairing token.

## How it works

Two halves, both local to your machine, bridged by a tray app:

1. **Detection (UI Automation).** New Teams is a WebView2 app. While you are in a
   call it exposes a button with AutomationId `microphone-button` whose name is
   `Mute mic` when live and `Unmute mic` when muted. When the button is absent
   you are not in a call. `shuush` re-reads this fresh on every poll, because
   WebView2 replaces its accessibility nodes on re-render and a cached node keeps
   returning a stale name. Toggling mute presses the same button via the Invoke
   pattern, which replaces the command the retired local API used to send.
2. **Output (USB HID).** The MuteMe is a plain USB HID device. Its LED is set
   with a 2-byte report `[0x00, cmd]`, where `cmd` is a color bitmask
   (`red 0x01`, `green 0x02`, `blue 0x04`, `dim 0x10`, `blink 0x20`). A physical
   tap arrives as an input report; `shuush` fires on release.

Windows records which apps are using the microphone under the
CapabilityAccessManager consent store in the registry. `shuush` watches that key
with `RegNotifyChangeKeyValue`, so while you are not in a call the poll loop
blocks on the change event instead of polling and idle CPU is effectively zero.
The moment Teams takes the microphone the watcher wakes the loop, which then
re-reads the UI Automation state on a short cadence until the call ends.

A background thread owns all UI Automation work and the poll loop, drives the
LED, and marshals tray-icon updates to the UI thread.

## Requirements

- Windows 10/11.
- .NET 9 SDK (or newer) to build; the produced app needs the .NET 9 Desktop
  Runtime.
- A MuteMe device (developed against the USB-C "Original", `20A0:42DA`).
- The **new** Microsoft Teams. No special Teams setting is required, and you do
  not need to enable the soon-to-be-retired third-party app API.

## Build and run

```pwsh
git clone https://github.com/supermem613/shuush.git
cd shuush
dotnet build -c Release
.\src\Shuush\bin\Release\net9.0-windows\shuush.exe
```

`shuush` runs in the system tray. Right-click the tray icon for the menu:

- **Toggle mute** - press the Teams mic button (enabled only while in a call).
- **Pause** - stop detection and show the configured paused color.
- **Start with Windows** - register/unregister an HKCU sign-in entry.
- **Settings...** - poll intervals, mode colors, dim, tray-color mirroring.
- **Exit**.

Quit the MuteMe vendor app (MuteMe-Client) first so the two don't fight over the
LED.

## Settings

Settings persist to `%AppData%\shuush\config.json`:

| Setting | Default | Meaning |
|---------|---------|---------|
| Poll interval (in call) | 750 ms | How often to re-read mute state during a call |
| Muted color / Live color | Red / Green | LED + tray colors from the MuteMe palette |
| Not-in-call color / Paused color | Off / Off | LED + tray colors from the MuteMe palette |
| Drive the MuteMe LED | on | Turn LED control off entirely |
| Dim the LED | off | Apply the dim bit |
| Tray icon color follows mute state | on | Off keeps a neutral tray icon |
| Start with Windows | off | Launch at sign-in |

Mode colors can be off or one of the seven bitmask colors: red, green, blue,
yellow, cyan, purple, white.

Changing a mode color in Settings previews only when that mode is currently
active. Muted, live, not-in-call, and paused each preview only while that
matching state is active.
OK saves the selected settings, Cancel restores the previous saved settings.

## Project layout

| File | Responsibility |
|------|----------------|
| `Program.cs` | Entry point, single-instance guard |
| `TrayContext.cs` | Tray icon, flyout menu, poll thread, state -> color |
| `TeamsMonitor.cs` | UI Automation: read and toggle the Teams mic button |
| `MuteState.cs` | Mute-state enum: no call, live, muted |
| `CallActivityProbe.cs` | Registry probe: is Teams using the microphone (in a call) |
| `MicActivityWatcher.cs` | Registry-change watcher that wakes the poll loop when a call starts |
| `MuteMeDevice.cs` | USB HID: LED output and button-tap input |
| `TrayIconRenderer.cs` | Colored-dot tray icon rendering |
| `LedPalette.cs` | MuteMe color palette -> HID command and tray color |
| `AppConfig.cs` | Settings load/save |
| `StartupManager.cs` | Start-with-Windows registration |
| `SettingsForm.cs` | Settings dialog |
| `NativeMethods.cs` | P/Invoke declarations for registry change notifications |

## License

MIT. See [LICENSE](LICENSE).
