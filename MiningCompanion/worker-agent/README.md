# Worker agent

This process runs on the user's own mining rig. It polls the Firebase worker gateway for queued commands and posts validated telemetry. It is not a miner and does not contain pool or wallet credentials.

## Setup

1. Copy `config.example.json` to `config.json`.
2. Set the HTTPS gateway URL, worker ID, and worker key provisioned by the backend operator.
3. Choose a profile from `coin-profiles.example.json` that matches the actual rig hardware.
4. Set `coin`, `network`, `pool_url`, `algorithm`, and `payout_address`. The payout address may be copied from your Luno receiving-wallet screen; do not use a seed phrase or private key.
5. Configure `telemetry_command` as an argv list that returns JSON with `state`, `hashrate`, `temperature_celsius`, and `uptime` from the installed miner.
6. Replace the command arrays with an installed miner controller executable and fixed arguments. Supported placeholders are `{coin}`, `{network}`, `{pool_url}`, `{pool_user}`, `{pool_password}`, `{worker_name}`, `{payout_address}`, and `{algorithm}`.
7. Run with Python 3.10+:

```text
python agent.py
```

The agent rejects non-HTTPS gateways, rejects solo mode without a node/stratum bridge, and invokes commands with `shell=False`. Keep `config.json` readable only by the service account and never commit it.

The pool supplies current work from the selected blockchain and pays accepted shares according to its payout rules. Luno must support deposits for the selected coin and network; do not assume a Luno address works for every coin. The agent does not currently call Luno. Luno address discovery must be added server-side against the current official Luno API after verifying account permissions and withdrawal-address behavior. Never place Luno API keys, secrets, or wallet seed phrases in the Android app or worker config.