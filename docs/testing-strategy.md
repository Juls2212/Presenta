# Testing strategy

The user cannot run the app manually until Phase 5 (ADR-20). Phases 1–4 are verified by automation only.

## Verification ladder, Phases 1–4

| Phase | Verification |
|---|---|
| 1 | File and tree checks, forbidden-term search, requirement-code search, Mermaid re-read. |
| 2 | `dotnet build` and `dotnet test`: unit tests for timer, counter, pace, alert rules, and recommendations using a fake clock; contract tests validating sample messages against the JSON Schemas. |
| 3 | Gradle build; JVM Compose UI tests; screenshot tests on Large Round (about 227 dp) and Small Round (about 192 dp); previews reviewed by the user. |
| 4 | `dotnet build` and `dotnet test`: in-memory integration tests using a real WebSocket client against an in-process server, a fake keyboard, and fake time; SQLite history tests. |

## Phase 2 test suites

Run everything with `dotnet test windows-companion/Presenta.Companion.sln`. At the end of Phase 2: 327 tests pass (126 + 201); later totals are in the phase sections below.

| Project | Tests | What it covers |
|---|---|---|
| `Presenta.Domain.Tests` | 126 | Lifecycle transitions and typed failures, configuration limits, slide clamping and the counter-changed flag, timer excluding paused time, negative remaining, warm-up and pace boundaries, average pace, every alert kind and the cooldown, acknowledgment, replacement and suppression rules, recommendation codes, summaries, `MarkInterrupted`, finish while paused. |
| `Presenta.Protocol.Tests` | 201 | Round trip of all 19 message types, every valid example against its schema and parser, every invalid example rejected by the schema and (unless named `schema-only`) by the parser, strict parsing cases, tolerance of unknown properties, unknown message types, and agreement between C# enums, schemas, `protocol-v1.md`, and domain limits. |

Rules for these suites:

- Time comes only from a controllable fake `TimeProvider`; there are no sleeps and no real timers.
- Examples live in `shared-contracts/examples/`; file names use `<message type>--<variant>.json`. Invalid examples that only a schema can reject include `schema-only` in the name.
- The schemas are executed with a test-only JSON Schema validator (ADR-22).

## Phase 3 test suites (Wear OS)

Run from `wear-app/`: `gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug`. Current result: 87 tests pass and lint reports 0 errors. No emulator or device is involved; UI tests run on the JVM with Robolectric (ADR-27).

| Suite | Tests | What it covers |
|---|---|---|
| `FormattersTest` | 10 | `mm:ss` and `h:mm:ss`, pace with a dot under any locale, Hoy, Ayer, `08 Oct` with a fixed clock and time zone, 24-hour times. |
| `StringMapperTest` | 15 | Every connection state, recommendation code, alert kind, and error code maps to non-empty Spanish text; plural forms; day labels and months; the default session name. |
| `FakePresentaRepositoryTest` | 21 | Start, next, previous, set slide, pause, resume, finish (manual, paused, interrupted), summaries, history, scripted pace and alerts, alerts never blocking slide commands, command failures, connection loss. |
| `ControlStateFactoryTest` | 8 | Interpolation between snapshots, whole-second rounding, paused and overtime states, pass-through of alert and pace. |
| `ViewModelTests` | 17 | Setup limits and defaults, control commands, selector, finish confirmation, error messages, history loading, app-level summary handling. |
| `ContrastTest` | 4 | WCAG contrast of every color pair, at least 4.5:1. |
| `PresentaFlowTest` | 9 | Home to Setup to Control, next and previous, slide selector, pause by long press, alert with Next and Previous enabled, finish by swipe up, summary, history with data and empty, read-only summary, disconnected and reconnecting, failed command, interruption, over-time summary, overtime alert. |
| `LayoutAccessibilityTest` | 1 | All 22 states on 227 dp and 192 dp at font scales 1.0 and 1.3: text inside the circle, no overlapping texts, every interactive element labelled in Spanish, 48 dp touch targets except the listed ones (ADR-31). |
| `ScreenshotTest` | 2 | Renders all states on both sizes; with `-Proborazzi.test.record=true` it writes `docs/ui-previews/`. |

