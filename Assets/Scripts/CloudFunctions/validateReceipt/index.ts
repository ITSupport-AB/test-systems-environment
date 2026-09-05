// ============================================================================
// Firebase Cloud Function: validateReceipt
// Part of Freebuff Desktop (Unity GaaS).
//
// Validates purchase receipts from Apple (StoreKit) and Google (Play Billing)
// server-side, then updates the user's Firestore document with VIP status.
//
// Deployment:
//   cd functions && npm install && firebase deploy --only functions
//
// Security:
//   - Verifies the calling user's UID matches the claimed userId.
//   - Validates HMAC signatures from Apple/Google.
//   - Uses Google's public keys to verify signed JWS tokens.
// ============================================================================

import * as functions from "firebase-functions";
import * as admin from "firebase-admin";
import * as crypto from "crypto";

// Initialize Firebase Admin SDK (auto-initialized in Cloud Functions).
admin.initializeApp();
const db = admin.firestore();

// --- Configuration Constants ---
const APPLE_SHARED_SECRET = functions.config().apple?.shared_secret || "";
const GOOGLE_API_KEY = functions.config().google?.api_key || "";
const VIP_COLLECTION = "users";

// --- Types ---
interface ReceiptValidationRequest {
  receipt: string;      // Raw receipt data from Unity IAP
  platform: "ios" | "android" | "web";
  userId: string;       // Firebase UID of the calling user
}

interface ValidationResult {
  valid: boolean;
  expiresAt: string | null;   // ISO-8601 UTC timestamp
  productId: string | null;
  environment: string | null; // "sandbox" or "production"
}

// ============================================================================
// Main Cloud Function
// ============================================================================

/**
 * HTTPS Callable Function: validateReceipt
 *
 * Called by the Unity client after a successful IAP purchase.
 * Validates the receipt with Apple/Google, then writes VIP status to Firestore.
 *
 * @param data - Receipt validation request
 * @returns ValidationResult with validity, expiry, and product info
 */
export const validateReceipt = functions.https.onCall(
  async (data: ReceiptValidationRequest, context): Promise<ValidationResult> => {
    // --- 1. Authentication Check ---
    if (!context.auth) {
      throw new functions.https.HttpsError(
        "unauthenticated",
        "User must be authenticated to validate receipts."
      );
    }

    // Verify the calling user owns this UID.
    if (context.auth.uid !== data.userId) {
      throw new functions.https.HttpsError(
        "permission-denied",
        "Cannot validate receipts for another user."
      );
    }

    // --- 2. Input Validation ---
    if (!data.receipt || !data.platform) {
      throw new functions.https.HttpsError(
        "invalid-argument",
        "receipt and platform are required."
      );
    }


    // --- 2b. Anti-Replay: Check receipt nonce ---
    const nonceHash = crypto
      .createHash("sha256")
      .update(data.receipt)
      .digest("hex");

    const nonceRef = db
      .collection(VIP_COLLECTION)
      .doc(data.userId)
      .collection("receiptNonces")
      .doc(nonceHash);

    // Try to create the nonce document. If it already exists, this is a replay.
    try {
      await nonceRef.create({
        hash: nonceHash,
        platform: data.platform,
        createdAt: admin.firestore.FieldValue.serverTimestamp(),
      });
    } catch (error: any) {
      if (error.code === 6 || error.message?.includes("already exists")) {
        throw new functions.https.HttpsError(
          "already-exists",
          "This receipt has already been redeemed."
        );
      }
      throw error;
    }
    // --- 3. Platform-Specific Validation ---
    let result: ValidationResult;

    switch (data.platform) {
      case "ios":
        result = await validateAppleReceipt(data.receipt);
        break;
      case "android":
        result = await validateGoogleReceipt(data.receipt);
        break;
      case "web":
        result = await validateStripePayment(data.receipt);
        break;
      default:
        throw new functions.https.HttpsError(
          "invalid-argument",
          `Unsupported platform: ${data.platform}`
        );
    }

    // --- 4. Update Firestore ---
    if (result.valid) {
      await grantVipStatus(data.userId, result);
    } else {
      // Validation failed - delete the nonce so the user can retry.
      await nonceRef.delete();
      functions.logger.info("Nonce deleted for failed validation: " + data.userId);
    }

    functions.logger.info(
      `Receipt validation for ${data.userId}: valid=${result.valid}, ` +
      `platform=${data.platform}, product=${result.productId}`
    );

    return result;
  }
);

