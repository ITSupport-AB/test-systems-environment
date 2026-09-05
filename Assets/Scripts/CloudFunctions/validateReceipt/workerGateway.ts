import * as admin from "firebase-admin";
import * as functions from "firebase-functions";
import {Request, Response} from "express";
import * as crypto from "crypto";
import * as https from "https";

const MAX_WORKER_TEMPERATURE_C = 85;

function firestore(): admin.firestore.Firestore {
  return admin.firestore();
}

type WorkerCommand = "START" | "STOP" | "RESTART";
type WorkerState = "RUNNING" | "STOPPED" | "OFFLINE" | "UNKNOWN";

function setCors(response: Response): void {
  response.set("Access-Control-Allow-Origin", "*");
  response.set("Access-Control-Allow-Headers", "Authorization, Content-Type");
  response.set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
}

async function authenticate(request: Request): Promise<admin.auth.DecodedIdToken> {
  const header = request.headers.authorization || "";
  if (!header.startsWith("Bearer ")) {
    throw new functions.https.HttpsError("unauthenticated", "A Firebase ID token is required.");
  }

  return admin.auth().verifyIdToken(header.substring("Bearer ".length));
}

async function authenticateAgent(request: Request, workerId: string): Promise<{uid: string}> {
  const header = request.headers["x-worker-key"];
  const key = Array.isArray(header) ? header[0] : header;
  if (!key) {
    throw new functions.https.HttpsError("unauthenticated", "A worker key is required.");
  }

  const agent = await firestore().collection("workerAgents").doc(workerId).get();
  const data = agent.data() || {};
  const expected = Buffer.from(String(data.keyHash || ""), "hex");
  const supplied = crypto.createHash("sha256").update(key).digest();
  if (!agent.exists || expected.length !== supplied.length || !crypto.timingSafeEqual(expected, supplied)) {
    throw new functions.https.HttpsError("unauthenticated", "Worker authentication failed.");
  }

  return {uid: String(data.uid)};
}

function workerRef(uid: string, workerId: string): admin.firestore.DocumentReference {
  return firestore().collection("users").doc(uid).collection("workers").doc(workerId);
}

function workerResponse(snapshot: admin.firestore.DocumentSnapshot): Record<string, unknown> {
  const data = snapshot.data() || {};
  return {
    id: snapshot.id,
    name: data.name || snapshot.id,
    coin: data.coin || "unknown",
    network: data.network || "unknown",
    algorithm: data.algorithm || "unknown",
    state: data.state || "UNKNOWN",
    hashrate: data.hashrate || "--",
    temperatureCelsius: data.temperatureCelsius || 0,
    uptime: data.uptime || "--",
    lastSeen: data.lastSeen || "--",
  };
}

async function listWorkers(uid: string): Promise<Record<string, unknown>[]> {
  const snapshot = await firestore().collection("users").doc(uid).collection("workers").get();
  return snapshot.docs.map(workerResponse);
}

async function getLunoFundingAddress(asset: string): Promise<Record<string, unknown>> {
  const config = functions.config().luno || {};
  const keyId = String(config.key_id || "");
  const keySecret = String(config.key_secret || "");
  if (!keyId || !keySecret) {
    throw new functions.https.HttpsError("failed-precondition", "Luno integration is not configured.");
  }

  return new Promise((resolve, reject) => {
    const request = https.get(
      `https://api.luno.com/api/1/funding_address?asset=${encodeURIComponent(asset)}`,
      {
        auth: `${keyId}:${keySecret}`,
        headers: {Accept: "application/json"},
        timeout: 10_000,
      },
      (response) => {
        let body = "";
        response.setEncoding("utf8");
        response.on("data", (chunk) => { body += chunk; });
        response.on("end", () => {
          if (response.statusCode !== 200) {
            reject(new functions.https.HttpsError("unavailable", "Luno address lookup failed."));
            return;
          }
          try {
            const parsed = JSON.parse(body);
            resolve({asset: parsed.asset, address: parsed.address, network: parsed.network || null});
          } catch {
            reject(new functions.https.HttpsError("internal", "Luno returned an invalid response."));
          }
        });
      }
    );
    request.on("timeout", () => request.destroy(new Error("Luno request timed out")));
    request.on("error", () => reject(new functions.https.HttpsError("unavailable", "Luno is unavailable.")));
  });
}

async function recordHeartbeat(uid: string, workerId: string, body: Record<string, unknown>): Promise<void> {
  const state = String(body.state || "").toUpperCase() as WorkerState;
  const coin = String(body.coin || "");
  const network = String(body.network || "");
  const algorithm = String(body.algorithm || "");
  const temperature = Number(body.temperatureCelsius);
  const hashrate = String(body.hashrate || "--");
  const uptime = String(body.uptime || "--");
  if (!["RUNNING", "STOPPED", "OFFLINE", "UNKNOWN"].includes(state)) {
    throw new functions.https.HttpsError("invalid-argument", "Unsupported worker state.");
  }
  if (!Number.isFinite(temperature) || temperature < 0 || temperature > 150) {
    throw new functions.https.HttpsError("invalid-argument", "Invalid worker temperature.");
  }
  if (hashrate.length > 64 || uptime.length > 64) {
    throw new functions.https.HttpsError("invalid-argument", "Worker telemetry field is too long.");
  }
  if (!coin || !network || !algorithm || [coin, network, algorithm].some((value) => value.length > 64)) {
    throw new functions.https.HttpsError("invalid-argument", "Coin, network, and algorithm are required.");
  }

  await workerRef(uid, workerId).set({
    coin,
    network,
    algorithm,
    state,
    hashrate,
    temperatureCelsius: temperature,
    uptime,
    lastSeen: new Date().toISOString(),
    updatedAt: admin.firestore.FieldValue.serverTimestamp(),
  }, {merge: true});
}