What is not automated: how the screens feel on a real watch, rotary input, the real fonts, and anything that needs the companion. Those start in Phase 5.

## Phase 4 test suites (Windows companion)

Run from `windows-companion/`: `dotnet build Presenta.Companion.sln -warnaserror` and `dotnet test Presenta.Companion.sln`. Current result: all pass.

| Project | Tests | What it covers |
|---|---|---|
| `Presenta.Domain.Tests` | 126 | Phase 2, unchanged. |
| `Presenta.Protocol.Tests` | 201 | Phase 2, unchanged. |
| `Presenta.Server.Tests` | 65 | A real `ClientWebSocket` against Kestrel on an ephemeral port. Pairing (success, wrong code, lockout and recovery on fake time, unsupported version, command before hello, code never logged, regenerate); robustness (invalid JSON, unknown type, number as string, oversized and binary frames, ping and pong, heartbeat timeout, periodic refresh); the full flow with alert broadcast on fake time; key rules with a recording keyboard; name fallback; history saved once and listed newest first, including a real SQLite file across a restart; reconnect, grace-period interruption, replacement of the first client; concurrency (100 commands while 300 ticks fire); loopback and all-interfaces binding and rebinding. |
| `Presenta.Persistence.Tests` | 15 | Round trip of every field and final state, newest first, limit, reopening, file not held open, concurrent saves. |
| `Presenta.Keyboard.Windows.Tests` | 7 | Which key codes and input structures the Windows keyboard builds (Right `0x27`, Left `0x25`, nothing else). No key is ever pressed. |
| `Presenta.Companion.ViewModels.Tests` | 32 | Window state and text mapping: server status, busy port, pairing code, switches, addresses, watch status, session area including overtime and pause, activity list limit and order, dispatcher use, resource completeness, launch arguments. |

How the tests stay deterministic: server time comes only from `ManualTimeProvider` (timers fire as the test advances time); socket tests wait for messages, and the few state checks poll for up to 8 seconds of real time. The server tests were repeated five times with the same result.

Also checked by hand once: the built `Presenta.exe` was started with a temporary port and database, answered a wrong pairing code with `hello.rejected`, created its database, and exited with code 0 after its window was closed, releasing the port. This is not part of `dotnet test`.

Key rules to cover automatically (Phases 2 and 4):

- Keys are sent only when the counter actually changed; `slide.set` never sends keys.
- Wrong pairing codes are rejected, with lockout after repeated failures.
- Disconnection keeps the session running; the grace period ends it as `interruptedByDisconnection`.
- A new valid connection replaces the old one.
- Every session end triggers auto-save and `session.summary`.

## Manual tests

The manual test steps are in [validation/validation-guide.md](validation/validation-guide.md) (the single list; setup and troubleshooting are in [manual-test-guide.md](manual-test-guide.md)). The earlier outline that stood here was replaced by that guide so that there are not two lists that can disagree.

A physical watch on the LAN is not supported yet (Phase 8), so no manual test uses one.

## Phase 5 test suites (watch client and cross-stack)

Run from `wear-app/`: `gradlew.bat :app:assembleDebug :app:testDebugUnitTest :app:lintDebug`. Nothing uses an emulator or a device.

