import { WebSocketServer } from "ws";
import { randomUUID } from "node:crypto";
import { createServer, IncomingMessage, ServerResponse } from "node:http";
import { createReadStream, existsSync, statSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join, normalize, extname } from "node:path";
import { config, clamp } from "./config.js";
import { log } from "./log.js";
import { Player } from "./Player.js";
import { RoomManager } from "./RoomManager.js";
import { safeParse, sanitizeNick, sanitizeAddress, randomGuestNick, isGameMode, sanitizeChatText } from "./protocol.js";
import type { ClientMsg, ServerMsg, Pose } from "./types.js";

const rm = new RoomManager();
const players = new Set<Player>();

// ---- static web hosting (serves the built web/ so one origin/URL covers web + ws) ----
const DIST = join(dirname(fileURLToPath(import.meta.url)), "../../web/dist");
const haveWeb = existsSync(join(DIST, "index.html"));

const MIME: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript",
  ".mjs": "text/javascript",
  ".css": "text/css",
  ".json": "application/json",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".webp": "image/webp",
  ".svg": "image/svg+xml",
  ".webm": "video/webm",
  ".glb": "model/gltf-binary",
  ".wasm": "application/wasm",
  ".ico": "image/x-icon",
  ".txt": "text/plain",
  ".map": "application/json",
};

// Unity WebGL Brotli assets: serve raw bytes with Content-Encoding: br + the right type.
function unitywebHeaders(name: string): { type: string; enc: string } | null {
  if (!name.endsWith(".unityweb")) return null;
  if (name.endsWith(".wasm.unityweb")) return { type: "application/wasm", enc: "br" };
  if (name.endsWith(".js.unityweb")) return { type: "text/javascript", enc: "br" };
  return { type: "application/octet-stream", enc: "br" }; // .data / .symbols.json
}

function serveStatic(req: IncomingMessage, res: ServerResponse): void {
  if (!haveWeb) {
    res.writeHead(200, { "content-type": "text/plain" });
    res.end("sol-champions-server running (web not built — run `npm run build` in web/)\n");
    return;
  }
  let urlPath = decodeURIComponent((req.url || "/").split("?")[0]);
  if (urlPath === "/") urlPath = "/index.html";
  // resolve + block traversal
  const filePath = normalize(join(DIST, urlPath));
  if (!filePath.startsWith(DIST)) {
    res.writeHead(403).end();
    return;
  }

  let target = filePath;
  if (!existsSync(target) || statSync(target).isDirectory()) {
    // SPA fallback for extension-less routes; otherwise 404
    if (extname(urlPath) === "") target = join(DIST, "index.html");
    else {
      res.writeHead(404, { "content-type": "text/plain" }).end("not found");
      return;
    }
  }

  const name = target.toLowerCase();
  const uw = unitywebHeaders(name);
  const headers: Record<string, string> = {};
  if (uw) {
    headers["content-type"] = uw.type;
    headers["content-encoding"] = uw.enc;
  } else {
    headers["content-type"] = MIME[extname(name)] || "application/octet-stream";
  }
  res.writeHead(200, headers);
  if (req.method === "HEAD") {
    res.end();
    return;
  }
  createReadStream(target).on("error", () => res.end()).pipe(res);
}

const httpServer = createServer(serveStatic);
const wss = new WebSocketServer({ server: httpServer });

httpServer.listen(config.PORT, "0.0.0.0", () => {
  log(
    `sol-champions-server listening on :${config.PORT}` +
      (haveWeb ? " (serving web/dist)" : " (ws only; web not built)") +
      (config.ALLOWED_ORIGIN ? ` origin=${config.ALLOWED_ORIGIN}` : ""),
  );
});

wss.on("connection", (ws, req) => {
  if (config.ALLOWED_ORIGIN) {
    const origin = req.headers.origin;
    if (origin && origin !== config.ALLOWED_ORIGIN) {
      ws.close(1008, "bad origin");
      return;
    }
  }

  const player = new Player(randomUUID(), ws);
  players.add(player);
  log("conn", player.id);

  ws.on("message", (data) => {
    const msg = safeParse(data.toString());
    if (!msg) {
      player.send({ t: "error", code: "badmsg", message: "unparseable message" });
      return;
    }
    try {
      handle(player, msg);
    } catch (err) {
      log("handler error", player.id, err);
    }
  });

  ws.on("pong", () => {
    player.isAlive = true;
  });

  ws.on("close", () => {
    log("close", player.id);
    rm.disconnect(player);
    players.delete(player);
  });

  ws.on("error", () => {
    /* 'close' will follow */
  });
});

