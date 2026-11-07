# Requirements traceability

Phases refer to [roadmap.md](roadmap.md). Decisions refer to [decisions.md](decisions.md).

## Functional requirements

| Code | Requirement | Phases | Related decisions |
|---|---|---|---|
| RF 01 | Connect the watch and the Windows app over LAN | 4, 5, 8 | ADR-02, 04, 06, 16 |
| RF 02 | Visible connection state (connected, reconnecting, disconnected) | 3, 5, 8 | ADR-17, UI decision 5 |
| RF 03 | Configure total slides and planned duration | 3, 5 | ADR-18 |
| RF 04 | Start once connected | 5 | ADR-14 |
| RF 05 | Next slide | 2, 4, 5 | ADR-07 |
| RF 06 | Previous slide | 2, 4, 5 | ADR-07 |
| RF 07 | Show slide counter, allow manual correction | 2, 3, 5 | ADR-08, UI decision 3 |
| RF 08 | Start, pause, finish the timer | 2, 5 | ADR-14, UI decisions 2 and 4 |
| RF 09 | Elapsed and remaining time | 2, 3, 5 | ADR-14 |
| RF 10 | Visual and, when possible, haptic alerts near the end or on slow pace | 2, 6 | ADR-21 |
| RF 11 | Expected pace and brief recommendation | 2, 6 | ADR-09, 13 |
| RF 12 | Finish manually and show a summary | 2, 5, 7 | ADR-15 |
| RF 13 | Windows stores planned/actual duration, configured/registered slides, average pace | 4, 7 | ADR-10, 15 |
| RF 14 | List finished presentations | 4, 7 | ADR-10 |
| RF 15 | On connection loss or command failure show a clear message without closing the session | 3, 8 | ADR-17 |

## Non-functional requirements

| Code | Requirement | Phases | Related decisions |
|---|---|---|---|
| RNF 01 | Next/previous on the main control screen without navigation | 3, 5 | ADR-21 |
| RNF 02 | Short text, contrast, round-screen components | 3, 9 | UI design rules |
| RNF 03 | Command reflected on the PC within about 2 s on a stable LAN | 5, 8 | ADR-02 |
| RNF 04 | Connection state updated within 5 s | 4, 8 | ADR-02 (heartbeat) |
| RNF 05 | Windows 10/11 | 4, 9 | ADR-07, 12 |
| RNF 06 | History persists across restarts | 4, 7 | ADR-10 |
| RNF 07 | LAN-only, no personal data or external credentials | 4, 8 | ADR-04, 05, 09, 16 |
| RNF 08 | Consistent design across normal, alert, pause, disconnected, finished states | 3, 9 | ADR-21 |

## Phase 2 status: what automated tests prove

Phase 2 delivered the protocol v1 contracts and the C# domain core (no networking, UI, keyboard, or storage). 327 tests pass: 126 in `Presenta.Domain.Tests` and 201 in `Presenta.Protocol.Tests`.

| Code | Proven in Phase 2 | Still open |
|---|---|---|
| RF 01 | Handshake messages (`hello`, `hello.accepted`, `hello.rejected`) are defined, schema-validated, and round-trip. | Server, pairing logic, and client (Phases 4, 5). |
| RF 02 | Not covered. | Connection state is a Phase 4 and 5 concern. |
| RF 03 | `session.start` carries slides and duration; the domain validates limits. | Setup screen (Phase 3). |
| RF 04 | Domain Start transition and `session.start` message. | Start only once connected (Phase 5). |
| RF 05, RF 06 | Next and Previous with clamping and the "counter changed" flag the companion uses to decide on a key; `slide.next` and `slide.previous` messages. | Real key sending (Phase 4). |
| RF 07 | Counter 1..total, manual `SetSlide` with range errors, `slide.set` message. | Slide selector UI (Phase 3). |
| RF 08 | Start, pause, resume, finish transitions and messages. | Gestures in the UI (Phase 3). |
| RF 09 | Elapsed excludes paused time; remaining is negative in overtime; snapshots carry server UTC time, elapsed, and running flag. | Smooth ticking on the watch (Phase 3). |
| RF 10 | All five alert rules (once-only, cooldown, acknowledgment, replacement, suppression); alerts never block slide commands. | Visual and haptic presentation (Phase 6). |
| RF 11 | Pace status, slides remaining, deterministic recommendations. | Display (Phase 6). |
| RF 12 | Finish variants and the summary, plus the `session.summary` message. | Summary screen and auto-save (Phases 3, 4, 7). |
| RF 13 | Summary and history item fields (planned and actual duration, planned and registered slides, average pace, final state). | SQLite storage (Phase 4). |
| RF 14 | `history.list` and `history.list.result` messages. | Query and display (Phases 4, 7). |
| RF 15 | `command.result` and `error` messages with typed codes; failed commands leave the session unchanged. | User-facing messages (Phases 3, 8). |
| RNF 01 to RNF 08 | None directly. RNF 07 is supported by the BCL-only libraries. | Later phases. |