| Suite | Tests | What it covers |
|---|---|---|
| `WireContractTest` | 24 | Every file in `shared-contracts/examples/` parses into the Kotlin models and serializes back to equivalent JSON; every invalid example the C# parser rejects is rejected here (the `schema-only` ones are skipped); all 19 message types have schemas and examples; error codes and enums equal the schemas and `protocol-v1.md`; strict typing, tolerance of unknown properties, unknown types, envelope rules. |
| `RemotePresentaRepositoryTest` | 41 | A scripted companion on virtual time: hello accepted and rejected (wrong code, lockout, version), state updates, command results and errors, timeouts, no queuing while disconnected, summaries, history, pings, loss detection inside 5 s (also at the worst alignment), reconnect with a new hello, capped backoff, giving up and retrying, bad input, timer interpolation and re-synchronization, pairing flows. |
| `OkHttpIntegrationTest` | 7 | The real OkHttp transport against a scripted WebSocket server on the JVM (real time, short timings): pairing and a command, rejection, reconnect with hello, silence detection, invalid frames, unreachable and malformed addresses. |
| `PairingStoreTest` | 7 | DataStore pairing settings (in-memory DataStore, plus one real file with a single write). |
| `NetworkConfigTest` | 5 | Cleartext only for `10.0.2.2`, `localhost`, `127.0.0.1`; `INTERNET` declared; no wildcard; no global cleartext; the address override exists only in debug. |
| `PairingViewModelTest` | 9 | Digit entry, wrapping, prefill, connect on the last digit, outcome mapping. |
| `PairingUiTest` | 14 | The pairing screen on the JVM: digit picker, crown input, focus order, 48 dp targets and round-screen checks, messages, connecting state. |
| `PairingFlowTest` | 6 | The whole app on the fake repository: pairing routing, rejection and lockout messages, the ring showing elapsed time, reconnecting while a session is open. |
| `ControlStateFactoryTest` | 11 | Interpolation and the elapsed-time ring (clamped, safe without a plan). |
| `LayoutAccessibilityTest` | 1 | Now covers 26 states, including the four pairing states, and also fails on any text that is cut off (clipped or ellipsized). |
| Earlier Phase 3 suites | unchanged | Formatters, string mapper, fake repository, view models, contrast, flow, screenshots. |

The cross-stack test is opt-in because it needs the .NET SDK: `gradlew.bat :app:crossStackTest`. It builds `Presenta.DevHost`, starts it, and drives the real Kotlin client through pairing, start, next, previous, set slide, pause, resume, a listener restart with reconnect, finish, summary, and history, then checks the key log shows only Right and Left and only when expected. It also checks rejection, lockout, and re-pairing against the real server. It is excluded from `testDebugUnitTest`.

## Phase 5.1 test suites (slide control targeting)

Run from `windows-companion/`: `dotnet build Presenta.Companion.sln -warnaserror` and `dotnet test Presenta.Companion.sln`.

| Suite | Tests | What it covers |
|---|---|---|
| `Presenta.Targeting.Tests` | 56 | Candidate filtering (own process, invisible, cloaked, tool windows, empty titles); suggested PowerPoint slide show first; identity resolution (live, dead handle re-resolved, none, ambiguous, handle breaks a tie, modified-title marker, minimized, elevated); activation then confirmation then key; skip when already in front; minimized restore; abort with no key when activation is refused or not confirmed or the foreground is lost before the key; no key for missing, ambiguous, elevated, or unselected targets; focus not restored; bounded attempts; only Right and Left under every path; settings round trip, corrupt and incomplete files, invalid UTF-8, unwritable location, file contents. |
| `Presenta.Server.Tests` (`KeyTargetingTests`) | 21 | Over a real WebSocket: start refused with the new code when keys are on and the target is unusable, allowed when it is ready or minimized, allowed with keys off; delivery failure keeps the counter, returns the right code, and the session continues; no key attempt when the counter would not change; `slide.set` never sends keys; an exception from the keyboard is a delivery failure. |
| `Presenta.Companion.ViewModels.Tests` (`TargetSelectionViewModelTests`) | 20 | The new section: list and suggested entry, selection saves, refresh keeps the choice, status mapping for closed, minimized, elevated, duplicate; verify brings the window forward and presses no key; refused and unconfirmed verification messages; every text comes from the `.resx`. |
| `Presenta.Domain.Tests` | +3 | `WouldMove` predicts exactly what Next and Previous do. |
| `Presenta.Protocol.Tests` | +6 | The two new codes: schema, enum, `protocol-v1.md`, wire names, two new shared examples. |
| `Presenta.Desktop.Tests` | 5 (opt-in) | Real windows in separate processes (`Presenta.WindowProbe`): a bare key lands in the foreground window (the original defect); with a "thief" window in front the key reaches only the chosen window; Left for Previous; nothing is pressed anywhere when the chosen window was closed; a minimized window is restored and receives the key. Skipped unless `PRESENTA_DESKTOP_TESTS=1`; it opens windows on the desktop and presses real arrow keys, so do not run it while typing in another program. |

