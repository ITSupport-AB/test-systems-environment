import * as admin from "firebase-admin";
import * as functions from "firebase-functions";
import {Request, Response} from "express";

const MAX_WORKER_TEMPERATURE_C = 85;

function firestore(): admin.firestore.Firestore {
  return admin.firestore();
}

type WorkerCommand = "START" | "STOP" | "RESTART";

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

function workerRef(uid: string, workerId: string): admin.firestore.DocumentReference {
  return firestore().collection("users").doc(uid).collection("workers").doc(workerId);
}

function workerResponse(snapshot: admin.firestore.DocumentSnapshot): Record<string, unknown> {
  const data = snapshot.data() || {};
  return {
    id: snapshot.id,
    name: data.name || snapshot.id,
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
    const identity = await authenticate(request);
    const path = request.path.replace(/^\/v1/, "").replace(/\/$/, "");

    if (request.method === "GET" && path === "/workers") {
      response.status(200).json(await listWorkers(identity.uid));
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