function sanitizeLook(raw: unknown): string | null {
  // Opaque to the server; just cap length to avoid abuse.
  if (typeof raw !== "string") return null;
  return raw.length <= 4096 ? raw : null;
}

function toPose(q: unknown): Pose | null {
  if (!q || typeof q !== "object") return null;
  const o = q as Record<string, unknown>;
  const n = (v: unknown) => (typeof v === "number" && Number.isFinite(v) ? v : 0);
  return {
    x: n(o.x), y: n(o.y), z: n(o.z), r: n(o.r), s: n(o.s), a: n(o.a), d: n(o.d),
    j: n(o.j), fx: n(o.fx), fy: n(o.fy), fz: n(o.fz),
  };
}

function handle(player: Player, msg: ClientMsg): void {
  switch (msg.t) {
    case "identify": {
      player.nick = sanitizeNick(msg.nick) || randomGuestNick();
      player.solAddress = sanitizeAddress(msg.solAddress);
      player.look = sanitizeLook(msg.look);
      player.identified = true;
      player.send({ t: "identified", id: player.id, nick: player.nick });
      break;
    }
    case "updateLook": {
      // Outfit changed in the lobby after identify — refresh the stored look so the next
      // match roster ships the current outfit. (Roster is snapshotted at startMatch.)
      player.look = sanitizeLook(msg.look);
      break;
    }
    case "joinQueue": {
      if (!player.identified) {
        player.send({ t: "error", code: "notidentified", message: "identify before joining a queue" });
        break;
      }
      if (!isGameMode(msg.mode)) {
        player.send({ t: "error", code: "badmode", message: "unknown game mode" });
        break;
      }
      rm.joinQueue(player, msg.mode);
      break;
    }
    case "leaveQueue": {
      rm.leave(player);
      break;
    }
    case "reportResult": {
      if (!isGameMode(msg.mode)) break;
      rm.reportResult(player, msg.mode, {
        survivalMs: clamp(Number(msg.survivalMs) || 0, 0, config.MATCH_WATCHDOG_MS),
        finished: !!msg.finished,
        reason: "died",
      });
      break;
    }
    case "state": {
      const pose = toPose(msg.q);
      if (pose) rm.setPose(player, pose);
      break;
    }
    case "ready": {
      rm.markReady(player);
      break;
    }
    case "timeSync": {
      // Clock-sync probe: reply immediately with the server clock (works pre-identify).
      player.send({ t: "timeSyncPong", t0: Number(msg.t0) || 0, serverNow: Date.now() });
      break;
    }
    case "chat": {
      if (!player.identified) break;
      const text = sanitizeChatText(msg.text);
      if (!text) break;
      const now = Date.now();
      if (now - player.lastChatMs < 500) break; // simple rate-limit: max ~2 msgs/sec
      player.lastChatMs = now;
      const out: ServerMsg = { t: "chatMsg", id: player.id, nick: player.nick, text, ts: now };
      // Lobby-only: deliver to identified players NOT in a queue/match (roomMode === null).
      for (const p of players) if (p.identified && p.roomMode === null) p.send(out);
      break;
    }
    default: {
      player.send({ t: "error", code: "badtype", message: "unknown message type" });
    }
  }
}

// ---- heartbeat: drop sockets that stop responding to pings -------------------
const HEARTBEAT_MS = 30_000;
const heartbeat = setInterval(() => {
  for (const p of players) {
    if (!p.isAlive) {
      p.ws.terminate();
      continue;
    }
    p.isAlive = false;
    try {
      p.ws.ping();
    } catch {
      /* ignore */
    }
  }
}, HEARTBEAT_MS);

wss.on("close", () => clearInterval(heartbeat));

for (const sig of ["SIGINT", "SIGTERM"] as const) {
  process.on(sig, () => {
    log(`${sig} received, shutting down`);
    clearInterval(heartbeat);
    wss.close();
    httpServer.close();
    process.exit(0);
  });
}