Watch: `WireContractTest` gained one test (the new codes parse from the shared examples and map to the UI model) and `StringMapperTest` checks the two new Spanish messages.

Run the desktop tests in PowerShell with `$env:PRESENTA_DESKTOP_TESTS = "1"; dotnet test windows-companion/tests/Presenta.Desktop.Tests`.

## Phase 6 test suites (alerts, haptics, screen on, shutdown, dates)

Companion: 574 tests (domain 138, protocol 211, persistence 15, server 87, Windows keyboard 7, view models 60, targeting 56) plus 5 opt-in desktop tests. Watch: 234 tests. Opt-in cross-stack: 6 tests (4 earlier, 2 new).

| Suite | Tests | What it covers |
|---|---|---|
| `Presenta.Domain.Tests` (`AlertTimingTableTests`) | +9 | Second-by-second simulation with a fake clock and the default rules: the exact second of every alert for 1 min / 5 slides (16, 42, 61 s), 2 min / 10 slides (14, 84, 121 s) and 15 min / 20 slides (95, 630, 840, 901 s) when standing still; the rushing case (10, 13 and 60 s jumps to the last slide); the 120 s cooldown repeat with new sequence numbers (95, 215, 335 s); no repeat without acknowledgment; sequences start at 1 in every session. These numbers are the ones in `validation/validation-guide.md` rows 43 to 56. |
| `Presenta.Server.Tests` | +1 | Over a real WebSocket the alert keeps its `sequence` across snapshots and the next alert gets the next number. |
| `Presenta.Protocol.Tests` | +4 | The `sequence` field: the shared example carries it; two new invalid examples (missing, and zero as a schema-only case). |
| `Presenta.Companion.ViewModels.Tests` | +8 | Closing during a running or paused session asks first and saves exactly one interrupted record and stops the server; declining changes nothing; closing idle or after the session ended does not ask and saves nothing; interrupting twice saves one record; the watch receives the interrupted summary; the texts come from the `.resx`. |
| `HapticsCoordinatorTest` (watch) | 15 | Patterns are distinct and short and follow the severity; one pattern per new alert; none for the same alert in later snapshots, for an acknowledgment, for a late snapshot of an acknowledged alert, or after a reconnection; a repeated pace alert (new sequence) plays again; a more severe replacement plays its own pattern; baseline for an alert already active at start; stale alerts skipped; a new session counts from 1; a failed command plays the single short pulse; one log line per event. |
| `AlertStatesTest` (watch) | 3 | All five alert kinds are a state of Control with Next and Previous enabled and a working "Entendido"; red only for overtime; with the whole app on the fake repository the pace pill follows the snapshot, the acknowledged alert does not come back after later snapshots, and the recording haptics player records exactly one pattern. |
| `KeepScreenOnTest` (watch) | 2 | The flag follows the enabled value and is cleared when the composable leaves; in the whole app it is set from the start of a session, kept while paused, and cleared at the summary and at Home. |
| `HistoryDatesTest` (watch) | 8 | "Hoy", "Ayer", "08 Oct"; twelve capitalized months with two-digit days; UTC timestamps converted to the local day; midnight boundaries; the same record moving from Hoy to Ayer to a date; year change; another time zone; dot decimal for pace under a Spanish locale. |
| `HistoryFlowTest` (watch) | 2 | Rows newest first with the right dates and times; an interrupted record opens a read-only summary that says so. (The full finish, summary, history, read-only, empty flow is in `PresentaFlowTest`.) |
| `AlertsCrossStackTest` (opt-in) | 2 | The real client and the real server running 100 times faster: a standing-still 15-minute session shows behind, running out, critical, and overtime once each in order with sequences 1 to 4 and the recorded patterns warning, warning, critical, critical; a rushing session shows too fast, an acknowledgment vibrates nothing, and the repeat after the cooldown has sequence 2 and vibrates once. |

