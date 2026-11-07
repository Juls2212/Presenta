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

Run everything with `dotnet test windows-companion/Presenta.Companion.sln`. Current result: 327 tests pass (126 + 201).

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

## Manual test outline, Phase 5 and later

1. Start the companion, note the pairing code.
2. Start the Wear OS emulator (Large Round), open the app, connect via `10.0.2.2`; if it fails, use `adb reverse tcp:8765 tcp:8765` and `ws://localhost:8765`.
3. Pair with the code; try a wrong code first.
4. Configure slides and minutes, then start.
5. Open a real presentation on the PC; press next and previous and confirm the slide changes within about 2 s.
6. Correct the counter manually and confirm no key is sent.
7. Pause and resume with long-press; check the timer.
8. Finish through swipe-up and confirm; check the summary and the saved record.
9. Phase 6+: provoke slow pace and low remaining time to see alerts and haptics.
10. Phase 7+: restart the companion and check the history.
11. Phase 8+: close the companion mid-session, reconnect, and check state restoration and the grace period.
12. Physical watch (optional): enable LAN listening, add the firewall rule, connect via the PC LAN IP.

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