## Phase 3 status: what automated tests prove

Phase 3 built the Wear OS UI on fake data (87 JVM tests pass: unit tests, Compose UI flow tests, accessibility and layout checks, screenshot rendering). Nothing here talks to a real companion.

| Code | Proven in Phase 3 (on fake data) | Still open |
|---|---|---|
| RF 01 | Not covered. | WebSocket client (Phase 5). |
| RF 02 | Home shows connected, reconnecting, and disconnected; Control shows the state in its status line; a flow test moves through them. | Real states from the connection (Phases 5, 8). |
| RF 03 | Setup with limits 1 to 200 slides and 1 to 180 minutes, defaults 20 and 15, tested. | Sending it to the companion (Phase 5). |
| RF 04 | Starting from Setup creates the session and opens Control; Home offers "Nueva presentación" only when connected. | Real start (Phase 5). |
| RF 05, RF 06 | Next and Previous on Control, tested through the full flow, including during an alert. | Real keys (Phases 4, 5). |
| RF 07 | Counter display, slide selector with − and +, tested. | Companion-side correction (Phase 5). |
| RF 08 | Pause and resume by long press, finish by swipe up with confirmation, tested. | Real timer (Phase 5). |
| RF 09 | Elapsed and remaining time, interpolated between snapshots, tested (`mm:ss`, `h:mm:ss`). | Real snapshots (Phase 5). |
| RF 10 | Alert state of Control for every alert kind including overtime; Next and Previous stay enabled, tested. | Haptics and real alerts (Phase 6). |
| RF 11 | Pace pill and Spanish text for every recommendation code. | Real recommendations (Phase 6). |
| RF 12 | Finish confirmation and summary screens: within time, over time, interrupted, read-only. | Auto-save on the companion (Phase 7). |
| RF 13 | Not covered (storage is on Windows). | Phases 4, 7. |
| RF 14 | History list, empty history, and read-only summary, tested on fake data. | Real query (Phase 7). |
| RF 15 | A failed command shows a clear Spanish message and the session continues; losing the connection keeps the controls, tested. One message per protocol error code. | Real failures and reconnection (Phase 8). |
| RNF 01 | Next and Previous are on the main Control screen, no navigation, tested. | Manual check (Phase 5). |
| RNF 02 | Short text, contrast at least 4.5:1 measured per pair, round-screen layout checks on 227 dp and 192 dp at font scales 1.0 and 1.3, 48 dp touch targets with listed exceptions. | Real-device check (Phase 9). |
| RNF 03, RNF 04 | Not covered. | Phases 5, 8. |
| RNF 05, RNF 06, RNF 07 | Not covered by this phase (the app has no network access or storage yet). | Phases 4 to 8. |
| RNF 08 | Normal, alert, paused, overtime, disconnected, reconnecting, and finished states share one design system and are all rendered and checked. | Review by the user, Phase 9. |

## Phase 4 status: what automated tests prove

Phase 4 built the Windows companion: server, pairing, keyboard interface, SQLite history, and the WPF window. 446 tests pass across the solution (server 65, persistence 15, view models 32, Windows keyboard 7, plus the 327 from Phase 2). The server tests use a real `ClientWebSocket` against Kestrel on an ephemeral loopback port, a fake keyboard, and a fake clock. No real watch, emulator, or physical key press was involved.

