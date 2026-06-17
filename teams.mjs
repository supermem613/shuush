// Microsoft Teams local "Third-party app API" client.
//
// The new Teams desktop client exposes a WebSocket on ws://localhost:8124 when
// the user enables Settings > Privacy > Third-party app API. Identity and the
// pairing token travel as URL query parameters, not as a handshake message.
//
// First connect with no token while in a meeting raises an Allow/Block prompt
// in Teams. After Allow, Teams sends { tokenRefresh } once. That token is
// persisted and replayed on later connects so the prompt never returns.
//
// Teams pushes { meetingUpdate: { meetingState } } on connect and on every
// state change. Commands flow the other way as { action, parameters,
// requestId }.

import { readFileSync, writeFileSync } from "node:fs";

const ENDPOINT = "ws://localhost:8124";

const IDENTITY = {
  "protocol-version": "2.0.0",
  manufacturer: "marcusm",
  device: "MuteMe-DIY",
  app: "TeamsMuteWatch",
  "app-version": "1.0.0",
};

export class TeamsClient {
  constructor({ tokenFile, onState, onStatus, onRaw } = {}) {
    this.tokenFile = tokenFile;
    this.onState = onState;
    this.onStatus = onStatus ?? (() => {});
    this.onRaw = onRaw;
    this.token = this.#loadToken();
    this.ws = null;
    this.requestId = 0;
    this.stopped = false;
    this.reconnectMs = 2000;
  }

  start() {
    this.stopped = false;
    this.#connect();
  }

  stop() {
    this.stopped = true;
    if (this.ws) this.ws.close();
    this.ws = null;
  }

  toggleMute() {
    this.#send({ action: "toggle-mute", parameters: {}, requestId: ++this.requestId });
  }

  #connect() {
    const params = new URLSearchParams(IDENTITY);
    if (this.token) params.set("token", this.token);
    const url = `${ENDPOINT}?${params.toString()}`;

    let ws;
    try {
      ws = new WebSocket(url);
    } catch (err) {
      this.#scheduleReconnect(`connect failed: ${err.message}`);
      return;
    }
    this.ws = ws;

    ws.addEventListener("open", () => {
      this.reconnectMs = 2000;
      this.onStatus(this.token ? "connected (paired)" : "connected, waiting for pairing approval in Teams");
    });

    ws.addEventListener("message", (ev) => this.#handleMessage(ev.data));

    ws.addEventListener("close", () => {
      this.ws = null;
      if (!this.stopped) this.#scheduleReconnect("connection closed");
    });

    // The error event always precedes close. Let close own the reconnect so a
    // single failure does not schedule two reconnects.
    ws.addEventListener("error", () => {});
  }

  #handleMessage(data) {
    let msg;
    try {
      msg = JSON.parse(typeof data === "string" ? data : data.toString());
    } catch {
      return;
    }
    if (this.onRaw) this.onRaw(msg);

    if (msg.tokenRefresh) {
      this.token = msg.tokenRefresh;
      this.#saveToken(this.token);
      this.onStatus("paired with Teams, token saved");
    }
    if (msg.meetingUpdate?.meetingState && this.onState) {
      this.onState(msg.meetingUpdate.meetingState);
    }
    if (msg.errorMsg) {
      this.onStatus(`Teams error: ${msg.errorMsg}`);
    }
  }

  #send(obj) {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(obj));
    }
  }

  #scheduleReconnect(reason) {
    this.onStatus(`${reason}; retrying in ${this.reconnectMs / 1000}s`);
    setTimeout(() => {
      if (!this.stopped) this.#connect();
    }, this.reconnectMs);
    this.reconnectMs = Math.min(this.reconnectMs * 2, 15000);
  }

  #loadToken() {
    if (!this.tokenFile) return "";
    try {
      return JSON.parse(readFileSync(this.tokenFile, "utf8")).token ?? "";
    } catch {
      return "";
    }
  }

  #saveToken(token) {
    if (!this.tokenFile) return;
    writeFileSync(this.tokenFile, JSON.stringify({ token }, null, 2));
  }
}