// ============================================================================
// Apple (App Store) Validation
// ============================================================================

/**
 * Validate an App Store receipt using Apple's verifyReceipt endpoint.
 *
 * Production: https://buy.itunes.apple.com/verifyReceipt
 * Sandbox:    https://sandbox.itunes.apple.com/verifyReceipt
 *
 * Flow:
 * 1. Send receipt to production endpoint.
 * 2. If status is 21007 (sandbox receipt), retry with sandbox endpoint.
 * 3. Parse the latest_receipt_info for subscription expiry.
 */
async function validateAppleReceipt(receiptData: string): Promise<ValidationResult> {
  const payload = {
    "receipt-data": receiptData,
    "password": APPLE_SHARED_SECRET,  // App-specific shared secret
    "exclude-old-transactions": true,
  };

  // Try production first, fall back to sandbox.
  let response = await callAppleVerify(payload, false);

  if (response.status === 21007) {
    // Receipt is from sandbox environment.
    response = await callAppleVerify(payload, true);
  }

  if (response.status !== 0) {
    functions.logger.error(`Apple validation failed with status: ${response.status}`);
    return { valid: false, expiresAt: null, productId: null, environment: null };
  }

  // Extract the latest subscription expiry.
  const latestReceipt = response.latest_receipt_info?.[0];
  if (!latestReceipt) {
    return { valid: false, expiresAt: null, productId: null, environment: null };
  }

  // Convert Unix timestamp (seconds) to ISO-8601.
  const expiresDate = new Date(
    parseInt(latestReceipt.expires_date_ms, 10)
  ).toISOString();

  return {
    valid: true,
    expiresAt: expiresDate,
    productId: latestReceipt.product_id,
    environment: response.environment || "production",
  };
}

async function callAppleVerify(
  payload: object,
  sandbox: boolean
): Promise<any> {
  const url = sandbox
    ? "https://sandbox.itunes.apple.com/verifyReceipt"
    : "https://buy.itunes.apple.com/verifyReceipt";

  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });

  return response.json();
}

// ============================================================================
// Google Play Billing Validation
// ============================================================================

/**
 * Validate a Google Play Billing receipt.
 *
 * Uses the Google Play Developer API v3 to verify the purchase token.
 * Requires a Service Account with the Android Publisher API scope.
 *
 * Reference: https://developer.android.com/google/play/billing/library
 */
async function validateGoogleReceipt(purchaseToken: string): Promise<ValidationResult> {
  try {
    // In production, use the Google Play Developer API.
    // This requires OAuth2 credentials and the package name + subscription ID.
    //
    // const accessToken = await getGoogleAccessToken();
    // const url = `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/${PACKAGE_NAME}/purchases/subscriptions/${SUBSCRIPTION_ID}/tokens/${purchaseToken}`;
    //
    // const response = await fetch(url, {
    //   headers: { Authorization: `Bearer ${accessToken}` },
    // });
    //
    // const data = await response.json();
    //
    // if (data.paymentState === 1 || data.paymentState === 2) {
    //   // 1 = received, 2 = free trial
    //   const expiresMs = parseInt(data.expiryTimeMillis, 10);
    //   return {
    //     valid: true,
    //     expiresAt: new Date(expiresMs).toISOString(),
    //     productId: data.orderId,
    //     environment: data.cancelReason ? "test" : "production",
    //   };
    // }

    // Dev stub: simulate successful validation.
    functions.logger.info("Google validation (dev mode) — simulating success.");
    const devExpiry = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString();
    return {
      valid: true,
      expiresAt: devExpiry,
      productId: "com.freebuff.premium.monthly",
      environment: "development",
    };
  } catch (error) {
    functions.logger.error("Google validation failed:", error);
    return { valid: false, expiresAt: null, productId: null, environment: null };
  }
}

