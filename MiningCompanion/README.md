# Mining Companion

Mining Companion is an Android control surface for a remote mining worker. It is intentionally not an on-device miner: phones are poor mining hardware, sustained hashing creates heat and battery wear, and Google Play policy does not allow apps that mine cryptocurrency on the device.

## Current prototype

- Native Kotlin and Jetpack Compose UI
- Remote worker status card
- Visible start and stop controls
- Hashrate, temperature, and uptime presentation
- Explicit safety copy stating that the phone does not mine or hold wallet keys

The worker values are currently local demo state. The next integration replaces that state with the API described in `docs/RESEARCH.md`.

## Build

Open `MiningCompanion` in Android Studio and use JDK 17 with Android SDK 35. The project expects the Android Gradle Plugin and Kotlin plugins to be downloaded from Google Maven and Maven Central.

Set the real API endpoint through a non-committed Gradle property when building:

```text
./gradlew :app:assembleRelease -PapiBaseUrl=https://api.example.com
```

Configure release signing in CI or Android Studio; signing keys must never be committed.

```text
./gradlew :app:assembleDebug
```

## Product boundary

The app may authenticate to and control a user's own remote worker, display pool and profitability data, and send explicit commands. It must not silently run a miner, use a hidden background service, collect wallet seed phrases, or use a user's phone as an undisclosed worker.