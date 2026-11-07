# wear-app

Wear OS frontend for Presenta: screens, navigation, UI state, and view models.

Stack: Kotlin, Jetpack Compose for Wear OS, Wear Material 3 (stable 1.7.1), Wear Compose navigation, ViewModel and StateFlow.
This module never touches Windows keyboard APIs or OS automation. It will only talk to the companion through the protocol in `../shared-contracts/`.
Every user-visible string is Spanish and lives only in `app/src/main/res/values/strings.xml` (English resource names).

## Status (Phase 5)

The whole UI exists and, by default, talks to the Windows companion through `RemotePresentaRepository` (OkHttp WebSocket, protocol v1, DataStore for the pairing code). The fake repository is still used by previews, screenshots, and tests; build with `-PuseFakeRepository=true` to run the app on it. There are no haptics yet (Phase 6). `PresentaApplication` is the only composition point.

How the app connects:

- Default address `ws://10.0.2.2:8765` (the emulator's alias for the PC). First launch shows the pairing screen "Vincular": enter the six-digit code shown by the companion, one digit at a time. The code is remembered.
- Cleartext `ws://` is allowed only for `10.0.2.2`, `localhost`, and `127.0.0.1` (`network_security_config`). **A physical watch on a LAN address is not supported yet** (Phase 8).
- To use `adb reverse tcp:8765 tcp:8765` with `localhost` on a **debug** build:
  `adb shell run-as com.presenta.wear sh -c "echo ws://localhost:8765 > files/server_address.txt"`, then restart the app. Release builds ignore this file.
- The watch pings every 1.5 s and treats 3 s of silence as a lost connection (noticed within 3.5 s), then retries with a short backoff for 30 s. See ADR-48.

Manual testing steps: `../docs/manual-test-guide.md`.

## Open the project

1. In Android Studio choose **Open** and select this `wear-app` folder (not the repository root).
2. Let Gradle sync. Use JDK 21 (the one bundled with Android Studio works). Android Studio creates `local.properties` with the SDK path; it is ignored by Git.
3. Compose previews: open `app/src/main/kotlin/com/presenta/wear/ui/preview/Previews.kt` and use the Compose preview panel. It shows every screen and state on Large Round and Small Round.
4. Rendered screenshots of all states are in `../docs/ui-previews/` (`large-round/` and `small-round/`).

The emulator is not needed for anything in this phase.

## Commands

Run from this folder (`gradlew.bat` on Windows, `./gradlew` elsewhere).

| Task | Command |
|---|---|
| Build the debug app | `gradlew.bat :app:assembleDebug` |
| Run all unit and UI tests | `gradlew.bat :app:testDebugUnitTest` |
| Run lint | `gradlew.bat :app:lintDebug` |
| Re-record the screenshots | `gradlew.bat :app:testDebugUnitTest --tests "*ScreenshotTest" "-Proborazzi.test.record=true"` |
| Run the real client against the real server (opt-in, needs the .NET SDK) | `gradlew.bat :app:crossStackTest` |

Notes:

- The tests run on the JVM with Robolectric (no emulator or device). On the first run Robolectric downloads the Android 16 (SDK 36) runtime jar, so the machine needs network access once.
- JDK 21 needs `--add-exports=java.base/jdk.internal.access=ALL-UNNAMED` for Robolectric SDK 36; `app/build.gradle.kts` already sets it for the test tasks.
- In PowerShell, quote the `-P` property as shown above.

## Structure

```
app/src/main/kotlin/com/presenta/wear/
  MainActivity.kt, AppContainer.kt      entry point, hand-written dependency holder
  model/                                UI-side models mirroring shared-contracts/protocol-v1.md
  protocol/                             wire models and the strict codec for protocol v1, mapping to UI models
  data/                                 PresentaRepository (the only dependency of the screens), clocks
  data/remote/                          RemotePresentaRepository, OkHttp transport, pairing store (DataStore)
  data/fake/                            FakePresentaRepository and canned data from the mockups
  format/                               mm:ss and pace formatting, day labels, StringMapper (codes to Spanish text)
  ui/theme/                             design tokens and Wear Material 3 theme
  ui/components/                        buttons, steppers, pills, rings, icons, shrink-to-fit text
  ui/screens/<name>/                    home, pairing, setup, control, summary, history (stateless composables)
  ui/viewmodel/                         view models and ControlStateFactory (no pace or alert rules)
  ui/navigation/                        PresentaApp and routes
  ui/preview/                           PreviewCatalog (all states) and the @Preview functions
app/src/main/res/values/strings.xml     all Spanish text
app/src/test/kotlin/                    unit tests, Compose UI tests, accessibility checks, screenshot tests
```

## Screens and states

Navigation views: Home, Setup, Control, Result/History. The alert is a **state of Control**, never a destination, so Next and Previous are never blocked.

The Control ring shows elapsed time (ADR-50). Setup limits: slides 1 to 200 (default 20), minutes 1 to 180 (default 15). Pace is written with a dot and two decimals (`1.37`) using `Locale.ROOT`, whatever the device locale. Times are `mm:ss`, and `h:mm:ss` from one hour.

See `../docs/ui-direction.md` for the design direction, the derived screens, and the open question about the progress ring.

## Where things are

`PresentaApplication.container` chooses the repository (real by default). Screens and view models depend only on `PresentaRepository`; the connection logic, timings (`RemoteTimings`), and states are in `data/remote/RemotePresentaRepository.kt`.
