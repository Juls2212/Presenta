# Presenta

**Presenta** (Control Inteligente de Presentaciones) is an academic Wear OS app that controls a presentation running on a Windows PC and helps the presenter monitor time, slide progress, pace, and alerts.

It is developed and tested with Android Studio and the Wear OS emulator, and is designed to work later with a physical watch on the same local network.

## Repository layout

| Folder | Purpose |
|---|---|
| `wear-app/` | Wear OS frontend (Kotlin, Jetpack Compose for Wear OS, Wear Material 3, DataStore). |
| `windows-companion/` | Windows backend and desktop UI (C#, .NET 10, WPF, Kestrel WebSocket server, SQLite history). |
| `shared-contracts/` | Language-neutral protocol specifications (JSON Schema + Markdown). |
| `docs/` | Architecture, decisions, roadmap, UI direction, requirements traceability, testing, environment. |

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
- **Phase 5 (watch client wired to the companion):** done and verified by automation: the watch now has a real repository (WebSocket, protocol v1, DataStore), a pairing screen, heartbeat with loss detection, and reconnection. 203 watch tests, 446 companion tests, and an opt-in cross-stack test pass. **Nothing has run on the emulator yet: that is your first manual test, step by step in `docs/manual-test-guide.md`.**
- **Phase 5.1 (slide control targeting):** done and verified by automation, plus an opt-in desktop test on this machine. The first manual run found that keys went to whichever window was in front (Android Studio), not to PowerPoint. The companion now has a "Ventana de la presentación" section: you choose the slideshow window, and every key is aimed at it after confirming it is in front, or nothing is pressed. 552 companion tests (plus 5 opt-in desktop tests, skipped by default) and 204 watch tests pass. **Test it with PowerPoint using section E of `docs/manual-test-guide.md`.**
- **Not built yet:** haptics and alert polish (Phase 6), the full history flow (7), robustness including physical-watch LAN support (8).

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
