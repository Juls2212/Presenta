# Architecture Decision Records

All decisions below are accepted. Each record has context, decision, and consequences.

## ADR-01: Modular monolith

- **Context:** An academic project with one developer, two runtimes (Wear OS and Windows), and a need for simple builds and debugging.
- **Decision:** One repository, clear module boundaries (`wear-app`, `windows-companion`, `shared-contracts`, `docs`). No microservices.
- **Consequences:** Simple setup and deployment. Boundaries rely on discipline and on the protocol, not on network separation.

## ADR-02: WebSocket over the local network

- **Context:** The watch must send commands and receive live state with low latency (RNF 03) and detect connection loss quickly (RNF 04).
- **Decision:** Use WebSocket over LAN, on a configurable port (default 8765). Bluetooth/Data Layer is excluded because it needs a phone; automatic discovery is a future improvement.
- **Consequences:** Bidirectional push with one connection. The user must know the PC address (or use the emulator defaults). Requires cleartext `ws://` on the watch (see ADR-05).

## ADR-03: Kestrel hosted inside the WPF app

- **Context:** The companion needs a server and a desktop UI, and the user should run a single program.
- **Decision:** Host Kestrel inside the WPF process.
- **Consequences:** One executable and shared in-memory state. Server lifetime follows the app. The server must not block the UI thread, and the UI must not own business logic.

## ADR-04: Pairing code

- **Context:** Any device on the LAN could otherwise send commands that press keys on the PC.
- **Decision:** The companion shows a random 6-digit code. The watch sends it in the first message. Wrong codes are rejected, with lockout after repeated failures.
- **Consequences:** Basic protection without accounts. The code is not strong cryptography; the threat model is a trusted local network (RNF 07).

## ADR-05: `network_security_config` for cleartext

- **Context:** Android blocks cleartext traffic by default, and the protocol uses `ws://`.
- **Decision:** Add a `network_security_config` that allows cleartext only for `10.0.2.2`, localhost, and local-network addresses.
- **Consequences:** Cleartext stays blocked for the public internet. Local-network ranges must be maintained in the config. Validated in Phases 5 and 8.

## ADR-06: `10.0.2.2` default with `adb reverse` fallback

- **Context:** The emulator reaches the host through a special alias, but this may vary with setup.
- **Decision:** Default host `10.0.2.2` (configurable, validated in Phase 5). Fallback: `adb reverse tcp:8765 tcp:8765` and `ws://localhost:8765`. A physical watch uses the PC LAN IP.
- **Consequences:** Works out of the box in the emulator. The default must be overridable in watch settings.

## ADR-07: SendInput behind an interface, Right/Left arrows only

- **Context:** The app must control a presentation without coupling the domain to Windows APIs, and must avoid unintended keystrokes.
- **Decision:** Keyboard simulation uses Windows `SendInput` behind an interface (`IKeyboardSimulator`). Only Right and Left arrow keys are sent. Keys are sent only when the domain reports that the slide counter actually changed. `slide.set` never sends keys.
- **Consequences:** Testable with a fake keyboard. Counter and PC cannot drift because of no-op commands. Correcting the counter never moves the real presentation.

## ADR-08: Manual slide counter

- **Context:** Reading the real slide from PowerPoint, Canva, or Google Slides requires per-application APIs and is out of scope.
- **Decision:** The counter is maintained by the companion from next/previous commands and manual correction.
- **Consequences:** The counter can diverge from the real presentation if the presenter navigates on the PC. Manual correction (`slide.set`) is the remedy.

## ADR-09: Deterministic rules with an AI extension point

- **Context:** Recommendations must be predictable and testable, but a future provider may be wanted.
- **Decision:** V1 uses rule-based recommendations. An `IRecommendationProvider` abstraction allows an external provider later (Phase 10, optional).
- **Consequences:** No network dependency or credentials in V1. The interface must return structured results, not localized text.

## ADR-10: SQLite + EF Core on Windows, DataStore on the watch

- **Context:** History must persist across restarts (RNF 06) and the watch needs only small preferences.
- **Decision:** History in SQLite through EF Core on Windows (`EnsureCreated` is acceptable for the prototype). Preferences on the watch in DataStore.
- **Consequences:** No migrations in the prototype, so schema changes may require recreating the database. The watch holds no history; it queries the companion.

## ADR-11: Shared contracts as language-neutral specs, protocol v1

- **Context:** Kotlin and C# cannot share model code.
- **Decision:** Define messages in JSON Schema plus Markdown in `shared-contracts/`. Each side keeps its own models. The protocol is versioned (v1).
- **Consequences:** Contract tests on the C# side validate messages against the schemas. Both sides must be kept in sync by hand.

## ADR-12: Target `net10.0`

- **Context:** The .NET 8 SDK is not needed, and .NET 8 reaches end of support in November 2026.
- **Decision:** Target `net10.0` (SDK 10 is installed).
- **Consequences:** Uses a current supported runtime. Contributors need the .NET 10 SDK.

## ADR-13: Structured data, localization in the UI