| Code | Proven in Phase 4 | Still open |
|---|---|---|
| RF 01 | A real WebSocket client connects, pairs with the code, and is replaced by a second valid client; loopback by default, all interfaces only with the switch (bound endpoints checked). | The watch client, the emulator path `10.0.2.2`, the physical watch, and the firewall (Phase 5). |
| RF 02 | The companion tracks the watch as connected or waiting and shows it; silent connections are dropped after the heartbeat timeout; a clean close is noticed at once. | The watch's own states (Phases 5, 8). |
| RF 03, RF 04 | `session.start` with slides, duration, and name; invalid values are rejected with a protocol error. | The watch setup flow (Phase 5). |
| RF 05, RF 06 | Next and Previous send exactly the Right or Left arrow request when the counter changed, and nothing at the limits (fake keyboard). The Windows key mapping and input structures are tested. | Real key injection into a presentation, by hand (Phase 5). |
| RF 07 | `slide.set` corrects the counter, never presses a key, and rejects out-of-range slides. | Watch selector against the real server (Phase 5). |
| RF 08, RF 09 | Start, pause, resume, finish over the socket; `state.update` carries server time, elapsed time, and the running flag; a fake-time test advances the clock. | Timing on a real watch (Phase 5). |
| RF 10 | Alerts raised by time passing (fake clock) are broadcast in `state.update` and can be acknowledged. | Haptics and the watch presentation (Phase 6). |
| RF 11 | The recommendation and pace status travel in the snapshot. | Display on the watch (Phase 6). |
| RF 12 | Finish and finish while paused send `session.summary`, then the "no session" state; the record is saved once. | The watch summary screen on the real connection (Phase 5). |
| RF 13 | Planned and actual duration, planned and registered slides, average pace, and final state are stored in SQLite and read back; the data survives closing and reopening the database. | The record count after real use (Phase 7). |
| RF 14 | `history.list` returns newest first with a limit; empty history is handled. | The watch history screen on the real connection (Phase 7). |
| RF 15 | Failed commands return `command.result` with the error code and leave the session unchanged; invalid JSON, unknown types, oversized and binary frames never stop the server; a keyboard or database failure does not end the session; the session survives a disconnect and is interrupted and saved after the grace period. | User-facing messages on the watch (Phases 3, 8) and behavior on a real network. |
| RNF 01 | Not covered here (watch UI). | Phase 5. |
| RNF 03 | Not measured. Commands are handled in-process without waiting; the key is pressed before the result is sent. | Latency on the real network (Phases 5, 8). |
| RNF 04 | The server closes a connection silent for 15 seconds (configurable) and notices a clean close at once. The 5-second target is for the watch, which should detect a lost server itself with its own ping/pong. | Measurement in Phases 5 and 8. |
| RNF 05 | Builds and runs on this Windows 11 machine. | Windows 10 not tested. |
| RNF 06 | History persists across closing and reopening the database, including through a server restart in a test. | Restart of the real app (Phase 7). |
| RNF 07 | Loopback only by default, LAN only by an explicit switch, pairing code with lockout, no credentials, no personal data, no outbound network access, pairing code never logged (tested). | Firewall behavior (Phase 5). |
| RNF 08 | The window shows normal, paused, overtime, and waiting states with one palette. | Visual review by the user. |

## Phase 5 status: what automated tests prove, and what awaits your manual run

Phase 5 added the watch's real repository (WebSocket client, protocol v1, DataStore, pairing screen) and the opt-in cross-stack test. The watch suite has 203 tests (JVM, Robolectric, a scripted companion on virtual time, MockWebServer) and the C# side still has 446. `:app:crossStackTest` (4 tests) runs the real Kotlin client against the real C# server through `Presenta.DevHost`, which prints keys instead of pressing them. **No emulator, device, or real key press was involved in any automated check.**