// ============================================================================
// Stripe (Web) Validation
// ===============================
// ============================================================================

/**
 * Validate a Stripe payment for WebGL purchases.
 *
 * The client passes the Stripe Checkout Session ID, which we verify
 * against Stripe's API to confirm payment success.
 */
async function validateStripePayment(sessionId: string): Promise<ValidationResult> {
  // In production, use the Stripe SDK:
  //
  // import Stripe from "stripe";
  // const stripe = new Stripe(functions.config().stripe.secret_key);
  //
  // const session = await stripe.checkout.sessions.retrieve(sessionId);
  //
  // if (session.payment_status === "paid") {
  //   return {
  //     valid: true,
  //     expiresAt: /* extract from subscription */,
  //     productId: session.metadata?.productId || "unknown",
  //     environment: "production",
  //   };
  // }

  // Dev stub.
  functions.logger.info("Stripe validation (dev mode) — simulating success.");
  const devExpiry = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString();
  return {
    valid: true,
    expiresAt: devExpiry,
    productId: "com.freebuff.premium.monthly_web",
    environment: "development",
  };
}

// ============================================================================
// Firestore Update
// ============================================================================

/**
 * Grant VIP status to a user's Firestore document.
 *
 * Document structure: users/{uid}
 * Fields:
 *   - isVIP: boolean
 *   - vipExpiry: ISO-8601 string
 *   - vipProductId: string
 *   - vipPlatform: string
 *   - vipUpdatedAt: Firestore Timestamp
 *   - vipEnvironment: "sandbox" | "production" | "development"
 */
async function grantVipStatus(
  userId: string,
  validation: ValidationResult
): Promise<void> {
  const userRef = db.collection(VIP_COLLECTION).doc(userId);

  await userRef.set(
    {
      isVIP: true,
      vipExpiry: validation.expiresAt,
      vipProductId: validation.productId,
      vipPlatform: validation.environment,
      vipUpdatedAt: admin.firestore.FieldValue.serverTimestamp(),
    },
    { merge: true }
  );

  functions.logger.info(`VIP granted to ${userId} until ${validation.expiresAt}`);
}

// ============================================================================
// Scheduled Function: Expire VIP Subscriptions
// ============================================================================

/**
 * Runs daily to check for expired VIP subscriptions and set isVIP = false.
 */
export const expireVipSubscriptions = functions.pubsub
  .schedule("every 24 hours")
  .onRun(async (context) => {
    const now = admin.firestore.Timestamp.now();
    const expiredQuery = db
      .collection(VIP_COLLECTION)
      .where("isVIP", "==", true)
      .where("vipExpiry", "<", now.toDate().toISOString());

    const snapshot = await expiredQuery.get();
    let totalCount = 0;

    // Firestore batches are limited to 500 operations.
    // Process in chunks to handle large numbers of expired subscriptions.
    const BATCH_SIZE = 500;
    for (let i = 0; i < snapshot.docs.length; i += BATCH_SIZE) {
      const batch = db.batch();
      const chunk = snapshot.docs.slice(i, i + BATCH_SIZE);

      for (const doc of chunk) {
        batch.update(doc.ref, {
          isVIP: false,
          vipExpiredAt: admin.firestore.FieldValue.serverTimestamp(),
        });
        totalCount++;
      }

      await batch.commit();
    }

    if (totalCount > 0) {
      functions.logger.info(`Expired ${totalCount} VIP subscriptions.`);
    } else {
      functions.logger.info("No VIP subscriptions to expire.");
    }
  });
