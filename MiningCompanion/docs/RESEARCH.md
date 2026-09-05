# Crypto mining Android app research

## Decision

Build a remote-worker companion app, not a phone miner. The phone should provide authentication, worker controls, alerts, telemetry, and profitability estimates. Hashing should run on hardware selected for the algorithm and electricity price, such as an ASIC or a user-owned GPU rig.

This is the best performance path because mobile thermal limits, battery energy density, and Android process scheduling make sustained phone hashing uneconomical. It is also the viable distribution path: Google Play does not allow apps that mine cryptocurrency on the device. Any background Android work must be user-visible and justified by the Android foreground-service rules.

## Architecture

```text
Android Compose app
    | HTTPS, short-lived access token
    v
Mining control API
    | mTLS or signed worker command
    v
Worker gateway / agent  --->  ASIC or GPU worker
    |
    +--> pool telemetry and payout data
```

### Android client

- Kotlin, Jetpack Compose, Material 3
- Credential storage in Android Keystore-backed storage
- No wallet seed phrases or private keys
- Poll telemetry while the screen is open; use push notifications for critical alerts
- Show the worker name, command state, last update, and temperature on every control surface
- Require a deliberate confirmation for start, stop, pool changes, and firmware actions

### Control API

The first API contract should expose:

- `GET /v1/workers`: worker identity, algorithm, state, hashrate, temperature, uptime, and last seen time
- `POST /v1/workers/{id}/commands`: `START`, `STOP`, or `RESTART` with an idempotency key
- `GET /v1/workers/{id}/telemetry?window=1h`: downsampled time series for the chart
- `GET /v1/profitability`: pool-reported yield plus electricity cost and fee assumptions

The first Firebase gateway implementation lives in `Assets/Scripts/CloudFunctions/validateReceipt/workerGateway.ts`.
It authenticates Firebase ID tokens, reads user-owned worker documents, queues commands, applies a
temperature/offline guard, and rejects reused idempotency keys. A separate worker agent still needs
to consume `users/{uid}/workerCommands` and report telemetry back to Firestore. The gateway now
exposes agent-only heartbeat and command-claim routes using a server-provisioned SHA-256 worker-key
hash stored at `workerAgents/{workerId}`; the raw worker key must never be stored in Firestore.

Commands should be authorized server-side, logged, rate-limited, and rejected when the worker is stale or over its configured temperature limit. The gateway must persist and enforce each client-provided idempotency key so retries cannot execute a command twice. The Android client is not a security boundary.

## Performance strategy

1. Benchmark candidate algorithms on the actual remote hardware.
2. Compare net yield after pool fee, electricity, cooling, and hardware depreciation.
3. Prefer hardware efficiency in joules per hash over raw hashrate.
4. Keep telemetry low-bandwidth by sending aggregates and only requesting detailed samples on demand.
5. Use exponential backoff for unavailable workers and never spin a tight retry loop.
6. Make temperature and power limits explicit safety controls owned by the worker agent.

## How work is sourced

The production path is pooled mining. A pool connects to the selected blockchain network,
creates current block-template jobs, and sends Stratum work to the miner. The miner returns
shares; the pool tracks accepted work and pays according to its published payout rules. The
Android app should select a coin/profile, but it should never construct block templates or
hold chain private keys.

Solo mining is a separate product mode requiring a fully synchronized coin node, a stratum
bridge, reliable peer connectivity, and a payout address compatible with that chain. The
worker agent rejects solo mode until those components are installed and tested.

## Security and abuse controls

- Use OAuth/OIDC or passkeys for account authentication.
- The current prototype accepts an access token to exercise the gateway contract; replace this with OAuth/OIDC or passkeys before external release.
- Bind commands to a device session and require re-authentication for sensitive actions.
- Store only opaque access tokens, never wallet secrets.
- Use TLS certificate validation and server-side authorization per worker.
- Keep an append-only audit log for control commands.
- Make mining state and network activity visible to the user.

## Sources

- [Google Play Developer Program Policies](https://support.google.com/googleplay/android-developer/answer/10177647)
- [Android foreground services overview](https://developer.android.com/develop/background-work/services/fgs)
- [Android power and performance guidance](https://developer.android.com/topic/performance/power)
- [Android Keystore system](https://developer.android.com/privacy-and-security/keystore)
- [Luno API reference](https://www.luno.com/en/developers/api) (`GET /api/1/funding_address`, `Perm_R_Addresses`)