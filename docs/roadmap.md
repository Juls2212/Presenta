# Roadmap

The roadmap records what each phase delivered and the real final state. Every phase was verified by automation; **no phase has a manual result yet**: the manual campaign (the full guide, or at least the 10-minute smoke test) is the one open step. Test counts quoted in a phase are the counts at the end of that phase; the current totals are in [validation/automated-run-latest.md](validation/automated-run-latest.md).

## Final state at a glance

| Item | State |
|---|---|
| Companion tests (default run) | 1138 pass, 12 opt-in tests skipped |
| Watch unit tests | 270 pass, lint 0 errors |
| Cross-stack tests (opt-in) | 13 pass |
| Manual validation | **Not run.** Full guide: [validation/validation-guide.md](validation/validation-guide.md) (120 numbered rows, 12 visual checks). Minimum: [validation/smoke-test.md](validation/smoke-test.md). |
| Demo | [delivery/demo-script.md](delivery/demo-script.md) and `scripts/demo-preflight.ps1` |
| Accessibility | [validation/accessibility-report.md](validation/accessibility-report.md) (automated evidence only) |

## Phases

| # | Phase | State |
|---|---|---|
| 1 | Foundations | Done |
| 2 | Protocol v1 and C# domain core | Done, automated |
| 3 | Wear OS UI with fake data | Done, automated; the previews still await your review |
| 4 | Windows companion | Done, automated |
| 5 | First runnable milestone (watch wired to the companion) | Done, automated; the manual run is yours |
| 5.1 | Slide control targeting | Done, automated and opt-in desktop tests |
| 6 | Alerts end to end, haptics, screen on, safe shutdown, history dates | Done, automated |
| 7a | Validation kit | Done |
| 7b-1 | Real PowerPoint key delivery diagnostics | Done, automated; PowerPoint itself not run |
| 7 | Full manual validation campaign | **Open: yours to run** |
| 8 | Robustness and usability (part done; physical watch deferred) | Done for the listed items, automated |
| 10 | Optional AI wording (10b request shape, 10c local `.env` launcher) | Done, automated; no real provider called |
| 9 | Delivery kit: final documentation, demo script, preflight, smoke test, accessibility report | Done |
| 11 | Desktop companion redesign: navigation rail, six pages, design tokens, glass look (native WPF) | Done, automated; the look on your monitor is yours to check (guide rows 110 to 118) |
| 12 | Release artifacts: self-contained Windows package and Wear OS debug APK in `releases/` | Built and checked without running them; the clean-unzip and emulator tests are yours |
| 13 | Application icon, rail logo, Inicio layout, verified window status | Done, automated; the look is yours to check (guide rows 119 and 120) |

(The numbers 9 and 10 were built in the order 10, then 9, because the AI work was ready first. Phase 10 below was first planned as an `IRecommendationProvider` behind the rules; what shipped is the enrichment of the text only, so the deterministic provider is never replaced.)

## Phase 1: Foundations

- **Goal:** Structure, decisions, environment check, mockup intake.
- **State:** Done. Documentation files, no source.

## Phase 2: Protocol v1 contracts and C# domain core

- **Goal:** Message schemas (including `session.summary`), session engine, timer, slide counter, pace, alert rules, deterministic recommendations.
- **State:** Done. 21 schemas, valid and invalid examples, `Presenta.Domain`, `Presenta.Protocol`; 327 tests at the end of the phase (126 domain, 201 protocol). Nothing ran against a socket, keyboard or watch.

## Phase 3: Wear OS UI with fake data

- **Goal:** Design system, navigation, all screens and states on Large Round and Small Round with Spanish strings in resources.
- **State:** Built and verified by automation (87 JVM tests, lint 0 errors, 44 screenshots in `docs/ui-previews/`, 52 now with the four pairing states). The derived screens await your approval; the progress ring's meaning was later fixed as elapsed time (Phase 5).

## Phase 4: Windows companion

- **Goal:** WPF shell, Kestrel WebSocket server, pairing, keyboard interface (SendInput and fake), SQLite history.
- **State:** Built and verified by automation (446 tests at the end of the phase). The built app was started once, answered a pairing attempt on a real socket and exited cleanly.

## Phase 5: First runnable milestone

- **Goal:** Watch WebSocket client, connection states, setup, start, next and previous with real keys, timer and counter.
- **State:** Built and verified by automation (203 watch tests, 446 companion tests, 4 cross-stack tests). The watch has a real repository (OkHttp WebSocket, protocol v1, DataStore, a pairing screen, heartbeat with loss detection in at most 3.5 s, capped reconnect backoff, commands never queued).

## Phase 5.1: Slide control targeting

- **Goal:** Fix the defect of the first manual run: keys went to the foreground window instead of the presentation.
- **State:** Built and verified by automation and by the opt-in desktop tests (real windows in separate processes). The companion aims keys at a chosen window after confirming it is in front, or presses nothing.

## Phase 6: Alerts and recommendations end to end

- **Goal:** Alert rules and recommendations visible on the watch, with haptics.
- **State:** Built and verified by automation (574 companion tests, 234 watch tests, 6 cross-stack at the end of the phase): haptic patterns through an abstraction, the exact second of every alert for example plans asserted and written into the guide, the screen kept on during a session, a question when closing the companion during a session, history dates. How the vibration feels needs a physical watch.

## Phase 7: Full manual validation campaign