- **Context:** Mixing Spanish text into domain and protocol would couple logic to a language.
- **Decision:** The domain and protocol return structured data (codes and numeric parameters). The watch and the WPF UI map codes to Spanish strings in resource files.
- **Consequences:** Logic is language-free and testable. Every code needs a string in each UI.

## ADR-14: The companion owns the authoritative timer

- **Context:** The watch may be slow, disconnected, or suspended.
- **Decision:** The companion keeps the authoritative timer. Snapshots carry server UTC time, elapsed milliseconds, and a running flag so the watch can tick smoothly between snapshots.
- **Consequences:** Reconnection restores exact time. The watch must correct its local tick whenever a snapshot arrives.

## ADR-15: Auto-save on session end

- **Context:** A manual save step adds friction and risks losing data.
- **Decision:** When a session ends (manual finish, finish while paused, interruption), the companion saves a history record automatically and sends `session.summary`. There is no save command.
- **Consequences:** History is always complete. The "save and return home" button on the watch only navigates; it does not trigger saving.

## ADR-16: Loopback-only listening by default

- **Context:** Listening on all interfaces exposes the server to the LAN.
- **Decision:** Listen on loopback by default, with a toggle in the companion UI to allow the local network.
- **Consequences:** Safer default and sufficient for the emulator. A physical watch requires the toggle and a firewall rule.

## ADR-17: Disconnection policy

- **Context:** A brief connection loss must not destroy a presentation.
- **Decision:** If the watch disconnects during a running session, the session keeps running. After a grace period (default 5 minutes) without reconnection it ends as `interruptedByDisconnection`. A reconnecting watch immediately receives the current state. Only one watch connection exists at a time; a new valid connection replaces the old one.
- **Consequences:** Satisfies RF 15. The grace period must be configurable and tested with fake time.

## ADR-18: The watch generates the session name

- **Context:** The UI has no text field (see UI decision 1).
- **Decision:** The watch generates the name (for example "Presentación 31 oct") and sends it in `session.start`. The companion uses a neutral English fallback name if it is absent.
- **Consequences:** Spanish names originate only in the watch UI. The companion fallback keeps the server language-free.

## ADR-19: Spanish only in the Windows desktop UI

- **Context:** The presenter is Spanish-speaking, but code and developer material are English.
- **Decision:** The Windows desktop UI is Spanish, in a resource file. Server, domain, protocol, and persistence contain no Spanish.
- **Consequences:** Developer error messages and logs stay in English. User-facing text never appears in non-UI layers.

## ADR-20: Automation-first verification until Phase 5

- **Context:** The user cannot run the app manually until Phase 5.
- **Decision:** Phases 1–4 are verified by automation only: builds, unit tests, in-memory integration tests (real WebSocket client against an in-process server, fake keyboard, fake time), and JVM Compose UI/screenshot tests. Manual testing starts in Phase 5.
- **Consequences:** Code must be designed for testability (injected clock, keyboard interface, in-process server).

## ADR-21: The alert is a state of the Control screen

- **Context:** A separate alert destination could block Next/Previous during a critical moment.
- **Decision:** The alert is a state of the Control screen, never a separate destination.
- **Consequences:** Next and Previous are never blocked (RNF 01). Control must render normal, alert, paused, and overtime states consistently (RNF 08).

## ADR-22: JSON Schema validator as a test-only dependency

- **Context:** Schemas, examples, and C# models must not drift apart (ADR-11), and the schemas are only useful if something executes them.
- **Decision:** The Protocol test project references `JsonSchema.Net` 9.6.0 (version checked on NuGet) to validate every example and every serialized message against the draft 2020-12 schemas. Test tooling is xUnit 2.9.3 with `Microsoft.NET.Test.Sdk` 18.11.0 and `xunit.runner.visualstudio` 3.1.5. No production project references any package; the Domain and Protocol libraries use the BCL only. The Protocol test project also references the Domain project, only to check that error codes, enums, and limits agree.
- **Consequences:** Contract drift fails the build. The dependency is not shipped. A future major upgrade of the validator only touches the test project.

## ADR-23: Pace and alert rule choices

- **Context:** The presenter needs useful, non-annoying alerts, and the rules must be deterministic and testable (ADR-09).
- **Decision:**
  - Expected slide is a linear interpolation from slide 1 at time 0 to the last slide at the planned duration, clamped afterwards. Pace is on track within a tolerance of max(1, round(10% of total slides)), inclusive; beyond it the status is behind or too fast. During a warm-up of min(60 s, 10% of planned time) the status is not enough data.
  - Average pace is registered slides per elapsed minute, and 0 below 5 s of elapsed time.
  - Time alerts: running out at min(5 min, 30% of planned) remaining (warning); critical at min(1 min, running-out threshold) remaining (critical); overtime when remaining is negative (critical). Each fires once; when one evaluation crosses several thresholds only the most severe is raised.
  - Pace alerts (behind, too fast) fire on a transition into the status. They may repeat after a cooldown (default 2 min, configurable) only if the presenter is still off pace and the previous alert was acknowledged; an unacknowledged alert is never re-raised. A pace alert clears itself when the presenter recovers.
  - Pace alerts are suppressed once the critical-time threshold is reached. A time alert raised in the same evaluation takes precedence over a pace alert, and the pace transition is consumed.
  - A new alert replaces the active one. Acknowledging clears it and is harmless when there is none. An alert never blocks slide commands.
  - Recommendations are checked in order: overtime, wrap up (last slide or within the critical-time threshold), none while there is not enough data, speed up (now when more than twice the tolerance behind, otherwise slightly), slow down, on track.
