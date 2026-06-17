# shuush

A MuteMe button that actually knows when you're muted in Microsoft Teams.

When Microsoft shipped the "new" Teams, it removed the screen-reading hooks that
hardware mute buttons relied on, so the MuteMe light stopped following your mute
state. `shuush` is a tiny, dependency-light replacement: it reads your real mute
and in-call state from the supported Teams local API and drives the MuteMe LED
directly over USB. Tap the button and it toggles your Teams mute back.

- **Muted in a call** -> solid red
- **Live in a call** (unmuted) -> solid green
- **No call** -> off
- **Tap the button** -> toggles Teams mute

No vendor app, no cloud, no admin rights. Just Node and the device.

## How it works

Two halves, both local to your machine:

1. **Input.** The new Teams desktop client exposes a WebSocket at
   `ws://localhost:8124` (the "Third-party app API"). After a one-time pairing
   approval it pushes `{ isMuted, isInMeeting, ... }` on every change.
2. **Output.** The MuteMe is a plain USB HID device. Its LED is set with a
   2-byte report `[0x00, cmd]`, where `cmd` is a color bitmask
   (`red 0x01`, `green 0x02`, `blue 0x04`, `dim 0x10`, `blink 0x20`).

`shuush` bridges the two and persists the Teams pairing token so you only
approve once.

## Requirements

- A MuteMe device (developed against the USB-C "Original", `20A0:42DA`).
- Node.js 22 or newer. Node ships a built-in `WebSocket`, so the Teams side has
  zero dependencies. The only npm dependency is `node-hid` for USB access.
- The **new** Microsoft Teams on a work/school (paid) account. The local API is
  not available on the free version.

## Setup

```sh
git clone https://github.com/supermem613/shuush.git
cd shuush
npm install
```

### Enable the Teams local API (one time)

This toggle is a Teams privacy setting and cannot be flipped programmatically.

1. Teams -> **... (top right)** -> **Settings** -> **Privacy**.
2. Scroll to **Third-party app API** -> **Manage API**.
3. Turn it **on**. If it is greyed out, update Teams or ask your admin to allow
   third-party device pairing.

### Quit the MuteMe vendor app

`shuush` and the MuteMe-Client app both want to own the LED. Quit the vendor app
so they don't fight over it.

## Usage

```sh
npm start          # or: node watch.mjs
node watch.mjs --debug   # also prints raw Teams messages and button reports
```

First run, **join a meeting** and approve the **Allow** prompt that Teams shows.
The pairing token is saved to `token.json` (gitignored) and reused after that.

`npm run demo` cycles the LED through its colors as a quick hardware check.

## Customizing the colors

The LED mapping lives at the top of `watch.mjs`:

```js
const COLOR_MUTED = Cmd.RED;
const COLOR_LIVE = Cmd.GREEN;
const COLOR_IDLE = Cmd.OFF;
```

Swap in any value from `Cmd` (`WHITE`, `PURPLE`, `RED | Cmd.BLINK`, etc.).

## Button calibration

MuteMe firmware revisions report touch with different byte codes. `shuush`
treats a non-zero touch byte as a press and fires on release. If your button
doesn't toggle mute, run with `--debug`, tap it, and note the `button raw:`
lines, then adjust the predicate in `muteme.mjs`.

## Project layout

| File | Responsibility |
|------|----------------|
| `muteme.mjs` | USB-HID device: LED output and button input |
| `teams.mjs` | Teams local API client: pairing, token, state, commands |
| `watch.mjs` | Glue: maps Teams state to the LED and taps to mute |
| `muteme-demo.mjs` | Standalone LED color test |

## License

MIT. See [LICENSE](LICENSE).
