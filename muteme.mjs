// MuteMe USB-HID controller: LED output and physical-button input.
//
// Wire protocol for the MuteMe Original / USB-C (20A0:42DA): a 2-byte output
// report [0x00, cmd]. The command byte is a bitmask, not an 8-bit RGB value.
//   red 0x01  green 0x02  blue 0x04  dim 0x10  blink 0x20
// Combine color bits for mixes. 0x07 is white, 0x00 is off.
//
// Input reports arrive as node-hid "data" events whenever the touch state
// changes. The raw code differs across firmware revisions, so the press edge
// is derived from a configurable predicate and unknown codes are surfaced for
// calibration rather than silently dropped.

import HID from "node-hid";

export const VID = 0x20a0;
export const PID = 0x42da;

export const Cmd = {
  OFF: 0x00,
  RED: 0x01,
  GREEN: 0x02,
  BLUE: 0x04,
  YELLOW: 0x03,
  PURPLE: 0x05,
  CYAN: 0x06,
  WHITE: 0x07,
  DIM: 0x10,
  BLINK: 0x20,
};

export class MuteMe {
  constructor({ vid = VID, pid = PID, onTap, onRaw } = {}) {
    this.vid = vid;
    this.pid = pid;
    this.onTap = onTap;
    this.onRaw = onRaw;
    this.device = null;
    this.lastCmd = null;
    this.touchDown = false;
  }

  open() {
    this.device = new HID.HID(this.vid, this.pid);
    this.device.on("data", (buf) => this.#handleInput(buf));
    // A device error usually means it was unplugged. Surface it and drop the
    // handle so the supervisor can retry a clean open.
    this.device.on("error", () => {
      this.touchDown = false;
      this.device = null;
    });
    return this;
  }

  get isOpen() {
    return this.device !== null;
  }

  // Idempotent write. Repeated identical commands are skipped so a steady
  // state does not spam the USB endpoint.
  set(cmd) {
    if (!this.device || cmd === this.lastCmd) return;
    this.device.write([0x00, cmd]);
    this.lastCmd = cmd;
  }

  close() {
    if (!this.device) return;
    try {
      this.device.write([0x00, Cmd.OFF]);
      this.device.close();
    } catch {
      // Already gone. Nothing to clean up.
    }
    this.device = null;
    this.lastCmd = null;
  }

  #handleInput(buf) {
    if (this.onRaw) this.onRaw(buf);
    // The touch byte is the last byte of the report across known firmwares.
    // Non-zero means a finger is on the pad. A tap fires once on release so a
    // held button does not repeat-toggle.
    const touch = buf[buf.length - 1] !== 0x00;
    if (touch && !this.touchDown) {
      this.touchDown = true;
    } else if (!touch && this.touchDown) {
      this.touchDown = false;
      if (this.onTap) this.onTap();
    }
  }
}