- **Consequences:** All thresholds live in `SessionOptions` and `RecommendationOptions` with the defaults above. Interrupting a session by disconnection counts time until the call, so it includes the grace period (ADR-17).

## ADR-24: Strict protocol parsing and structured errors

- **Context:** Silent coercion hides bugs between two codebases written in different languages.
- **Decision:** The C# parser rejects numbers sent as strings, non-integer values for integer properties, enum values that are not known strings, timestamps that are not UTC ending in `Z`, and missing required properties (including required nullable ones). It ignores unknown extra properties. Unknown message types, malformed JSON, and envelope violations produce a ready-to-send `error` message with a code (`invalidMessage` or `unsupportedVersion`) and never throw. Range rules (for example `plannedSlides` of at least 1) are enforced by the schemas and by the domain, not by the parser; examples that only a schema can reject are named `schema-only`.
- **Consequences:** Contract mistakes surface immediately in tests. Readers stay forward compatible because unknown properties are tolerated (see `shared-contracts/protocol-v1.md` for the compatibility policy).

## ADR-25: Tick contract for time-based alerts

- **Context:** The domain has no timers (ADR-20 forbids real timers in domain code and tests), so time-based alerts cannot fire on their own.
- **Decision:** The companion must call `Tick()` on the active session periodically, recommended about once per second, and broadcast a `state.update` whenever `Tick()` returns an alert. `Tick()` does nothing unless the session is running. All time comes from an injected `TimeProvider`; the monotonic timestamp drives elapsed time and the UTC clock only stamps snapshots and summaries.
- **Consequences:** Alert timing accuracy is bounded by the tick period. Tests advance a fake clock and call `Tick()` explicitly. The companion in Phase 4 owns the scheduling and must serialize access to the session.

## ADR-26: Android toolchain versions

- **Context:** Versions must be current, compatible with each other, and compatible with the machine in `docs/environment.md` (JDK 21, Android SDK platform `android-37.0`). They were looked up on Google Maven, Maven Central, and the Gradle site, not chosen from memory.
- **Decision:** Gradle 9.8.2 (distribution checksum verified), Android Gradle Plugin 9.2.1, Kotlin 2.4.21 (AGP 9 has built-in Kotlin support, so only the Compose compiler plugin is applied), Wear Compose Material 3, foundation, and navigation 1.7.1 (stable, so no fallback was needed), Compose UI 1.11.4, androidx navigation-compose 2.10.2, activity-compose 1.13.0, lifecycle 2.11.0, coroutines 1.11.0, all in `gradle/libs.versions.toml`. `minSdk` 30 (Wear OS 3, which Wear Compose requires), `targetSdk` 36 (Wear OS 6), `compileSdk` 37 (the installed platform). Java and Kotlin target 17.
- **Consequences:** AGP 9.2.1 was chosen deliberately below the newest 9.5.0 to stay closer to the installed Android Studio (build 261); lint reports that newer AGP and Compose UI 1.12.1 exist, and bumping them is a one-line change per version. The Gradle wrapper is committed; `local.properties` and `.kotlin/` are ignored by Git.

## ADR-27: JVM UI tests and screenshots

- **Context:** The user cannot run an emulator until Phase 5, so UI behavior and appearance must be verified without one.
- **Decision:** Compose UI tests run on the JVM with Robolectric 4.17 (Android SDK 36) and the Compose test library. Screenshots use Roborazzi 1.77.0 with Robolectric native graphics; it was verified to run on this machine. A test harness renders each state in a round window of 227 dp or 192 dp at density 2 (so 227 dp is exactly the 454 px baseline) with a chosen font scale. Roborazzi writes the PNGs to `docs/ui-previews/` when run with `-Proborazzi.test.record=true`. Test-only dependencies: Robolectric, Compose UI test, Roborazzi, coroutines-test, JUnit. Paparazzi was not needed. JDK 21 requires `--add-exports=java.base/jdk.internal.access=ALL-UNNAMED` for the test JVM.
- **Consequences:** No emulator in any check. Screenshots are rendered by Robolectric, so fonts and anti-aliasing can differ slightly from a real watch. Previews in Android Studio use the same `PreviewCatalog`, so previews and screenshots cannot diverge.

## ADR-28: Application id

- **Context:** The app needs a stable id from the first build.
- **Decision:** `com.presenta.wear`, also the Kotlin package and the Android namespace.
- **Consequences:** Changing it later would require updating the manifest, package names, and any signing or store setup.

## ADR-29: Repository abstraction and fake data