`NetworkConfigTest` now expects `INTERNET` and `VIBRATE`. What is not tested: the real vibrator (`AndroidHapticsPlayer`), the real message box, and the feel of any pattern; these are in the manual guide and need a physical watch for the last one.

## Phase 7a test suites (validation kit)

Companion: 576 tests in Debug (575 in Release; the 574 of Phase 6 plus one in `Presenta.Server.Tests` and, in Debug only, one for the debug timing file) and 5 opt-in desktop tests. Watch: 238 tests (234 plus 4). Opt-in cross-stack: 8 tests (6 plus 2). Run everything with `scripts/verify-all.ps1 [-CrossStack] [-Desktop]`; it writes `docs/validation/automated-run-latest.md`. `scripts/check-guide-strings.ps1` checks that every Spanish text quoted in the guides exists in `strings.xml` or `UiStrings.resx`.

| Suite | Tests | What it covers |
|---|---|---|
| `KeyTargetingTests` | +1 (+1 in Debug) | Each key delivery writes one `PresentaTiming` line in Debug builds and none in Release builds; the debug timing file keeps only timing lines. |
| `TimingDiagnosticsTest` (watch) | 4 | A command logs its round trip in milliseconds on virtual time; a silent server logs the loss after the last frame (3.0 to 3.5 s) with the production constants; the silent log records nothing; release builds get the silent log. |
| `TimingCrossStackTest` (opt-in) | 2 | 100 slide commands against the real server over loopback (median, 95th percentile, bounds 250 ms and 1000 ms, printed as `TIMING` lines); the client declares the loss within 5 s after the host is killed and keeps the session on screen. |

Measured on the development computer (loopback, host prints keys): median 0.5 ms, 95th percentile 0.8 ms, loss declared 6 to 7 ms after the host was killed (`sinceLastFrameMs` 4 to 9 ms). These do not include the real key injection, the slideshow repaint, or a real network; the manual rows `27`, `28` and `42` of the validation guide measure those.

## Phase 7b-1 test suites (real PowerPoint key delivery)

Companion: 617 tests in Debug (576 before; targeting 77, server 96, view models 73) and 8 opt-in desktop tests (5 before). Watch (238) and cross-stack (8) unchanged.

| Suite | Tests | What it covers |
|---|---|---|
| `Presenta.Targeting.Tests` (`ForegroundRuleTests`) | +15 | The foreground rule: owned window accepted (also through two levels), owner of the chosen window refused, unrelated same-program window refused, another program refused even with an owner link, owner chains that leave the program, a thief after the confirmation stops the key, an owned window after the confirmation still gets it; the saved identity keeps the class and never falls back to another class. |
| `Presenta.Targeting.Tests` (`DiagnosticReportTests`) | +6 | The report content (system, elevation, target, owner chain, windows of the same program, foreground before and after, each step with its Win32 result, decision), refused and unconfirmed activations, no target, no key sent, no pairing code. |
| `Presenta.Server.Tests` | +7 | Each failing delivery result reaches the activity with its typed reason. |
| `Presenta.Companion.ViewModels.Tests` | +13 | "Copiar diagnóstico" copies the report, confirms, sends no key, and reports clipboard failures; slide-show entries and other PowerPoint windows are labelled; every failure reason has a distinct Spanish text; the activity line shows the reason. |
| `Presenta.Desktop.Tests` | +3 (opt-in) | A program that hands the focus to its owned window (failed before the rule, passes after); a top-most helper window; a full-screen helper window. All 8 passed locally. Skipped unless `PRESENTA_DESKTOP_TESTS=1`. |