| Code | Proven by tests | Awaits your manual run (`docs/manual-test-guide.md`) |
|---|---|---|
| RF 01 | The Kotlin client pairs with the real server over a real socket (loopback) and reconnects after a listener restart. | The emulator path `10.0.2.2` and the firewall. A physical watch on a LAN address is not supported yet (ADR-46). |
| RF 02 | Connected, reconnecting, and disconnected states follow the connection; the pairing screen appears on rejection. | How the states look and feel on the emulator. |
| RF 03, RF 04 | Setup sends `session.start`; the session appears from the server's state. | Starting from the real watch UI. |
| RF 05, RF 06 | Cross-stack: Next presses Right, Previous presses Left, once each, and nothing at the limits (DevHost key log). | Real arrow keys reaching a real slideshow window. |
| RF 07 | `slide.set` corrects the counter and presses no key (cross-stack); the selector sends it. | Using the selector on the watch. |
| RF 08, RF 09 | Pause, resume, finish, and elapsed and remaining time with local interpolation re-synchronized by each snapshot (virtual time). | How the clock looks over a real session. |
| RF 10 | Alerts in `state.update` become the alert state of Control with Next and Previous enabled. | Seeing a real alert (needs a running session near its end; haptics are Phase 6). |
| RF 11 | Recommendations and pace status are mapped from the snapshot. | Reading them in use. |
| RF 12 | The `session.summary` arrives and opens the summary; the cross-stack finish checks its contents. | The summary screen with real data. |
| RF 13, RF 14 | The history list from the real server returns the saved record; the empty list is handled. | The history screen with real data and after restarting the companion. |
| RF 15 | Commands fail at once when not connected and are never queued; failed results map to Spanish errors; the session stays on screen and keeps ticking while reconnecting; a command waiting during a drop fails instead of hanging. | Stopping the companion mid-session and reconnecting. |
| RNF 01 | Next and Previous stay on Control, also during alerts and while reconnecting. | Feel on the watch. |
| RNF 02 | Pairing screen checked at 227 dp and 192 dp, at font scales 1.0 and 1.3, for 48 dp targets, descriptions, and text cut off. | A real watch. |
| RNF 03 | **Not measured.** | Latency by eye, target about 2 seconds. |
| RNF 04 | Silent-server detection takes at most 3.5 s with the production constants (virtual time, also at the worst alignment); closed sockets are noticed at once. | A stopwatch test on the real network, target 5 seconds. |
| RNF 06 | The companion keeps history across restarts (Phase 4); the watch lists it. | Restart the companion and check. |
| RNF 07 | Cleartext only for `10.0.2.2`, `localhost`, `127.0.0.1`; `INTERNET` declared; the pairing code is stored on the watch but never logged. | Behavior with a physical watch (Phase 8). |

## Phase 5.1 status: slide control targeting

The first manual run showed the watch counter moving while PowerPoint did not: keys went to the foreground window (Android Studio). Phase 5.1 aims keys at a chosen presentation window. Counts: companion 552 tests (plus 5 opt-in desktop tests, skipped by default), watch 204.

| Code | Proven by tests | Awaits your manual run |
|---|---|---|
| RF 05, RF 06 | With another window in front, Next and Previous reach only the chosen window: activation, confirmation, then exactly one Right or Left key (faked window system, 56 targeting tests; server tests over a real WebSocket); on this machine the opt-in desktop test did the same with real windows in separate processes (5 of 5). No key is sent when the window cannot be confirmed in front, is missing, ambiguous, or elevated. The counter moves only after delivery. | A real slideshow in PowerPoint (windowed and full screen), steps 30 to 42 of `manual-test-guide.md`. |
| RF 15 | Two new error codes (`targetWindowUnavailable`, `keyDeliveryFailed`) with Spanish messages on the watch; a failed key leaves the counter unchanged and the session running; `session.start` is refused with a clear message when keys are on and no window is usable. | How the messages read on the watch. |
| RNF 03 | Activation is skipped when the target is already in front; retries are bounded at under a second. **Latency is not measured.** | The stopwatch measurement, step 42. |
| RNF 05 | Builds and runs on Windows 11. The window code uses only documented Win32 calls that exist in Windows 10. | Windows 10 not tested. |

Limits that tests cannot remove: Windows foreground rules may still refuse an activation (then nothing is sent and the error is shown); an elevated target is blocked unless the companion is elevated too; PowerPoint exclusive full-screen modes of other programs, several monitors, and browsers for Google Slides or Canva (identified by title only) were not exercised.