- **Context:** Phase 5 plugs in the real connection and must not touch the screens (ADR-20).
- **Decision:** Screens depend only on view models, and view models only on `PresentaRepository`: connection state, session status (an explicit `NoSession` or `Active(snapshot, receivedAt)`), a flow of session summaries, and the commands. `FakePresentaRepository` implements it deterministically with an injected monotonic clock and wall clock and never sleeps. It keeps only a trivial slide counter and timer; pace, alerts, and recommendations are scripted by the caller, so Kotlin contains no pace or alert rules (ADR-09). A `ControlStateFactory` turns snapshots into UI state and only interpolates elapsed time between snapshots (ADR-14). UI models mirror the names in `protocol-v1.md`.
- **Consequences:** Every state, including read-only summaries and interruptions, can be produced in tests. The interface is the contract the Phase 5 client must satisfy. Fake history names and data are English placeholders and never shown.

## ADR-30: The alert is a state of Control (watch implementation)

- **Context:** ADR-21 says an alert must never block Next or Previous.
- **Decision:** `ControlUiState.alert` switches the Control screen between its normal and alert layouts; both always contain the Previous and Next buttons. Overtime is an accent (red) of either layout, applied only when the remaining time is negative or the alert kind is overtime. Swipe-to-dismiss is disabled on Control and Summary so a stray swipe cannot leave a running presentation. Pausing, the slide selector, and the finish confirmation are overlays inside Control, not routes. A test raises an alert and checks that both buttons are enabled and still change the slide.
- **Consequences:** The alert layout hides the large slide counter (as in the mockup), so the counter and the clock are not reachable until "Entendido" is pressed; Next and Previous are.

## ADR-31: Touch-target exceptions

- **Context:** The rule is 48 dp where physically possible. On Small Round (about 192 dp) the curve of the screen and the vertical budget make that impossible for every element at once.
- **Decision:** On Large Round everything interactive is at least 48 dp (checked by a test). On Small Round only these are smaller, and they are secondary controls: the slide counter and the clock block on Control are 36 dp tall (48 dp wide); the Setup steppers are 44 dp tall and 42 dp wide, and "Iniciar" is 44 dp tall; "Entendido" on the alert is 44 dp tall. Previous and Next stay 48 dp on both sizes. Content that scrolls (the history list, the summary on Small Round) is checked where it is shown first.
- **Consequences:** The exceptions are listed in `LayoutAccessibilityTest` and fail the build if they grow. A larger Small Round layout would need a different information design.

## ADR-32: Font scale caps

- **Context:** Round screens cannot absorb much text growth without clipping at the curve.
- **Decision:** Screens that must fit without scrolling (Home, Setup, Control with its overlays, Summary, and the history messages) cap the system font scale at 1.15 on Large Round and 1.0 on Small Round. The history list keeps the full scale. Long headlines near the top shrink to fit the available chord instead of being cut. Tests render scales 1.0 and 1.3 on both sizes and check that all text is inside the circle, that texts do not overlap, and that touch targets hold.
- **Consequences:** Users who set the largest font sizes do not get larger text on Small Round for these screens. This is a known accessibility trade-off to revisit if a real watch shows more room.

## ADR-33: Text, numbers, and dates

- **Context:** The audience is Spanish-speaking (Colombia), while code is English.
- **Decision:** Spanish is the default and only resource set, with English resource names and plural resources wherever a count appears. One `StringMapper` converts structured codes and parameters into text. Numbers use fixed formats independent of the device locale: pace `1.37` (dot, two decimals), time `mm:ss` or `h:mm:ss`, clock time 24-hour `HH:mm`. History day labels are Hoy, Ayer, or `dd Mon` relative to an injectable clock. The default session name is built on the watch from a resource (ADR-18).
- **Consequences:** A different device language never changes the app. Adding a language later means adding a resource folder and revisiting the fixed formats.

## ADR-34: Keyboard rules

- **Context:** The companion presses keys on the presenter's computer, so mistakes are visible to the audience.
- **Decision:** `slide.next` presses the Right arrow and `slide.previous` the Left arrow, only when the domain reports that the slide counter actually changed. At the last slide Next sends nothing; at slide 1 Previous sends nothing. `slide.set` never sends a key. A runtime switch ("send keys to the presentation", default on) turns key sending off; commands then still move the counter. A key that cannot be sent is reported as `internalError` in `command.result` and logged; the session continues.
- **Consequences:** The counter and the presentation can only drift by the presenter's own actions or a failed key. All rules are covered by tests with a recording fake keyboard.

## ADR-35: Keyboard safety

- **Context:** A bug or a malformed message must never be able to type arbitrary text on the computer.
- **Decision:** The only route from the companion to the keyboard is `ISlideKeyboard.Send(SlideDirection)`, where `SlideDirection` has exactly two values, `Next` and `Previous`. The Windows implementation (`Presenta.Keyboard.Windows`, `SendInput`) maps them to virtual keys `0x27` (Right) and `0x25` (Left) and rejects any other value. It sends a key down and a key up as extended-key keyboard events, never mouse or hardware input. Tests build the input structures and check the codes, the flags, the structure size, and that no other key can be produced. Pressing real keys is checked by hand (see `docs/manual-test-guide.md`).
- **Consequences:** Adding any other key needs a new enum value, a new mapping, and a new decision. The key goes to whichever window has the focus; the companion does not read or control the presentation program.