async function claimCommands(uid: string, workerId: string): Promise<Record<string, unknown>[]> {
  const db = firestore();
  const snapshot = await db.collection("users").doc(uid).collection("workerCommands")
    .where("workerId", "==", workerId)
    .where("status", "==", "QUEUED")
    .limit(10)
    .get();

  const commands: Record<string, unknown>[] = [];
  const batch = db.batch();
  snapshot.docs.forEach((command) => {
    commands.push({id: command.id, ...command.data(), status: "DISPATCHED"});
    batch.update(command.ref, {
      status: "DISPATCHED",
      dispatchedAt: admin.firestore.FieldValue.serverTimestamp(),
    });
  });
  if (commands.length > 0) await batch.commit();
  return commands;
}

async function queueCommand(
  uid: string,
  workerId: string,
  command: WorkerCommand,
  idempotencyKey: string
): Promise<void> {
  if (!/^[a-zA-Z0-9._:-]{16,128}$/.test(idempotencyKey)) {
    throw new functions.https.HttpsError("invalid-argument", "A valid idempotency key is required.");
  }

  const db = firestore();
  const commandRef = db.collection("users").doc(uid).collection("commandKeys").doc(idempotencyKey);
  const worker = await workerRef(uid, workerId).get();
  if (!worker.exists) {
    throw new functions.https.HttpsError("not-found", "Worker does not exist.");
  }

  const data = worker.data() || {};
  const temperature = Number(data.temperatureCelsius || 0);
  const state = String(data.state || "UNKNOWN").toUpperCase();
  if (temperature >= MAX_WORKER_TEMPERATURE_C) {
    throw new functions.https.HttpsError("failed-precondition", "Worker temperature is above the safety limit.");
  }
  if (command === "START" && state === "OFFLINE") {
    throw new functions.https.HttpsError("failed-precondition", "Offline workers cannot be started.");
  }

  await db.runTransaction(async (transaction) => {
    const existing = await transaction.get(commandRef);
    if (existing.exists) {
      throw new functions.https.HttpsError("already-exists", "This command has already been accepted.");
    }

    const queuedCommand = db.collection("users").doc(uid).collection("workerCommands").doc();
    transaction.create(commandRef, {
      commandId: queuedCommand.id,
      workerId,
      command,
      createdAt: admin.firestore.FieldValue.serverTimestamp(),
    });
    transaction.create(queuedCommand, {
      workerId,
      command,
      status: "QUEUED",
      createdAt: admin.firestore.FieldValue.serverTimestamp(),
      idempotencyKey,
    });
  });
}

export const workerGateway = functions.https.onRequest(async (request, response) => {
  setCors(response);
  if (request.method === "OPTIONS") {
    response.status(204).send("");
    return;
  }

  try {
    const path = request.path.replace(/^\/v1/, "").replace(/\/$/, "");

    const heartbeatMatch = path.match(/^\/agent\/workers\/([^/]+)\/heartbeat$/);
    if (request.method === "POST" && heartbeatMatch) {
      const agent = await authenticateAgent(request, heartbeatMatch[1]);
      await recordHeartbeat(agent.uid, heartbeatMatch[1], request.body || {});
      response.status(204).send("");
      return;
    }

    const agentCommandsMatch = path.match(/^\/agent\/workers\/([^/]+)\/commands$/);
    if (request.method === "GET" && agentCommandsMatch) {
      const agent = await authenticateAgent(request, agentCommandsMatch[1]);
      response.status(200).json({commands: await claimCommands(agent.uid, agentCommandsMatch[1])});
      return;
    }

    const identity = await authenticate(request);

    if (request.method === "GET" && path === "/workers") {
      response.status(200).json(await listWorkers(identity.uid));
      return;
    }

    if (request.method === "GET" && path === "/payout/luno/address") {
      const asset = String(request.query.asset || "").toUpperCase();
      if (!/^[A-Z0-9]{2,12}$/.test(asset)) {
        throw new functions.https.HttpsError("invalid-argument", "A valid Luno asset is required.");
      }
      response.status(200).json(await getLunoFundingAddress(asset));
      return;
    }

    const commandMatch = path.match(/^\/workers\/([^/]+)\/commands$/);
    if (request.method === "POST" && commandMatch) {
      const command = String(request.body?.command || "").toUpperCase() as WorkerCommand;
      const idempotencyKey = String(request.body?.idempotencyKey || "");
      if (!["START", "STOP", "RESTART"].includes(command)) {
        throw new functions.https.HttpsError("invalid-argument", "Unsupported worker command.");
      }

      await queueCommand(identity.uid, commandMatch[1], command, idempotencyKey);
      response.status(202).json({status: "QUEUED"});
      return;
    }

    response.status(404).json({error: "Not found"});
  } catch (error: any) {
    const status = error instanceof functions.https.HttpsError ? errorToStatus(error.code) : 500;
    functions.logger.error("Worker gateway request failed", {error: error.message, status});
    response.status(status).json({error: error.message || "Internal server error"});
  }
});

function errorToStatus(code: string): number {
  switch (code) {
    case "unauthenticated": return 401;
    case "permission-denied": return 403;
    case "not-found": return 404;
    case "already-exists": return 409;
    case "invalid-argument": return 400;
    case "failed-precondition": return 412;
    default: return 500;
  }
}