Not reproduced: an unusual foreground program or missing recent input making `AttachThreadInput` or `SetForegroundWindow` fail (hypothesis B). Not run: the real PowerPoint.

## Phase 8 test suites (robustness and usability)

Companion: 712 tests in Debug (617 plus 95) and 11 opt-in desktop tests (8 plus 3). Watch: 250 tests (238 plus 12). Opt-in cross-stack: 11 tests (8 plus 3). Run everything with `scripts/verify-all.ps1 -CrossStack [-Desktop]`.

| Suite | Tests | What it covers |
|---|---|---|
| `Presenta.Server.Tests` (`ActiveSessionTests`) | +34 | The active session is written on start and on each command, at least every 5 seconds, paused or running, and removed when the session ends by finishing, the grace period or closing the window; a store or history that fails never disturbs the session; recovery gives exactly one interrupted record with the last written time, never resumes, survives a crash during recovery (file left behind, then found already in the history), a failing history and a corrupt file, and never replaces a live session; the real file store round-trips, replaces atomically, leaves no temporary file and rejects 15 kinds of corrupt, partial or out-of-range content; recovery with the real file and the real SQLite database. |
| `Presenta.Server.Tests` (`RememberedPairingCodeTests`) | +30 | Off by default; on stores the code and a restart keeps it; regenerating replaces it only when remembered; off removes it; unusable stored codes are replaced; the fixed test code wins; the remembered code pairs a real WebSocket after a restart and the old code is refused after regenerating; the lockout still applies; the code is never logged; a settings store that fails only logs; the file store round-trips, removes the code when off, and falls back on corrupt content. |
| `Presenta.Companion.ViewModels.Tests` (`AutomaticTargetTests`) | +21 | Automatic choice (one slide show), no choice (several, none), a replaced unresolvable choice, a resolvable choice never replaced, the hand choice ending the note, choosing and the timer never verify or press keys, refresh keeps the choice, a later-started presentation is picked up by the timer without rebuilding an unchanged list, and the warning appears only for the PowerPoint window that is not the slide show. |
| `Presenta.Companion.ViewModels.Tests` (`Phase8WindowTests`) | +10 | The recovery activity line (once), the remember switch (default off, stores, regenerates, clears, initial state), the new Spanish captions, the single-instance gate with a fake lock (message and nothing started), the real named lock (one owner per name, freed on dispose, different names independent), the launch arguments, and the real files across two runs. |
| `SessionReconnectionTest` (watch) | 12 | On virtual time: a 10-minute outage during a running session never reaches the give-up state; the capped backoff continues past two minutes; a paused session also keeps retrying; the screen keeps showing the session and its clock as reconnecting; commands fail at once and are never queued or replayed; the stored code is re-sent and the state resynchronized after minutes; a restarted companion without the session clears it; a rejected code returns to pairing without losing the displayed session and a new code restores it; idle behavior is unchanged (give up after 30 s, "Reintentar" reconnects, also after the session ended and for a never-connected watch). |
| `RememberedCodeCrossStackTest` (opt-in) | 3 | The real watch client against the real server process: a remembered code keeps the watch paired across a companion restart and regenerating replaces it (the code is never printed); without remembering the old code is refused; a companion killed in the middle of a session is recovered exactly once, and a further restart adds nothing. |
| `SingleInstanceProcessTests` (opt-in desktop) | 3 | The real named event across processes: taken while another process holds it, free after it exits or is killed, and a helper finds it taken by this process. All 3 passed locally. |

Failing first: with the give-up guard removed, 8 of the 12 `SessionReconnectionTest` tests fail; with it restored all pass.

One existing test (`Closing_after_the_session_already_ended_does_not_ask_and_does_not_duplicate_the_record`) failed once in about 20 full runs under load because it read the status before the service had published it; it now waits for that. A single failure of one test in `Presenta.Server.Tests` was also seen once and not reproduced in 25 further runs; its name was not captured.