## ADR-36: Auto-save happens exactly once

- **Context:** ADR-15 says the companion saves automatically when a session ends.
- **Decision:** Ending a session (finish, finish while paused, interruption) first clears the active session and then saves one history record, so no second path can save it again. After a manual end the companion sends `command.result`, `session.summary`, and then `state.update` with no session. After an interruption nothing is sent. If saving fails, the error is logged, the watch still receives the summary, and the session is closed anyway.
- **Consequences:** History can miss a record only if the disk write fails. A watch that was disconnected during an interruption finds the record through `history.list`.

## ADR-37: Listening address and rebinding

- **Context:** ADR-16 makes loopback the default. The emulator reaches the host through `10.0.2.2`, which the Android emulator forwards to the host's loopback.
- **Decision:** Kestrel listens on `127.0.0.1` by default and on `0.0.0.0` only when the user turns on "allow physical watch on the local network". Changing the switch restarts the listener (`RebindAsync`) on the same port while the session logic, which lives outside Kestrel, keeps running; connected watches are closed and reconnect. The default port is 8765, set with `--port` on the command line. The window shows the emulator address, the `adb reverse` address, and the machine's LAN addresses when the switch is on. Binding is verified in tests by reading the bound endpoints.
- **Consequences:** The first LAN use needs a Windows Firewall rule (see the companion README). A busy port is reported in the window instead of crashing.

## ADR-38: Disconnection and the grace period

- **Context:** ADR-17 defines the policy; this fixes the implementation.
- **Decision:** When the watch's connection ends while a session exists, the session keeps running and a timer starts (via `TimeProvider`). If a paired watch reconnects, the timer is cancelled and the watch immediately receives the current state in `hello.accepted`. If the grace period (default 5 minutes, configurable) passes first, the session is marked `interruptedByDisconnection` and saved, and no message is sent. The recorded duration includes the grace period, as in ADR-23. A connection silent for longer than the heartbeat timeout (default 15 seconds) is closed by the server, which then counts as a disconnect.
- **Consequences:** The watch should send a `ping` about every 2 seconds and detect a lost server within 5 seconds on its own side (RNF 04); the companion's own silent-peer detection is slower by design. Clean closes are noticed immediately.

## ADR-39: One watch connection at a time

- **Context:** Two watches controlling one presentation would conflict.
- **Decision:** A second connection that completes pairing replaces the first: the old socket receives a normal close frame and its end does not start the grace period. A second connection that fails pairing changes nothing. Pairing failures are counted per remote address: five in a row lock that address out for 30 seconds (both configurable); the lockout is reported as `pairingLockedOut` with `retryAfterMs`. The code is compared in constant time and never logged.
- **Consequences:** Someone on the network who knows the code can take over the connection. This is accepted for a trusted local network (ADR-04).

## ADR-40: Session name fallback

- **Context:** ADR-18 says the watch sends the name.
- **Decision:** If `session.start` has no name, the companion uses a neutral English name such as `Session 2026-11-02 14:30` built from the local time. The name is stored and returned as given; the watch decides how to show it.
- **Consequences:** Such a name is only ever seen if a watch omits the name.

## ADR-41: EnsureCreated for the prototype history database

- **Context:** ADR-10 allows `EnsureCreated`. The history is a single table.
- **Decision:** `SqliteHistoryStore` creates the schema with `EnsureCreated` and stores times as UTC Unix milliseconds (SQLite cannot order timezone-aware values). It opens a short-lived context per operation with pooling off, so the file is never held open. The default path is `%LOCALAPPDATA%\Presenta\history.db`; `--database` overrides it. Newest first, default limit 50.
- **Consequences:** A schema change needs a migration or a new file; `EnsureCreated` does not alter an existing table. There is no migration history.

## ADR-42: Kestrel hosting and a single writer

- **Context:** Commands, the one-second tick, heartbeats, and disconnects all touch the same session.
- **Decision:** `CompanionService` holds all logic and one gate (a semaphore); every access to the session, the connection, and the history goes through it, so those events are strictly serialized. Each connection has a queue drained by one sender loop, so the socket is never written concurrently and messages keep their order. `CompanionHost` is a thin Kestrel wrapper (slim web application, one WebSocket endpoint at any path) built with options: port, time provider, keyboard, history store, thresholds. A single `TimeProvider` timer ticks once per second to tick the domain, check heartbeats and the grace period, and send a `state.update` every 5 seconds while a watch is connected. A stress test sends 100 commands while 300 ticks fire.
- **Consequences:** One slow history write delays other commands briefly; acceptable for one watch. All behavior depends on `TimeProvider`, so tests need no real waiting beyond socket I/O.

## ADR-43: Test doubles for time, keyboard, and storage

