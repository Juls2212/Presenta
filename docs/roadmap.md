# Roadmap

Constraint: the user cannot run the app manually until Phase 5. Phases 1–4 are verified by automation only.

| # | Phase | Depends on |
|---|---|---|
| 1 | Foundations | none |
| 2 | Protocol v1 and C# domain core | 1 |
| 3 | Wear OS UI with fake data | 1 (2 for message shapes) |
| 4 | Windows companion | 2 |
| 5 | First runnable milestone | 3, 4 |
| 6 | Alerts and recommendations end to end | 5 |
| 7 | Full history flow | 5 |
| 8 | Robustness | 6, 7 |
| 9 | Accessibility, QA, demo | 8 |
| 10 | Optional external AI provider | 9 |

## Phase 1: Foundations

- **Goal:** Structure, decisions, environment check, mockup intake.
- **Done when:** Documentation files exist, are consistent, and the verification searches pass. No source or build files.
- **Validation:** File and tree listing, forbidden-term search, traceability check, Mermaid re-read.

## Phase 2: Protocol v1 contracts and C# domain core

- **Goal:** Message schemas (including `session.summary`), session engine, timer, slide counter, pace, alert rules, deterministic recommendations.
- **Done when:** Schemas cover every message; domain behavior matches the ADRs; all tests pass.
- **Validation:** `dotnet test` with unit tests (fake time) and contract tests against the JSON Schemas.
- **Status:** Done. `dotnet build windows-companion/Presenta.Companion.sln -warnaserror` succeeds with 0 warnings and `dotnet test` passes 327 tests (126 domain, 201 protocol). Delivered: `shared-contracts/protocol-v1.md`, 21 schemas, valid and invalid examples, `Presenta.Domain`, `Presenta.Protocol`, and their test projects. Nothing here has run against a real socket, keyboard, or watch; that starts in Phases 4 and 5.

## Phase 3: Wear OS UI with fake data

- **Goal:** Design system, navigation, all screens and states (normal, alert, paused, overtime, disconnected, reconnecting, finished, history, empty history, slide selector, finish confirmation).
- **Done when:** Every screen in `ui-direction.md` renders on Large Round and Small Round, with Spanish strings in resources.
- **Validation:** Gradle build, JVM Compose UI tests, screenshot tests, and previews reviewed by the user.
- **Status:** Built and verified by automation; the previews still await your review. `assembleDebug`, `testDebugUnitTest` (87 tests passing), and `lintDebug` (0 errors) succeed, and 44 screenshots (22 states on Large and Small Round) are in `docs/ui-previews/`. Everything runs on fake data behind `PresentaRepository`. Nothing has run on an emulator or a watch, and there is no networking, DataStore, or haptics yet. Open points: the derived screens need your approval, and the progress-ring meaning is undecided.

## Phase 4: Windows companion

- **Goal:** WPF shell, Kestrel WebSocket server, pairing, keyboard interface (SendInput and fake), SQLite history.
- **Done when:** Server, pairing, session flow, key rules, and history work in-process.
- **Validation:** `dotnet build` and `dotnet test` with in-memory integration tests (real WebSocket client, fake keyboard, fake time).
- **Status:** Built and verified by automation. `dotnet build windows-companion/Presenta.Companion.sln -warnaserror` succeeds with 0 warnings and `dotnet test` passes 446 tests (domain 126, protocol 201, persistence 15, server 65, Windows keyboard 7, view models 32). The WPF project builds from the command line with only the .NET SDK, and the built app was started, answered a pairing attempt on a real socket, created its database, and exited cleanly. Not verified: real key injection, how the window looks, a physical watch or the emulator, and the Windows Firewall. A draft `manual-test-guide.md` is ready for Phase 5.

## Phase 5.1: Slide control targeting

- **Goal:** Fix the defect found in the first manual run: keys went to the foreground window instead of the presentation.
- **Status:** Built and verified by automation and by the opt-in desktop test on this machine (real windows, separate processes). Pending: your manual run with PowerPoint (section E of `manual-test-guide.md`).

## Phase 5: First runnable milestone

- **Goal:** Watch WebSocket client, connection states, setup, start, next/previous with real keys, timer and counter.
- **Done when:** The user can run the companion and the emulator and drive a real presentation.
- **Validation:** Manual test guide (first manual tests), plus all automated suites green.
- **Status:** Built and verified by automation; **the manual run is yours to do** with `docs/manual-test-guide.md`. The watch now has a real repository (OkHttp WebSocket, protocol v1, DataStore, a pairing screen, heartbeat with loss detection in at most 3.5 s, capped reconnect backoff, commands never queued, an elapsed-time ring). `assembleDebug`, `testDebugUnitTest` (203 tests), `lintDebug` (0 errors) pass, and so do `dotnet build -warnaserror` and `dotnet test` (446 tests); the opt-in `:app:crossStackTest` (4 tests) runs the real watch client against the real companion server. Not verified: anything on the emulator or a physical watch, real key presses into a slideshow, how the screens look on real hardware, and latency and loss detection on a real network (RNF 03, RNF 04). A physical watch on a LAN address is not supported yet (ADR-46, Phase 8).

## Phase 6: Alerts and recommendations end to end

- **Goal:** Alert rules and recommendations visible on the watch with haptics.
- **Done when:** Alert state appears and clears correctly with controls usable (RF 10, RF 11).
- **Validation:** Automated rule tests; manual check of visual and haptic alerts.

## Phase 7: Full history flow

- **Goal:** Auto-save, summary, history list, read-only summary.
- **Done when:** Records persist across restarts and the watch lists them (RF 13, RF 14).
- **Validation:** Integration tests plus manual restart test.

## Phase 8: Robustness

- **Goal:** Reconnection, connection loss without ending the session, latency targets, network security config.
- **Done when:** RNF 03 and RNF 04 targets are met and the grace-period behavior works (RF 15).
- **Validation:** Fake-time tests, manual network interruption tests, latency measurements.

## Phase 9: Accessibility, final QA, demo preparation

- **Goal:** Touch targets, contrast, small-screen checks, consistent states, demo script.
- **Done when:** Checklists pass and a demo run completes without errors.
- **Validation:** Accessibility checklist, full manual run, regression suites.

## Phase 10: Optional external AI provider

- **Goal:** An external provider behind `IRecommendationProvider`.
- **Done when:** The provider is optional, off by default, and the rule-based provider remains the fallback.
- **Validation:** Provider contract tests with a fake provider; manual check. Requires a separate decision about credentials and privacy.

