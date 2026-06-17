// teams-mute-watch: a DIY replacement for the MuteMe app's "new Teams" sync.
//
// Reads mute and in-meeting state from the Teams local API and drives the
// MuteMe LED. A physical tap toggles the Teams mute. This is a standalone
// utility. Run it instead of the MuteMe-Client app, not alongside it.
//
// Usage:
//   node watch.mjs          start the watcher
//   node watch.mjs --debug  also print raw Teams messages and button reports
//
// Prerequisite: in Teams, enable Settings > Privacy > Third-party app API.
// First run, join a meeting and approve the Allow prompt once to pair.

import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

import { MuteMe, Cmd } from "./muteme.mjs";
import { TeamsClient } from "./teams.mjs";

const HERE = dirname(fileURLToPath(import.meta.url));
const TOKEN_FILE = join(HERE, "token.json");
const DEBUG = process.argv.includes("--debug");

// LED meaning. Edit these three to restyle without touching any logic.
const COLOR_MUTED = Cmd.RED;
const COLOR_LIVE = Cmd.GREEN;
const COLOR_IDLE = Cmd.OFF;

function commandFor(state) {
  if (!state.isInMeeting) return COLOR_IDLE;
  return state.isMuted ? COLOR_MUTED : COLOR_LIVE;
}

function stamp(line) {
  const t = new Date().toLocaleTimeString();
  console.log(`[${t}] ${line}`);
}

function colorName(cmd) {
  return { [Cmd.RED]: "red", [Cmd.GREEN]: "green", [Cmd.OFF]: "off" }[cmd] ?? `0x${cmd.toString(16)}`;
}

const muteme = new MuteMe({
  onTap: () => {
    stamp("button tapped -> toggling Teams mute");
    teams.toggleMute();
  },
  onRaw: DEBUG ? (buf) => stamp(`button raw: ${[...buf].map((b) => b.toString(16).padStart(2, "0")).join(" ")}`) : undefined,
});

const teams = new TeamsClient({
  tokenFile: TOKEN_FILE,
  onStatus: (s) => stamp(s),
  onRaw: DEBUG ? (msg) => stamp(`teams raw: ${JSON.stringify(msg)}`) : undefined,
  onState: (state) => {
    const cmd = commandFor(state);
    muteme.set(cmd);
    const where = state.isInMeeting ? "in call" : "no call";
    const mic = state.isMuted ? "muted" : "live";
    stamp(`${where}, ${mic} -> LED ${colorName(cmd)}`);
  },
});

function openDevice() {
  try {
    muteme.open();
    stamp("MuteMe connected");
  } catch (err) {
    stamp(`MuteMe not reachable: ${err.message}. Retrying in 5s`);
    setTimeout(openDevice, 5000);
  }
}

let shuttingDown = false;
function shutdown() {
  if (shuttingDown) return;
  shuttingDown = true;
  stamp("shutting down: LED off");
  teams.stop();
  muteme.close();
  process.exit(0);
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);

stamp("teams-mute-watch starting");
stamp(`LED map: muted=${colorName(COLOR_MUTED)} live=${colorName(COLOR_LIVE)} idle=${colorName(COLOR_IDLE)}`);
openDevice();
teams.start();