- **Context:** Server behavior must be tested with a real `ClientWebSocket` but without waiting for real minutes or touching the keyboard.
- **Decision:** `Presenta.TestSupport` (tests only, no packages) holds `ManualTimeProvider` (a `TimeProvider` whose timers fire as time is advanced, so `Task.Delay` and `CreateTimer` work), `RecordingSlideKeyboard`, and `InMemoryHistoryStore`. No fake-time NuGet package is used. Tests wait for socket messages or poll a condition for a few seconds of real time; they never sleep to simulate session time.
- **Consequences:** The fake clock can run ahead of the server, so tests that jump time raise the heartbeat timeout explicitly.

## ADR-44: Desktop window structure

- **Context:** The window must be testable without WPF and must keep Spanish text out of code.
- **Decision:** View models live in `Presenta.Companion.ViewModels` (no WPF types, plain `INotifyPropertyChanged`); the WPF project only holds XAML, styles, and startup. All window text is Spanish in `UiStrings.resx` with English keys, read through `UiText`; XAML binds to view-model properties and a `UiLabels` object. Service events are marshalled to the UI thread through `IUiDispatcher`. The window starts the server on launch; closing it disposes the host, which closes any watch connection and releases the port.
- **Consequences:** Window layout cannot be verified headlessly; only its state and text mapping are tested. Adding text means adding a key and a test line.

## ADR-45: Server address on the watch

- **Context:** The emulator reaches the PC at `10.0.2.2` (ADR-06). Editing an address on a round watch screen is out of scope.
- **Decision:** The default address `ws://10.0.2.2:8765` is stored in DataStore (written together with the first pairing code) and read at startup. There is no UI to change it. For development, the **debug** source set reads an optional file `files/server_address.txt` in the app's private storage; set it with `adb shell run-as com.presenta.wear sh -c "echo ws://localhost:8765 > files/server_address.txt"` and restart the app (use this together with `adb reverse tcp:8765 tcp:8765`). The **release** source set has an `ServerAddressOverride` that always returns null, so no override ships, and a test checks that the release version cannot read a file.
- **Consequences:** `run-as` only works on debuggable builds, which is exactly the intended scope. A physical watch needs a different address and the LAN support deferred in ADR-46.

## ADR-46: Cleartext traffic

- **Context:** Android blocks `ws://` by default (ADR-05) and the companion has no TLS.
- **Decision:** `network_security_config` sets `cleartextTrafficPermitted="false"` as the base and allows it for exactly three hosts, without subdomains: `10.0.2.2`, `localhost`, and `127.0.0.1`. The manifest declares `INTERNET` and nothing else. A test parses the manifest and the config and fails if a host is added, a wildcard or debug override appears, or cleartext is enabled globally.
- **Consequences:** The emulator and `adb reverse` work. **A physical watch on a LAN address does not work yet**: Android cannot express IP ranges in this file, so it needs another approach (a configurable host list, TLS, or user-approved hosts), planned for Phase 8.

## ADR-47: Pairing screen and the stored code

- **Context:** The companion shows a random six-digit code (ADR-04) and generates a new one every time it starts. A numeric keypad with 48 dp keys does not fit a round screen.
- **Decision:** A derived screen "Vincular" asks for the code one digit at a time: − and + change the selected digit (9 wraps to 0; the crown does the same), "Siguiente" moves on, a back button returns to the previous digit, and on the sixth digit the button becomes "Conectar". The focus order follows the reading order. The last code used is stored in DataStore, shown again when the screen opens, and used for automatic reconnects. It is a short LAN pairing code that changes whenever the companion restarts, not a credential, so it is stored unencrypted. `invalidPairingCode`, `pairingLockedOut`, `notPaired`, and `unsupportedVersion` return the watch to this screen with the matching Spanish message, and automatic retries stop until the user enters a code.
- **Consequences:** Entering six digits takes a few dozen taps; the prefilled code makes repeat use short. After the companion restarts the user must enter the new code once.

## ADR-48: Heartbeat, loss detection, and reconnection

- **Context:** RNF 04 asks for the connection state within 5 seconds; the companion drops silent peers after 15 seconds (ADR-38).
- **Decision:** The watch sends `ping` every 1.5 s. Any frame from the companion counts as a sign of life. A watchdog runs every 0.5 s and declares the connection lost when nothing has arrived for 3 s, so the worst case from the last frame to detection is 3.5 s (the constants are in `RemoteTimings`, and a test fixes the worst case at or below 5 s with the production values). Detection through a closed or failed socket is immediate. After a loss the state is `Reconnecting`, and the watch retries with a capped exponential backoff (0.5 s, 1 s, 2 s, then 4 s) sending `hello` with the stored code on every attempt. A connection attempt times out after 4 s and a missing `hello.accepted` after 3 s. After 30 s without a connection, automatic retries stop and the state becomes `Disconnected`; the "Reintentar" button (or entering a new code) starts again.
- **Consequences:** Two missed pongs in a row (about 3 s) are treated as a loss; a very slow network may flap. Stopping retries after 30 s saves battery but means the user must press "Reintentar" after a long outage.

## ADR-49: Timer interpolation and commands while disconnected

