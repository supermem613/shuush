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

A background thread owns all UI Automation work and the poll loop, drives the
LED, and marshals tray-icon updates to the UI thread. Polling slows down while
you are not in a call to keep idle CPU low.

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
- **Pause** - stop driving the LED and detection (LED off, grey paused icon).
- **Start with Windows** - register/unregister an HKCU sign-in entry.
- **Settings...** - poll intervals, LED colors, dim, tray-color mirroring.
- **Exit**.

Quit the MuteMe vendor app (MuteMe-Client) first so the two don't fight over the
LED.

## Settings

Settings persist to `%AppData%\shuush\config.json`:

| Setting | Default | Meaning |
|---------|---------|---------|
| Poll interval (in call) | 750 ms | How often to re-read mute state during a call |
| Idle interval (no call) | 2000 ms | Slower poll while not in a call |
| Muted color / Live color | Red / Green | LED + tray colors from the MuteMe palette |
| Drive the MuteMe LED | on | Turn LED control off entirely |
| Dim the LED | off | Apply the dim bit |
| Tray icon color follows mute state | on | Off keeps a neutral tray icon |
| Start with Windows | off | Launch at sign-in |

The LED can only show the seven bitmask colors: red, green, blue, yellow, cyan,
purple, white.

## Project layout

| File | Responsibility |
|------|----------------|
| `Program.cs` | Entry point, single-instance guard |
| `TrayContext.cs` | Tray icon, flyout menu, poll thread, state -> color |
| `TeamsMonitor.cs` | UI Automation: read and toggle the Teams mic button |
| `MuteMeDevice.cs` | USB HID: LED output and button-tap input |
| `TrayIconRenderer.cs` | Colored-dot tray icon rendering |
| `LedPalette.cs` | MuteMe color palette -> HID command and tray color |
| `AppConfig.cs` | Settings load/save |
| `StartupManager.cs` | Start-with-Windows registration |
| `SettingsForm.cs` | Settings dialog |

## License

MIT. See [LICENSE](LICENSE).
