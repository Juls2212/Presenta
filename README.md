# Presenta

**Presenta** (Control Inteligente de Presentaciones) is an academic Wear OS app that controls a presentation running on a Windows PC and helps the presenter monitor time, slide progress, pace, and alerts.

It is developed and tested with Android Studio and the Wear OS emulator, and is designed to work later with a physical watch on the same local network.

## Repository layout

| Folder | Purpose |
|---|---|
| `wear-app/` | Wear OS frontend (Kotlin, Jetpack Compose for Wear OS, Wear Material 3, DataStore). |
| `windows-companion/` | Windows backend and desktop UI (C#, .NET 10, WPF, Kestrel WebSocket server, SQLite history). |
| `shared-contracts/` | Language-neutral protocol specifications (JSON Schema + Markdown). |
| `docs/` | Architecture, decisions, roadmap, UI direction, requirements traceability, testing, environment, and `docs/validation/` (the manual validation guide and the latest automated run). |
| `scripts/` | `verify-all.ps1` (runs every automated check and writes `docs/validation/automated-run-latest.md`) and `check-guide-strings.ps1` (checks the Spanish text quoted in the guides against the resource files). |

The project is a **modular monolith** in a single repository, with a clear frontend/backend separation. It is not a microservice system.

## Version 1 scope

Wear OS app; Windows companion; local connection; next/previous slide commands; timer and remaining time; manual slide counter; pace calculation; rule-based recommendations; visual and haptic alerts; history stored on Windows and queried by the watch.

Out of scope for version 1: direct PowerPoint/Canva/Google Slides APIs, slide content analysis, mouse control, gestures, cloud accounts, a phone intermediary, complex dashboards, microservices, and any real external AI API.

## Documentation

- [Architecture](docs/architecture.md)
- [Decisions (ADRs)](docs/decisions.md)
- [Roadmap](docs/roadmap.md)
- [UI direction](docs/ui-direction.md)
- [Requirements traceability](docs/requirements-traceability.md)
- [Testing strategy](docs/testing-strategy.md)
- [Environment](docs/environment.md)
- [Mockups](docs/mockups/README.md)

## Status

- **Phase 1 (foundations):** documentation, done.
- **Phase 2 (protocol v1 and domain core):** done and proven only by automated tests: the protocol contracts in `shared-contracts/` and the C# domain and protocol libraries in `windows-companion/`. 327 tests pass.
- **Phase 3 (Wear OS UI with fake data):** done and verified by automation (build, 87 JVM tests, lint). All screens and states exist in `wear-app/` and are rendered in `docs/ui-previews/`. It runs on fake data only; the derived screens await review.
- **Phase 4 (Windows companion):** done and verified by automation: the WebSocket server with pairing, the Windows keyboard interface, SQLite history, and the WPF window. 446 tests passed at the end of that phase; the app starts. Real key presses, the window's appearance, and any watch or emulator connection are not verified yet.
- **Phase 5 (watch client wired to the companion):** done and verified by automation: the watch now has a real repository (WebSocket, protocol v1, DataStore), a pairing screen, heartbeat with loss detection, and reconnection. 203 watch tests, 446 companion tests, and an opt-in cross-stack test pass. **Nothing has run on the emulator yet: that is your first manual test, step by step in `docs/validation/validation-guide.md` (setup in `docs/manual-test-guide.md`).**
- **Phase 5.1 (slide control targeting):** done and verified by automation, plus an opt-in desktop test on this machine. The first manual run found that keys went to whichever window was in front (Android Studio), not to PowerPoint. The companion now has a "Ventana de la presentación" section: you choose the slideshow window, and every key is aimed at it after confirming it is in front, or nothing is pressed. 552 companion tests (plus 5 opt-in desktop tests, skipped by default) and 204 watch tests pass. **Test it with PowerPoint using rows 30 to 42 of `docs/validation/validation-guide.md`.**
- **Phase 6 (haptics, alerts with real data, screen on, safe shutdown, history dates):** done and verified by automation. The watch vibrates once per new alert (four patterns, debug-only Logcat line per event); the exact second of every alert for example sessions is in `docs/validation/validation-guide.md` (rows 43 to 56) and asserted by domain tests; the screen stays on during a session; closing the companion during a session asks first and saves one interrupted record; history dates read "Hoy", "Ayer", "08 Oct". Alerts carry a new additive `sequence` field in protocol v1. 574 companion tests (plus 5 opt-in desktop tests), 234 watch tests, and 6 opt-in cross-stack tests pass. **Real vibration needs a physical watch; the emulator vibrates nothing you can feel.**
- **Phase 7a (validation kit):** done and verified by automation. `docs/validation/validation-guide.md` is the single manual guide (85 numbered tests, 12 visual checks, a defects log and a sign-off); debug builds measure command round trips and connection loss (`PresentaTiming`); `scripts/verify-all.ps1 [-CrossStack] [-Desktop]` runs everything and records the result. 576 companion tests (plus 5 opt-in desktop tests), 238 watch tests and 8 opt-in cross-stack tests pass. **The manual campaign itself is yours to run.**
- **Phase 7b-1 (real PowerPoint key delivery):** done and verified by automation. Every failure of a key now says why in the activity list, "Copiar diagnóstico" copies a report of the activation without pressing any key, and a window owned by the chosen one (a slide show owned by its frame) counts as in front (ADR-66). The real PowerPoint was not run; the report from your machine is what confirms the cause there.
- **Phase 8 (robustness and usability):** done and verified by automation. The only PowerPoint slide show is chosen by itself and the other PowerPoint window gets a warning; a running session survives a crash and is saved once as interrupted at the next start; an optional "Recordar código de vinculación" keeps the watch paired across companion restarts; the watch never gives up reconnecting during a session; a second companion refuses to start. 712 companion tests (plus 11 opt-in desktop tests), 250 watch tests and 11 opt-in cross-stack tests pass. **Rows 89 to 99 of the validation guide are yours to run.**
- **Phase 10 (optional AI wording of the recommendation):** done and verified by automation, **without any call to a real provider**. The companion can ask a chat-completions provider for a one-sentence Spanish tip (only codes and rounded numbers are sent; the key comes from an environment variable and is never stored or shown), and the watch shows it in the alert pill; the product is unchanged when the AI is off, missing, slow, failing or refused, and the pace, alerts and timing stay deterministic. 906 companion tests (plus 11 opt-in desktop tests), 269 watch tests and 13 opt-in cross-stack tests pass. **Rows 100 to 107 of the validation guide are yours to run; row 103 needs a real provider and key.**
- **Not built yet:** the manual validation campaign results (7), physical-watch LAN support and network security configuration (beyond Phase 8).

To build and test the companion libraries (requires the .NET 10 SDK):

```
dotnet build windows-companion/Presenta.Companion.sln -warnaserror
dotnet test windows-companion/Presenta.Companion.sln
```

To build and test the watch app (requires JDK 21 and the Android SDK; see `wear-app/README.md`):

```
cd wear-app
gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
gradlew.bat :app:crossStackTest    # optional: real watch client against the real server, needs the .NET SDK
```