- **Context:** The companion owns the timer (ADR-14) and a command cannot succeed without it.
- **Decision:** Every `state.update` and `hello.accepted` replaces the session and records the local monotonic time at which it arrived. Between snapshots the UI adds the time elapsed since then to the server's elapsed value while the session is running, whole seconds only, so elapsed plus remaining always equals the planned time. The clock is injectable. While the connection is lost the last known session stays on screen and keeps ticking. Commands are never queued: without a connection they fail immediately with `invalidMessage` ("No se pudo enviar el comando"); a command with no answer within 5 s fails the same way; commands waiting when the connection drops fail at once. A failed command never ends the session.
- **Consequences:** The displayed time can drift slightly if the watch is suspended; the next snapshot corrects it. A command the user taps during an outage is lost, not delayed.

## ADR-50: The Control ring shows elapsed time

- **Context:** The ring showed slide progress while the question was open (`ui-direction.md`).
- **Decision:** The ring is elapsed time divided by planned duration, clamped to 0..1, using the interpolated elapsed time. The slide number stays textual. The ring exposes its value as a progress range for accessibility.
- **Consequences:** The ring fills steadily whatever the slide, so it reads as time, and in overtime it is full (red). Slide progress is only visible in the number.

## ADR-51: Real and fake repository switch

- **Context:** Previews, screenshots, and UI tests need data without a companion.
- **Decision:** `PresentaApplication` is the only composition point. By default it builds `RemotePresentaRepository` (OkHttp, DataStore, system clock). The Gradle property `-PuseFakeRepository=true` sets `BuildConfig.USE_FAKE_REPOSITORY` and makes the app run on `FakePresentaRepository`. Previews and all tests use the fake directly and do not depend on the flag. The interface gained `pairing`, `savedPairingCode()`, and `connectWithCode()`; the screens depend only on the interface.
- **Consequences:** Nothing in the UI knows which implementation runs.

## ADR-52: Watch dependencies for the connection

- **Context:** The client needs a WebSocket, JSON, and small persistent settings.
- **Decision:** OkHttp 5.5.1 (WebSocket), kotlinx.serialization (JSON 1.12.0 and the compiler plugin matching Kotlin 2.4.21), and DataStore Preferences 1.3.0, all checked on Maven Central and Google Maven. Test-only: OkHttp MockWebServer 5.5.1. The parser is deliberately as strict as the C# one: kotlinx.serialization accepts `"4"` for a number, so an explicit type check rejects numbers and booleans sent as strings; unknown properties are ignored; unknown message types and malformed text are reported as rejected, never thrown. Tests compare every enum and error code with the schemas and every shared example with the models. One DataStore test uses a real file with a single write because DataStore cannot replace an existing file on Windows' JVM; the rest use an in-memory DataStore.
- **Consequences:** No DI framework, Ktor, or analytics. The Kotlin models must be kept in step with the schemas by hand; the contract tests fail if they drift.

## ADR-53: Developer host and the cross-stack test

- **Context:** The watch client and the companion are written in different languages and must be proven to work together without the user's manual run.
- **Decision:** `windows-companion/tools/Presenta.DevHost` is a console app that runs the real `Presenta.Server` with a keyboard that only prints `KEY Right` or `KEY Left`; it has no reference to the Windows keyboard project and cannot press a key. It takes `--code` (test use only), `--port`, `--database`, `--grace-seconds`, and `--heartbeat-seconds`, prints `LISTENING port=N`, accepts `rebind` and `quit` on standard input, and shuts down cleanly. The Gradle task `:app:crossStackTest` (opt-in, needs the .NET SDK) builds it, starts it, runs the real Kotlin client through a whole presentation, a listener restart, a summary, and the history, and checks the key log. The default `testDebugUnitTest` excludes that test.
- **Consequences:** The default test run needs no .NET SDK. The cross-stack test takes about half a minute and uses loopback ports.

## ADR-54: Keys go to a chosen presentation window, activated first

- **Context:** In the first manual test the watch counter moved but PowerPoint did not. `SendInput` delivers to the foreground window, and tapping the emulator made Android Studio the foreground program (ADR-07 and ADR-34 assumed the presentation had the focus). A desktop test now reproduces it: with a second window in front, a bare Right arrow lands in that window and not in the presentation.
- **Decision:** The companion lets the user choose a presentation window and, before every key, brings that window to the front. If it is minimized it is restored (maximized and full-screen states are not touched). If it is not already the foreground window, the companion attaches its input queue to the foreground thread (`AttachThreadInput`), calls `SetForegroundWindow`, and detaches. This is the supported technique for a caller that is not the foreground process. The Alt-key trick, or any other synthetic key used to unlock foreground changes, is forbidden: the only keys the program can ever produce remain Right and Left (ADR-34). The system's foreground-lock timeout is never changed. If Windows still refuses, the delivery fails cleanly (ADR-55). Focus is deliberately not given back to the previous window afterwards: restoring it would need a second activation after every slide and would steal focus from the presenter's notes or timer for no benefit.
- **Consequences:** The presentation window ends up in front after the first slide command. Each command pays the activation cost only when the target is not already in front (usually a few milliseconds; the retries are bounded at 3 x 250 ms).

## ADR-55: Never deliver to an unconfirmed window