## Phase 10 test suites (optional AI)

Companion: 906 tests in Debug (712 plus 194) and 11 opt-in desktop tests. Watch: 269 tests (250 plus 19). Opt-in cross-stack: 13 tests (11 plus 2). No test touches the network or needs a key: the provider is a fake client or an in-process server on loopback.

| Suite | Tests | What it covers |
|---|---|---|
| `Presenta.Ai.Tests` (new project) `SanitizerTests`, `PromptTests` | +45 | Accepted and rejected texts (quotes, markdown, bullets, line breaks, links, control characters, emojis, invisible characters, empty, too long, cut at a word, too few words after the cut, never over the limit), the pinned system prompt and user line, the coarse buckets and shared keys, the exact request body, the canned client. |
| `ClientTests`, `ConfigurationTests` | +60 | Against an in-process loopback server: success and the request shape (path, Bearer header, content type, golden body), a trailing slash, the key only in the header, no name or title or file name in the body, every status (401, 403, 429 with and without retry-after and with a date, 5xx, 4xx, a redirect not followed), malformed answers (11 kinds), too large and not UTF-8, a slow answer beyond the timeout, a closed port, the caller's cancellation; https enforcement and invalid addresses; the three variables. |
| `EnricherTests` | +30 | Off by default; not available without valid settings; no call without an alert; the text is requested in the background and then filled; cache by situation and coarse numbers; a stale text is dropped and comes back for the same situation at no cost; rate limit; per-session cap and its reset; circuit breaker (five failure kinds), trial, reopening at the first failed trial, reset by a success; refused key and its recovery; 429 backoff doubling up to the cap and with retry-after; timeout on fake time; unusable texts count as failures; a throwing client; `Apply` returns at once. |
| `SecretsTests` | +13 | The key never in a log (6 statuses, hostile echo, malformed, slow, unreachable), never printed by options or configuration, never a public member, an activity line cannot hold text, the window diagnostic report never has it even when it is in the environment. |
| `Presenta.Server.Tests` (`AiRecommendationTests`) | +9 | Through the real server and a real WebSocket: the text reaches the watch in a new state, also on a late connection; off means no call; a provider that never answers blocks neither commands nor ticks and the text arrives when it does; six failure kinds leave the deterministic recommendation; an unusable text never arrives; acknowledging the alert removes it; the counter restarts with a presentation; a service without an enricher behaves as before. |
| `Presenta.Companion.ViewModels.Tests` (`AiSectionViewModelTests`) | +11 | The switch (off by default, disabled with the reason when unavailable, on and off), the status mapping for every state, the pause ending with the timer, the dispatcher, strings from the resource file, the privacy note. |
| `Presenta.Protocol.Tests` (`FreeTextTests` and the new examples) | +16 | The new valid example and two invalid ones; text and null round trip; the schema limit of 80; types. |
| `AiTipLayoutTest` (watch) | 6 | The tip replaces the wording in the alert pill (4 setups), the same accessibility description, the fallback for every code, a 41-character tip without a layout defect and an 80-character tip inside the circle at Large Round and Small Round, font scales 1.0 and 1.3, and no change outside an alert. |
| `RecommendationTextTest`, `FreeTextWireTest`, `RecommendationTipStateTest` (watch) | 13 | The watch's own guard, parsing with and without the text against the shared examples, and the Control state preferring a usable text. |
| `AiTextCrossStackTest` (opt-in) | 2 | The real client against the real server with `--fake-ai`: the text appears with an alert and the Control state prefers it; without the flag no text ever arrives. |

The layout test found a real problem on the way: with a two-line pill Next and Previous lost up to 7 dp of touch size, so the pill with a text uses a smaller font and tighter spacing (ADR-78).

**Not tested:** any real provider. The shape of the request follows the common chat-completions convention and is proven against a local stand-in only.