- **Goal:** Validate the whole product by hand against the seven acceptance criteria.
- **Phase 7a (the validation kit):** done: the consolidated guide, debug-only timing diagnostics, cross-stack timing tests, `verify-all.ps1`, `check-guide-strings.ps1`.
- **Phase 7b-1:** done: every key failure says why, "Copiar diagnóstico" produces a report without pressing any key, and a window owned by the chosen one counts as in front (ADR-66). Real PowerPoint was not run.
- **State of the campaign:** **open.** The sign-off table of the guide is empty. The recommended minimum is [validation/smoke-test.md](validation/smoke-test.md).

## Phase 8: Robustness

- **Goal:** Reconnection, connection loss without ending the session, latency targets, network security configuration, usability hardening.
- **State:** Done and verified by automation for ADR-67 to ADR-71: automatic choice of the only slide show and a warning for the other PowerPoint window, a crash-safe active session recovered exactly once, an optional remembered pairing code, a watch that never gives up during a session, a single companion instance. 712 companion tests, 250 watch tests, 11 cross-stack at the end of the phase.
- **Deferred:** see the next section.

## Phase 10: Optional AI wording

- **Goal:** An optional external provider that words the recommendation text, off by default, with the deterministic wording as the fallback.
- **State:** Built and verified by automation (ADR-72 to ADR-80): a provider-agnostic HTTP client (`Presenta.Ai`), a background enricher with cache, rate limits and circuit breaker, sanitation, the "Recomendaciones con IA" card, and the watch showing `freeText` in the alert pill.
  - **10b:** request shape for current OpenAI chat models (`max_completion_tokens`, no temperature by default, optional settings by environment variables), adaptive retry once when the provider rejects a parameter, and a distinct status when the model returns no text (ADR-80).
  - **10c:** `.env.example` and `scripts/run-companion.ps1` to load the settings for one run.
  - **Not verified:** any call to a real provider.

## Phase 9: Delivery kit

- **Goal:** A final documentation pass and what is needed to run the demo and a short manual check.
- **State:** Done: `README.md` rewritten with the requirements coverage matrix, this roadmap, [delivery/demo-script.md](delivery/demo-script.md), `scripts/demo-preflight.ps1` (run for real, read-only), [validation/smoke-test.md](validation/smoke-test.md), [validation/accessibility-report.md](validation/accessibility-report.md), a documentation audit (stale counts and statements corrected), and a full regression with the guide-strings checker extended to the new documents. No product code was changed; candidate defects found while reading are listed in the delivery report and not fixed.

## Deferred items

| Item | Why it is deferred | What it needs |
|---|---|---|
| **A physical watch over the LAN** | The watch allows cleartext only to `10.0.2.2`, `localhost` and `127.0.0.1` (ADR-46 and the network security configuration); the companion listens on all interfaces only with the switch "Permitir reloj físico en la red local". No device was available. | An address that can be set on the watch (or a build-time setting), a network security configuration that allows it, and a physical Wear OS watch for the test. |
| **Address editing on the watch** | Typing an address on a round screen was out of scope (ADR-45); the address is `ws://10.0.2.2:8765`, with a debug-only override file. | A design for entering or choosing an address (for example a list of discovered computers), then tests. |
| **Windows 10 verification** | Only a Windows 11 machine was available. The code uses documented Win32 calls that exist in Windows 10. | Run row 84 of the validation guide on a Windows 10 computer. |
| **Manual campaign results** | Nobody has run the guide yet. | Run the smoke test first, then the full guide; fill in the results sheet and the sign-off. |
| **A real AI provider** | No key was used by any automated check. | Run rows 103 and 108 of the guide with your own key. |
| **Real TalkBack and real vibration** | No physical watch. | See the accessibility report, section 8. |
| **PowerPoint full screen, several monitors, administrator windows, browser-based slides** | Not exercised with the real program. | Rows 33 to 41 of the guide (the first two PowerPoint cases), others untested. |

## Phase 11: Desktop companion redesign

- **Goal:** Replace the one-page window by the approved redesign (`docs/mockups/presenta-escritorio-v3.html`) without changing any behavior.
- **State:** Done and verified by automation (ADR-82): a rail with six pages (Inicio, Vincular, Ventana, IA y alertas, Actividad, Configuración), status dots, the checklist and live session of Inicio, the pairing digits and diagram, the grouped window list, the didactic alert timeline taken from the domain's thresholds, design tokens, and an offscreen-rendered UI test project. Cut: the real blur backdrop and the page transition. Not shown: the number of alerts raised (not in the state the window receives).
- **Not verified:** how the window looks on your screen, at 125% and 150%, and in Windows high contrast (rows 110 to 118).

## Phase 12: Release artifacts

- **Goal:** Downloadable, ready-to-run files so the course instructor does not need to build anything.
- **State:** Built and checked without running them (see `docs/delivery/release.md`): `releases/Presenta-Companion-win-x64.zip` (self-contained, 77.7 MB), `releases/Presenta-Wear.apk` (debug build, real repository), `SHA256SUMS.txt` and the Spanish `LEEME.md`. No product source changed.
- **Not verified:** starting the unzipped companion and installing the APK on the emulator (checklist in `release.md`); committing and publishing is yours.

## Phase 13: Icon and visual polish

- **Goal:** Give the companion its icon (executable, title bar, taskbar, rail logo), balance the Inicio layout, and show the window row as successful only after a verification in this run.
- **State:** Done and verified by automation (ADR-83); release package rebuilt with the icon and without the Release warning override; the watch app file is unchanged.
- **Not verified:** how the icon and layout look on your screen (guide rows 119 and 120).
