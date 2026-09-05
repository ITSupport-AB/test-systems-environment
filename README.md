# Freebuff Desktop — Unity GaaS Foundation

Cross-platform (iOS / Android / PC / WebGL) games-as-a-service base project:
monetized via subscriptions / battle pass, with cross-platform entitlement
(buy on iOS via StoreKit, keep VIP on PC), Firebase backend, cloud saves,
LiveOps, and a global leaderboard.

**Interactive architecture + rules demos:** open `docs/project-overview.html`.

## Layout

```
Assets/
  Editor/CompileCheck.cs              Headless CI compile check (buildMethod target)
  Resources/RemoteConfigDefaults.json Designer-editable Remote Config defaults
  Scripts/
    Core/       GameManager, AuthManager, SubscriptionManager, PlayerController, UIManager
    Data/       PlayerData (cloud-save model)
    Gameplay/   Generic FSM: SpawnState -> WaveState -> GameOverState
    Save/       SaveManager (debounced Firestore sync + local fallback)
    Leaderboard/LeaderboardManager (optimistic submit + rollback)
    LiveOps/    RemoteConfigManager (typed getters, update events)
    Backend/    ReceiptValidator (Unity IAP -> Cloud Function bridge)
    CloudFunctions/
      firestore.rules                 Owner-only data; VIP server-writable; monotonic leaderboard
      validateReceipt/index.ts        Apple/Google/Stripe receipt validation + anti-replay nonces
Tests/Editor/
  GameplayStateMachineTests.cs        28 EditMode NUnit tests
  test_leaderboard_headless.pl        Headless rule-verification suite (41 assertions)
.github/workflows/ci.yml              CI (see below)
docs/project-overview.html            Architecture overview + interactive demos (static)
```

## CI

`.github/workflows/ci.yml` runs on pull requests and pushes to `main`:

1. **Headless tests** — `perl Assets/Tests/Editor/test_leaderboard_headless.pl`
   (Perl ships with the runner; runs today with no setup).
2. **Unity compile check** — opens the project in a batchmode editor via
   `game-ci/unity-builder@v4` (`buildMethod: CompileCheck.Run`, `unityVersion: auto`).
   Unity aborts non-zero on "Scripts have compiler errors", failing the job.

The Unity job skips its compile cleanly (job stays green) until **both** of these are
configured — the guard steps print notices explaining what is missing:

1. **License:** add a `UNITY_LICENSE` secret to the repo
   (generate one: https://game.ci/docs/github/activation/). Secrets cannot be used in
   `if:` conditions, so the workflow derives `HAS_UNITY_LICENSE` from the secret at job
   level and gates on the env var.
2. **A real Unity project:** open this folder once in Unity Hub so
   `ProjectSettings/ProjectVersion.txt` and `Packages/` are generated.

## Local test (no Unity needed)

```bash
perl Assets/Tests/Editor/test_leaderboard_headless.pl   # expect: 41/41 passed
```

## Production setup checklist

- [ ] Open the folder in **Unity Hub** (2022.3 LTS) once — generates project files
- [ ] Add the `UNITY_LICENSE` repo secret (game-ci activation flow)
- [ ] Configure the Firebase project; deploy rules:
      `firebase deploy --only firestore:rules` (from `Assets/Scripts/CloudFunctions`)
- [ ] Deploy the receipt-validation function:
      `firebase deploy --only functions` (in `Assets/Scripts/CloudFunctions/validateReceipt`)
- [ ] Replace the commented `// In production, ...` SDK stubs in the Unity managers
      (Firestore, Unity IAP, Remote Config) with your real configuration
- [ ] Push LiveOps values to Remote Config to override the shipped JSON defaults

## Notes

- Leaderboard anti-inflation is enforced server-side in `firestore.rules`
  (score must be monotonically increasing; deletes denied; owner-only writes) —
  the client mirrors it for UX, not as the security boundary.
- Receipt validation runs server-side in a Cloud Function with SHA-256
  anti-replay nonces; clients never self-report VIP.