- **Context:** A key sent to the wrong window can edit a document or send a message.
- **Decision:** After activation the companion confirms that the target really is the foreground window (up to 3 attempts, 250 ms each, well under a second in total), and checks once more immediately before `SendInput`. If the target cannot be confirmed, nothing is sent and a typed result says why: no target chosen, not found, ambiguous, elevated, activation refused, not confirmed in front, or the key not accepted. A target owned by an elevated process while the companion is not is detected beforehand and reported without trying (Windows blocks the input), and the status line says so.
- **Consequences:** There is still a tiny window between the last check and the key during which the user could click elsewhere; it is microseconds and is documented as a limitation.

## ADR-56: Target identity, resolution, and persistence

- **Context:** A window handle dies with the window, and PowerPoint, browsers, and PDF viewers create new windows all the time.
- **Decision:** The choice is stored as an identity: process image name, window class, and a title part (the title at selection time, trimmed of spaces and a leading "unsaved" marker such as `*`). A window matches when the process matches (case-insensitive), the class matches exactly, and its normalized title contains the stored part (case-insensitive). The live handle is only a cache: if it is dead or no longer matches it is resolved again; with no match the status is "not found", with several matches (and the cached handle not among them) it is "ambiguous", and in both cases no key is sent. The identity is saved in `%LOCALAPPDATA%\Presenta\target.json` (path configurable with `--target-settings`), written through a temporary file; a missing, corrupt, or incomplete file means "none selected". The file contains only the three fields and a version, never the pairing code. The title part can contain a document file name.
- **Candidate list:** visible, uncloaked, titled windows without the tool-window style, excluding the companion's own process. A PowerPoint slide-show window (process `POWERPNT`, class `screenClass`) is suggested and listed first. The class was checked on this machine with PowerPoint 16: the windowed slide show and the full-screen show are both `screenClass` with the title "Presentación de PowerPoint - [file.pptx]", and the editing window is `PPTFrameClass`. Browsers (Google Slides, Canva) are identified by title only and are not suggested.
- **Consequences:** Two windows with the same program, class, and title are ambiguous after a restart; the user chooses again. A title that changes (a browser tab, a renamed file) makes the window "not found" until it is chosen again.

## ADR-57: Additive error codes and when keys are required

- **Context:** The watch needs to tell the presenter that the problem is the computer's presentation window, not the connection.
- **Decision:** Protocol v1 gains two error codes without changing any message shape: `targetWindowUnavailable` (keys are on but no usable window: none chosen, not found, ambiguous, elevated) and `keyDeliveryFailed` (the window exists but activation, confirmation, or the key itself failed). When "send keys to the presentation" is on, `session.start` is answered with `targetWindowUnavailable` unless the target resolves (a minimized window counts as usable, because it is restored); with the switch off no window is required and nothing changes. Schemas, `protocol-v1.md`, the C# enum, the Kotlin models, shared examples, and the parity tests were updated. The watch maps them to two Spanish strings: "Elige la ventana de la presentación en el computador" and "No se pudo enviar el comando a la presentación". No other watch UI changed.
- **Consequences:** An older watch build would show its generic error for the new codes; both sides are in this repository, so they ship together.

## ADR-58: Key first, counter second

- **Context:** The counter must never run ahead of the presentation, but undoing a counter move has side effects (alerts are re-evaluated when the counter changes).
- **Decision:** When the switch is on and the counter would really move (a new read-only question on the session, `WouldMove`), the key is delivered first and the counter is moved only if delivery succeeded. A failed delivery therefore leaves the counter untouched (nothing to roll back), returns the failure code in `command.result`, and the session continues. When the counter would not move (slide 1 for Previous, the last slide for Next) no key is attempted. With the switch off the counter simply moves. `slide.set` never sends a key and never needs a target. An exception from the keyboard counts as a failed delivery.
- **Consequences:** The order of the two steps in the server is the reverse of Phase 4; the user-visible result is that the watch and the slideshow agree, including after errors.

## ADR-59: Where the window code lives

- **Context:** The Win32 calls must stay in one place, and everything else must be testable without a desktop.
- **Decision:** A new project `Presenta.Targeting` (portable) holds the window-system interface (`IWindowSystem`), the target identity, the settings store, the target service (candidates, resolution, status, activation) and `WindowSlideKeyboard`, the `ISlideKeyboard` the app uses. `Presenta.Keyboard.Windows` holds the only Win32 code (`Win32WindowSystem` and the internal arrow-key injector, which no longer implements `ISlideKeyboard` so a bare "press into the foreground" can no longer be wired in by accident). `ISlideKeyboard` now returns a typed `KeyDeliveryResult` and can report the `TargetState`. The view models use `PresentationTargetService`; the server sees only `ISlideKeyboard`. The activation runs while the server holds its gate, bounded at under a second, so commands stay serialized. Tests use `FakeWindowSystem`; an opt-in desktop test category (`PRESENTA_DESKTOP_TESTS=1`) opens real windows in separate processes and presses real keys.
- **Consequences:** No new third-party packages. The default `dotnet test` skips the five desktop tests.
