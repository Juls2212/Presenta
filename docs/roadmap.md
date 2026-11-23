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
- **Status:** Built and verified by automation and by the opt-in desktop test on this machine (real windows, separate processes). Pending: your manual run with PowerPoint (rows 30 to 42 of `validation/validation-guide.md`).

## Phase 5: First runnable milestone

- **Goal:** Watch WebSocket client, connection states, setup, start, next/previous with real keys, timer and counter.
- **Done when:** The user can run the companion and the emulator and drive a real presentation.
- **Validation:** Manual test guide (first manual tests), plus all automated suites green.
- **Status:** Built and verified by automation; **the manual run is yours to do** with `docs/validation/validation-guide.md`. The watch now has a real repository (OkHttp WebSocket, protocol v1, DataStore, a pairing screen, heartbeat with loss detection in at most 3.5 s, capped reconnect backoff, commands never queued, an elapsed-time ring). `assembleDebug`, `testDebugUnitTest` (203 tests), `lintDebug` (0 errors) pass, and so do `dotnet build -warnaserror` and `dotnet test` (446 tests); the opt-in `:app:crossStackTest` (4 tests) runs the real watch client against the real companion server. Not verified: anything on the emulator or a physical watch, real key presses into a slideshow, how the screens look on real hardware, and latency and loss detection on a real network (RNF 03, RNF 04). A physical watch on a LAN address is not supported yet (ADR-46, Phase 8).

## Phase 6: Alerts and recommendations end to end

- **Goal:** Alert rules and recommendations visible on the watch with haptics.
- **Done when:** Alert state appears and clears correctly with controls usable (RF 10, RF 11).
- **Validation:** Automated rule tests; manual check of visual and haptic alerts.
- **Status:** Built and verified by automation. Haptics through an abstraction (four patterns, one per new alert identified by the new `sequence` field, none on repeats, acknowledgments, or reconnections, a debug-only Logcat line per event); the exact second of every alert for three example sessions asserted in domain tests and written into `validation/validation-guide.md`; the screen stays on during a session; closing the companion during a session asks first and saves one interrupted record; history dates read "Hoy", "Ayer", "08 Oct". `DevHost --time-scale` lets the opt-in cross-stack test see every alert kind from the real server in seconds. Counts: companion 574 tests, watch 234, cross-stack 6. **Not verified:** how the vibration feels (needs a physical watch), anything on the emulator, how the closing question looks, and the rows 43 to 68 of the validation guide, which are yours to run.

## Phase 7: Full manual validation campaign

- **Goal:** Validate the whole product by hand against the seven acceptance criteria: auto-save, summary, history, reconnection, alerts, targeting, and the visual review of the derived screens. (The history flow itself was built earlier: Phases 4 to 6.)
- **Done when:** The sign-off table of `validation/validation-guide.md` is filled in and accepted.
- **Validation:** The user runs `validation/validation-guide.md`; the automated side is `scripts/verify-all.ps1`.
- **Phase 7a (the validation kit):** done. The consolidated guide (rows 1 to 85, visual checks V1 to V12, defects log, sign-off), debug-only timing diagnostics on both sides (`PresentaTiming`), cross-stack tests that measure loopback latency and loss detection, `scripts/verify-all.ps1` (writes `validation/automated-run-latest.md`) and `scripts/check-guide-strings.ps1`. Counts at the end of 7a: companion 576 tests (plus 5 opt-in desktop tests), watch 238, cross-stack 8.

## Phase 8: Robustness

- **Status:** The usability and resilience part is done and verified by automation (ADR-67 to ADR-71): automatic window choice and a warning for the non-slide-show PowerPoint window, a crash-safe active session recovered exactly once, an optional remembered pairing code, a watch that never gives up reconnecting during a session, and a single companion instance. Counts: companion 712 tests (plus 11 opt-in desktop), watch 250, cross-stack 11. Still open from the original goal: a physical watch on a LAN address and the network security configuration, which this phase did not touch.
- **Goal:** Reconnection, connection loss without ending the session, latency targets, network security config.
- **Done when:** RNF 03 and RNF 04 targets are met and the grace-period behavior works (RF 15).
- **Validation:** Fake-time tests, manual network interruption tests, latency measurements.

## Phase 10: Optional AI wording (done ahead of Phase 9)

- **Status:** Built and verified by automation (ADR-72 to ADR-79): a provider-agnostic HTTP client (`Presenta.Ai`), a background enricher with cache, limits, circuit breaker and sanitation, the "Recomendaciones con IA" section, and the watch showing `freeText` in the alert pill. Counts: companion 906 tests (plus 11 opt-in desktop), watch 269, cross-stack 13. Not verified: any live provider.

## Phase 9: Accessibility, final QA, demo preparation

- **Goal:** Touch targets, contrast, small-screen checks, consistent states, demo script.
- **Done when:** Checklists pass and a demo run completes without errors.
- **Validation:** Accessibility checklist, full manual run, regression suites.

## Phase 10: Optional external AI provider

- **Goal:** An external provider behind `IRecommendationProvider`.
- **Done when:** The provider is optional, off by default, and the rule-based provider remains the fallback.
- **Validation:** Provider contract tests with a fake provider; manual check. Requires a separate decision about credentials and privacy.

