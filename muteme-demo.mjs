// Quick proof that node-hid controls the MuteMe LED.
// Protocol: 2-byte output report [0x00, cmd]; cmd bitmask red=1 green=2 blue=4 dim=0x10 blink=0x20.
import HID from "node-hid";

const VID = 0x20a0;
const PID = 0x42da;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const device = new HID.HID(VID, PID);
const info = HID.devices().find((d) => d.vendorId === VID && d.productId === PID);
console.log(`Opened: ${info?.manufacturer ?? "?"} ${info?.product ?? "?"}`);

const seq = [
  ["red", 0x01],
  ["green", 0x02],
  ["blue", 0x04],
  ["white", 0x07],
  ["blinking red", 0x21],
  ["off", 0x00],
];

for (const [name, cmd] of seq) {
  device.write([0x00, cmd]);
  console.log(`  ${name.padEnd(14)} cmd=0x${cmd.toString(16).padStart(2, "0")}`);
  await sleep(700);
}

device.write([0x00, 0x00]);
device.close();
console.log("Node control confirmed.");
