# Validation guide

This is the single list of manual tests for Presenta. Follow it from top to bottom, write what you see in the results sheet (section 9), and send the results back as described in section 12. The setup steps (starting the companion, starting the emulator, running the watch app) and the troubleshooting table are in [../manual-test-guide.md](../manual-test-guide.md); this guide never repeats them, so there is only one list of test steps.

Everything a program could prove has already been proven by the automated tests (see [../testing-strategy.md](../testing-strategy.md) and the file `automated-run-latest.md` next to this guide). The manual campaign checks what only a person can: real key presses in a real slideshow, how the screens look and feel, and how fast things happen.

## 0. How to read this guide

- **Test IDs.** Rows 1 to 68 keep the numbers of the first manual guide, so earlier partial results still map. New rows start at 69 (rows 86 to 88 are the diagnostic rows of Phase 7b-1 rows 89 to 99 those of Phase 8 and rows 100 to 107 those of Phase 10 (the optional AI)). Visual checks are `V1` to `V12`, preparation items are `P1` to `P16`, timing measurements are `T1` and `T2`.
- **Spanish text.** Text in double quotes is exactly what the app shows (watch or companion window). Text in backticks is a command, a file name, or something that is not part of the app. A text such as "Quedan 5 min" has numbers that change with the situation; the row says when.
- **Covers.** `RF` and `RNF` are the functional and non-functional requirements of [../requirements-traceability.md](../requirements-traceability.md). `AC1` to `AC7` are the seven acceptance criteria of the prototype (section 8). Rows marked *optional* may be skipped; they are not needed for sign-off.
- **Evidence.** `S` is a screenshot of the watch, `C` a screenshot of the companion window, `L` a Logcat excerpt, `T` the companion timing file, `V` a short video (phone or screen recording). Name each file with the test ID, for example `E09-S.png`.
- **Automated.** The last column names the automated tests that already cover the same behavior. A pass here means the behavior also works on a real emulator and a real slideshow, which the automated tests cannot show.
- **Result values.** Write `Pass`, `Fail`, `Blocked` (you could not run it, say why), or `Skip` (optional rows only).

## 1. Pre-flight checklist

Tick each item before the first test. Setup details are in the manual guide, sections 1 to 4.

| ID | Check | How | Done |
|---|---|---|---|
| P1 | The automated checks are green. | In PowerShell: `./scripts/verify-all.ps1 -CrossStack`. Keep `docs/validation/automated-run-latest.md` for the campaign. | |
| P2 | .NET 10 SDK. | `dotnet --version` shows 10.0.x. Write the version in the sign-off section. | |
| P3 | JDK 21 and Android Studio. | Android Studio opens the `wear-app` folder and Gradle sync finishes. | |
| P4 | The emulator `Wear_OS_Large_Round` exists and starts. | Device Manager shows it; start it and wait for the watch face. | |
| P5 | `adb` works. | `& "C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe" devices` lists the emulator as `device`. All `adb` commands below use this full path. | |
| P6 | The watch app is installed from Android Studio (the **debug** build). | Run the `app` configuration on the emulator. Debug builds also write the timing and haptic lines used in this guide. | |
| P7 | The companion is running in the debug configuration. | `dotnet run --project windows-companion/src/Presenta.Companion.App` (this builds Debug by default). Write down the six-digit pairing code. | |
| P8 | The port is free. | The window shows "Puerto 8765". If it reports an error, close the other program that uses the port. | |
| P9 | Windows Firewall. | For the emulator you do not need to allow anything: choose Cancel if Windows asks. Only the optional network row 81 may trigger the question. | |
| P10 | A test deck of 5 slides in PowerPoint, each with a big number 1 to 5. | Run it **windowed** first (*Slide Show > Set Up Slide Show > Browsed by an individual (window)*, then F5). The full-screen run comes later (row 33). | |
| P11 | Notepad open with a few lines of text. | Used by the wrong-application check (row 34). | |
| P12 | A stopwatch (phone). | Used by rows 28, 29, 71 and the timing items. | |
| P13 | How to take emulator screenshots. | Use the camera icon in the emulator's side toolbar, or `adb exec-out screencap -p > E00.png` (run from the folder where you collect evidence). | |
| P14 | How to capture Logcat. | `adb logcat -d -s PresentaHaptics:D PresentaTiming:D > logcat.txt` (add `-c` before to clear it first: `adb logcat -c`). | |
| P15 | An evidence folder outside the repository. | For example `C:\Users\ASUS\Desktop\presenta-evidence`. | |
| P16 | Close other presentations. | So the keys cannot go to another program by mistake. | |

## 2. Test matrix

Each table row is one test. The empty result columns are in the results sheet (section 9).

### A. Pairing and connection

Precondition: the companion is running and the watch app is freshly installed, so it has no stored code. Evidence: `S` of each watch screen mentioned, `C` of the companion window.

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 1 | RF 01, RF 02, AC1 | Run the watch app for the first time. | The pairing screen "Vincular" appears with 000000 and the first digit highlighted. | S | `PairingUiTest`, `PairingViewModelTest` |
| 2 | RF 01, RNF 07 | Enter a **wrong** code, for example 000000: press "Siguiente" five times, then "Conectar". | The watch stays on the pairing screen and shows "Código incorrecto" in amber. The companion lists "Código de vinculación incorrecto". | S, C | `PairingTests`, `PairingFlowTest`, `CrossStackTest` |
| 3 | RF 01, RNF 07 | Try wrong codes four more times (five wrong attempts in total), then once more. After about 30 seconds enter the right code. | The watch shows "Demasiados intentos. Espera un momento". The companion lists "Demasiados intentos: conexión bloqueada por un momento". After about 30 seconds the right code works again. | S, C | `PairingTests`, `PairingFlowTest`, `CrossStackTest` |
| 4 | RF 01, RF 02, AC1 | Enter the **right** code from the companion window. Use + and − to set each digit (9 wraps to 0), "Siguiente" to move on, "←" to go back one digit. The emulator's crown (rotary control) also changes the digit. On the sixth digit press "Conectar". | The watch shows Home with the pill "PC conectado". The companion shows "Reloj conectado" and the event "Reloj conectado". | S, C | `PairingFlowTest`, `CrossStackTest` |
| 5 | RF 01 | Close the app on the emulator and open it again. | It goes straight to Home without asking for the code (the code is remembered). | S | `PairingStoreTest` |
| 6 | RF 02, RF 15, RNF 04, AC1 | With no presentation running, in the companion window press "Detener servidor". | Within about 4 seconds the watch shows "Reconectando…" and after about 30 seconds "Sin conexión al computador" with "Reintentar". (With a presentation running it never gives up: row 97.) | S | `RemotePresentaRepositoryTest`, `PresentaFlowTest` |
| 7 | RF 02, AC1 | Press "Iniciar servidor" in the companion, then "Reintentar" on the watch. | The watch returns to Home with "PC conectado". | S | `RemotePresentaRepositoryTest`, `CrossStackTest` |

### B. Running a presentation

Precondition: your test deck is open and the slideshow is running (F5), and that window is chosen in the companion's "Ventana de la presentación" section (rows 30 and 31 of section E; do them first if you have not). The companion brings the chosen window to the front before each key, so you may click the emulator freely.

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 8 | RF 03, RF 04, RF 08, RF 09, AC3, AC4 | On the watch: "Nueva presentación". Set the slides to the number of slides in your deck (for example 5) and minutes to 3. Press "Iniciar". | The Control screen shows "01 / 5", a clock at 00:00 and "Quedan 03:00". The pill says "Calculando ritmo" for the first seconds. The companion shows the session name (made by the watch, like "Presentación 2 nov"), "Diapositiva 1 de 5" and the remaining time counting down. | S, C | `PresentaFlowTest`, `ControlStateFactoryTest`, `SessionFlowTests` |
| 9 | RF 05, RNF 01, AC2 | Press the big **→** button. | The slideshow moves to slide 2 within about 2 seconds. The watch shows "02 / 5". The companion lists "Siguiente enviado". | S, C, V | `KeyTargetingTests`, `CrossStackTest` |
| 10 | RF 06, AC2 | Press **←**. | The slideshow goes back to slide 1. The companion lists "Anterior enviado". | S, C | `KeyTargetingTests`, `CrossStackTest` |
| 11 | RF 05 | Press → until the last slide, then → once more. | The slideshow stops on the last slide; the last press sends no key and nothing new appears in the companion list. | C | `SlideCounterTests`, `KeyTargetingTests` |
| 12 | RF 06 | On slide 1 press ←. | Nothing changes on the PC and nothing new is listed. | C | `SlideCounterTests`, `KeyTargetingTests` |
| 13 | RF 07 | **Correction:** on the watch tap the slide number ("03 / 5"). The screen "Ir a diapositiva" opens. Press + once, then "Listo". | The watch counter changes. **The slideshow does not move.** The companion lists "Contador ajustado a la diapositiva 4". | S, C | `PresentaFlowTest`, `KeyTargetingTests` |
| 14 | RF 05, RF 07 | In the companion switch **off** "Enviar teclas a la presentación". Press → on the watch. | The counter moves but the slideshow does not. The list says "Diapositiva 5 (sin enviar tecla)". Switch it **on** again and check → moves the slideshow again. | C | `KeyTargetingTests` |
| 15 | RF 08, RNF 08 | **Pause:** press and hold the clock on the watch. | "En pausa" appears in amber, the clock freezes and looks dimmed. The companion shows "En pausa". Hold again to resume. | S, C | `PresentaFlowTest`, `SessionLifecycleTests` |
| 16 | RF 10, RF 11, RNF 01, AC5 | **Alert:** start a new presentation of 3 minutes and stay on slide 1 (do not press →). Wait until you are clearly behind schedule (the exact second depends on the plan; section 3 has exact seconds for three plans). | The Control screen switches to the alert layout, for example "Vas atrasado" with "Tienes 4 diapositivas pendientes", an amber pill and "Entendido". **→ and ← still work while the alert is shown.** Press → and check the slideshow moves. Press "Entendido": the normal screen returns. | S | `AlertStatesTest`, `AlertTimingTableTests` |
| 17 | RF 10, RF 09, RNF 08 | Let the time run out. | Near the end "Queda 1 min" (or seconds) appears; after the end "Tiempo agotado" and "Llevas 00:05 de más" in red. "Pasaste 00:05" in red shows on the normal screen after you press "Entendido". | S | `AlertStatesTest`, `AlertTimingTableTests` |
| 18 | RF 08, RF 12 | **Finish:** swipe up on the Control screen. | The question "¿Finalizar presentación?" appears with "Cancelar" and "Finalizar". "Cancelar" returns to Control. | S | `PresentaFlowTest` |
| 19 | RF 12, RF 13, AC6 | Swipe up again and press "Finalizar". | The summary shows "Presentación finalizada", "DURACIÓN REAL", the time, the number of slides, "Ritmo promedio" with the number and "diap/min", and a message such as "Terminaste dentro del tiempo previsto" or "Terminaste 00:42 después del tiempo previsto". The companion lists "Presentación finalizada y guardada". | S, C | `PresentaFlowTest`, `SessionFlowTests`, `SqliteHistoryStoreTests` |
| 20 | RF 12, AC6 | Press "Guardar y volver al inicio". | Home. (The record was already saved by the PC; this button only returns home.) | S | `PresentaFlowTest` |
| 21 | RF 14, AC6 | Press "Historial". | A row "Hoy" with the time and the pace. Tap it to see the same summary with a "Volver" button. | S | `HistoryFlowTest`, `PresentaFlowTest` |
| 22 | RF 14 | For the empty state: close the companion, start it with a new database `dotnet run --project windows-companion/src/Presenta.Companion.App -- --database C:\Temp\empty.db`, pair again with the new code and open "Historial". | "Aún no hay presentaciones guardadas" and a back button ("Volver"). | S | `PresentaFlowTest`, `SqliteHistoryStoreTests` |

### C. When things go wrong

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 23 | RF 15, RNF 08 | During a presentation, press "Detener servidor" in the companion. | The watch keeps showing the Control screen; the status line turns amber with "Reconectando…". The clock keeps counting. Pressing → shows "No se pudo enviar el comando" for a few seconds and does not end the session. The companion window still shows the session. | S, C | `RemotePresentaRepositoryTest`, `PresentaFlowTest` |
| 24 | RF 15, RNF 04 | Press "Iniciar servidor" within 30 seconds. | The watch reconnects by itself, "Conectado" returns, and the counter and time are the same as in the companion window. | S, C | `RemotePresentaRepositoryTest`, `CrossStackTest` |
| 25 | RF 15 | With no presentation running, stop the server and wait more than 30 seconds, then start it and press "Reintentar" when the disconnected screen appears. | It reconnects and Home shows "PC conectado". (With a presentation running the watch retries by itself and the session is restored: row 97.) | S | `RemotePresentaRepositoryTest`, `SessionReconnectionTest` |
| 26 | RF 12, RF 15 | Close the companion window completely while a presentation runs and answer `Sí` to the question (rows 65 and 66 describe it), start the companion again and look at the watch. | The watch cannot connect with the old code: it returns to the pairing screen with "Código incorrecto" (the new run has a new code). Enter the new code. The session that was running was saved as interrupted (row 66). | S | `MainViewModelTests`, `PairingFlowTest` |

### D. Measurements

Read section 4 first: it explains what to read in the logs for each measurement.

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 27 | RNF 03 | **Command speed:** with the slideshow focused, press → on the watch and watch the PC. Do it 10 times. Note the slowest time by eye (or record the screen). Then read the logs (section 4, `T1`). | The slide changes within about 2 seconds each time. Logs: every `roundTripMs` is below 2000. | V, L, T | `TimingCrossStackTest` (loopback, no real keys) |
| 28 | RF 15, RNF 04 | **Loss detection:** with the watch on Home or Control, start the stopwatch at the moment you press "Detener servidor" and stop it when the watch shows "Reconectando…". Repeat 3 times. Then read the logs (section 4, `T2`). | At most 5 seconds. A clean stop is usually noticed at once; the automated tests show a server that goes silent is noticed in at most 3.5 seconds. | L | `RemotePresentaRepositoryTest`, `TimingCrossStackTest` |
| 29 | RNF 04 *(optional)* | **Silent loss:** with Sysinternals `pssuspend`, suspend the companion (`pssuspend Presenta`), start the stopwatch, and stop it when the watch shows "Reconectando…". Resume with `pssuspend -r Presenta`. | At most 5 seconds. | L | `RemotePresentaRepositoryTest` (virtual time) |

### E. Slide control targeting

This section proves that Next and Previous reach the slideshow even when another program (here Android Studio, which holds the emulator) is in front. Use PowerPoint. The companion lists only windows that are visible and have a title. Prepare a `.pptx` with at least 5 slides, each with a large number (1 to 5). Run it windowed first; row 33 repeats the main steps in full screen.

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 30 | RF 05, RF 06, AC2 | Start the companion and note the pairing code. In "Ventana de la presentación" press "Actualizar lista". Choose the entry that starts with "Sugerida:" (the slide-show window). Entries of PowerPoint that are not the slide show end with ": no es la ventana de la presentación en pantalla". If there is no suggested entry, start the slide show first and press "Actualizar lista" again. Then press "Verificar". | The status reads "Lista: la ventana está disponible" after choosing. After "Verificar" the slide-show window comes to the front and the status reads "Verificada: la ventana pasó al frente". No slide change happens (Verificar presses no key). | C | `TargetSelectionViewModelTests`, `PresentationTargetServiceTests`, `RealWindowDeliveryTests` (opt-in) |
| 31 | RF 04, AC3 | Start the `Wear_OS_Large_Round` emulator, run the watch app, and pair it with the code. On the watch choose "Nueva presentación", set **5** slides and **5** minutes, and press "Iniciar". | The watch shows "01 / 5". The companion shows "Presentación iniciada". | S, C | `PresentaFlowTest`, `SessionFlowTests` |
| 32 | RF 05, RF 06, AC2 | **Click inside the emulator** (Android Studio is now in front) and tap **→** on the watch. | PowerPoint comes to the front by itself and shows slide 2. The watch shows "02 / 5". Repeat until slide 5, clicking the emulator before each tap. Then tap **←** twice. At every step the slide shown in PowerPoint equals the number on the watch. | S, V | `RealWindowDeliveryTests` (opt-in), `KeyTargetingTests` |
| 33 | RF 05, RF 06 | Stop the slide show. Start it again in **full screen** (*Slide Show > Set Up Slide Show > Presented by a speaker (full screen)*, then F5). Press "Actualizar lista", choose the suggested entry again, press "Verificar". Start a new 5-slide presentation on the watch and repeat row 32 for a few taps. | The same behavior with the full-screen show. | V | none (needs the real program) |
| 34 | RF 05, RF 06 | **Wrong-application check:** open Notepad and type a few lines of text. Keep PowerPoint's show visible. In the companion press "Actualizar lista" and choose the Notepad window; press "Verificar". Tap → and ← on the watch. | The cursor moves in Notepad (Right and Left arrows) and PowerPoint does **not** advance. Choose the slide-show window again afterwards. | V | `RealWindowDeliveryTests` (opt-in) |
| 35 | RF 15 | **Closed target:** close the slide show (Esc) and tap → on the watch. | The watch shows an error message "Elige la ventana de la presentación en el computador", the counter on the watch does **not** change, and the session keeps running. The companion status line says the window was not found ("No se encuentra la ventana elegida. Actualiza la lista y vuelve a elegirla.") and the activity list says "No se pudo enviar la tecla: la ventana elegida ya no existe". | S, C | `KeyTargetingTests`, `RealWindowDeliveryTests` (opt-in) |
| 36 | RF 05 | Restart the slide show, press "Actualizar lista" in the companion, choose the slide-show window, press "Verificar", and tap → on the watch. | It works again and the counter and slide match. | V | `PresentationTargetServiceTests` |
| 37 | RF 05 | **Minimized target:** minimize the PowerPoint slide-show window (or the whole PowerPoint) and tap → on the watch. | The status line reads "Minimizada: se restaurará al enviar una tecla". The window is restored and advances one slide. | C, V | `RealWindowDeliveryTests` (opt-in) |
| 38 | RF 05, RF 07 | **Keys off:** switch "Enviar teclas a la presentación" off. Tap → on the watch. | The counter changes, nothing happens in PowerPoint, and the companion lists "Diapositiva N (sin enviar tecla)" (N is the new number). With keys off you may even clear the choice: no window is needed ("Las teclas están apagadas: no hace falta elegir una ventana."). Switch the keys on again. | C | `KeyTargetingTests`, `TargetSelectionViewModelTests` |
| 39 | RF 01, RNF 06 | **Restart the companion:** close it, start it again, and look at the "Ventana de la presentación" section (with the slide show still open). | The previous choice is recognized without choosing it again (status "Lista: la ventana está disponible"). If you close the slide show first, the status says the window was not found. | C | `TargetSettingsTests` |
| 40 | RF 04, RF 15 | **Start with no target:** with "Enviar teclas a la presentación" on, close the slide show, so no usable window is chosen, and on the watch try "Iniciar" for a new presentation (finish the running one first). | The presentation does not start, and the watch shows "Elige la ventana de la presentación en el computador". Open the show, choose it, and "Iniciar" works. The companion lists "No se inició: falta elegir la ventana de la presentación". | S, C | `KeyTargetingTests` |
| 41 | RF 15 *(optional)* | **Elevated window:** open PowerPoint *as administrator* and choose its slide-show window. | The status line explains that the window runs as administrator ("La ventana se ejecuta como administrador y Windows bloquea las teclas. Abre Presenta también como administrador."); the watch shows the same message as in row 35 and the activity list says "No se pudo enviar la tecla: la ventana se ejecuta como administrador". Close it and use the normal one. | C | `PresentationTargetServiceTests` |
| 42 | RNF 03 | **Speed:** with Android Studio in front, tap → ten times, noting the slowest delay from the tap to the slide change (stopwatch or screen recording). Then read the logs (section 4, `T1`). | At most about 2 seconds each time. Note the slowest. | V, L, T | none (real key injection) |

If the key does not arrive: read the line in the activity list. It now says why, after "No se pudo enviar la tecla: ": "no hay una ventana elegida", "la ventana elegida ya no existe", "hay varias ventanas iguales", "la ventana se ejecuta como administrador", "Windows no dejó traer la ventana al frente", "la ventana no llegó a quedar al frente" or "Windows no aceptó la tecla". Then press "Verificar". If it shows "Windows no dejó traer la ventana al frente. Haz clic en ella e inténtalo de nuevo.", click the slide-show window once and try again. Write the exact messages in the notes, and if the key still does not arrive follow the diagnostic procedure below (row 86).

### F. Alerts, vibration, screen on, and closing the companion

#### Exact alert times (rows 43 to 56)

The seconds below are not estimates: each one is asserted by `AlertTimingTableTests` (domain, fake clock, one-second tick, default rules). Count from the moment you press "Iniciar" on the watch, and allow about 2 seconds for the one-second tick and the network. Do not tap anything unless the row says so. The color is **amber** for every alert except the last row of each scenario, which is **red** (overtime is the only red state; the ring turns red only then). "Entendido", "Siguiente diapositiva" and "Diapositiva anterior" are on screen and enabled during every alert.

Vibration (device only; on the emulator you confirm it in Logcat, rows 57 to 61): a **warning** is two short pulses; a **critical** is one long pulse then one short one.

Scenario 1: **1 minute, 5 slides**, stay on slide 1. Both time thresholds are 18 s here, so only the critical time alert exists; the warm-up is 6 s and the pace tolerance is 1 slide.

| ID | Covers | Second | What appears on the watch | Color | Pattern | Evidence | Automated |
|---|---|---|---|---|---|---|---|
| 43 | RF 10, AC5 | **16** | "Vas atrasado", then "Tienes 4 diapositivas pendientes" | amber | warning | S | `AlertTimingTableTests`, `AlertsCrossStackTest` |
| 44 | RF 10, AC5 | **42** | "Quedan 18 s", then "Tienes 4 diapositivas pendientes" | amber | critical | S | `AlertTimingTableTests` |
| 45 | RF 10, RF 09, AC5 | **61** | "Tiempo agotado", then "Llevas 00:01 de más" (the number keeps growing) | red | critical | S | `AlertTimingTableTests` |

Scenario 2: **2 minutes, 10 slides**, stay on slide 1. Thresholds 36 s, warm-up 12 s, tolerance 1.

| ID | Covers | Second | What appears | Color | Pattern | Evidence | Automated |
|---|---|---|---|---|---|---|---|
| 46 | RF 10, AC5 | **14** | "Vas atrasado", then "Tienes 9 diapositivas pendientes" | amber | warning | S | `AlertTimingTableTests` |
| 47 | RF 10, AC5 | **84** | "Quedan 36 s", then "Tienes 9 diapositivas pendientes" | amber | critical | S | `AlertTimingTableTests` |
| 48 | RF 10, AC5 | **121** | "Tiempo agotado", then "Llevas 00:01 de más" | red | critical | S | `AlertTimingTableTests` |

Scenario 3: **15 minutes, 20 slides** (the default), stay on slide 1. Running out at 270 s left, critical at 60 s left, warm-up 60 s, tolerance 2.

| ID | Covers | Second | What appears | Color | Pattern | Evidence | Automated |
|---|---|---|---|---|---|---|---|
| 49 | RF 10, AC5 | **95** | "Vas atrasado", then "Tienes 19 diapositivas pendientes" | amber | warning | S | `AlertTimingTableTests`, `AlertsCrossStackTest` |
| 50 | RF 10, AC5 | **630** (10:30) | "Quedan 5 min" (270 s left, shown rounded up), then "Tienes 19 diapositivas pendientes" | amber | warning | S | `AlertTimingTableTests`, `AlertsCrossStackTest` |
| 51 | RF 10, AC5 | **840** (14:00) | "Queda 1 min", then "Tienes 19 diapositivas pendientes" | amber | critical | S | `AlertTimingTableTests`, `AlertsCrossStackTest` |
| 52 | RF 10, AC5 | **901** (15:01) | "Tiempo agotado", then "Llevas 00:01 de más" | red | critical | S | `AlertTimingTableTests`, `AlertsCrossStackTest` |

Rushing (alert "Vas muy rápido", amber, warning pattern, then "Ya estás en la última diapositiva" because you jumped to the last slide). Open the selector by tapping the slide counter, choose the last slide, tap "Listo". The alert appears at the moment you confirm, if you confirm in time:

| ID | Covers | Scenario | Confirm the jump | Result | Evidence | Automated |
|---|---|---|---|---|---|---|
| 53 | RF 10, AC5 | 1 minute, 5 slides: jump to slide 5 | between **6 s** (end of warm-up) and **15 s** (at 16 s "Vas atrasado" would have come first) | "Vas muy rápido" at once | S | `AlertTimingTableTests` |
| 54 | RF 10, AC5 | 2 minutes, 10 slides: jump to slide 10 | between **12 s** and **13 s** (at 14 s "Vas atrasado" comes first) | "Vas muy rápido" at once | S | `AlertTimingTableTests` |
| 55 | RF 10, AC5 | 15 minutes, 20 slides: jump to slide 20 | between **60 s** and **94 s** | "Vas muy rápido" at once | S | `AlertTimingTableTests`, `AlertsCrossStackTest` |

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 56 | RF 10 | In scenario 3 tap "Entendido" the moment "Vas atrasado" appears (95 s). Tap it again each time it comes back. | The same alert comes back **120 s later (215 s)**, and again at **335 s**. An alert you do not acknowledge does not repeat. Tapping "Entendido" never vibrates; a repeated alert vibrates again (it is a new alert). | S, L | `AlertTimingTableTests`, `AlertsCrossStackTest` |

Fast version (30 seconds instead of 15 minutes): close the normal companion, then in PowerShell run `dotnet run --project windows-companion/tools/Presenta.DevHost -- --code 123456 --port 8765 --time-scale 30`. The watch pairs with `123456`. Every time above is divided by 30 (scenario 3: 95 s becomes about 3 s, 630 s about 21 s, 840 s 28 s, 901 s 30 s). This host prints `KEY Right` or `KEY Left` instead of pressing keys. Stop it with Ctrl+C. Use the normal companion for everything else.

#### Vibration through Logcat (rows 57 to 61)

The emulator does not give you anything to feel, so the debug build writes one line per haptic event to Logcat under the tag `PresentaHaptics` (release builds write nothing). Run `& "C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe" logcat -s PresentaHaptics:D` in a PowerShell window.

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 57 | RF 10, AC5 | Start a session and wait for the first alert of a scenario above. | One line, for example `play Warning reason=new-alert kind=BehindSchedule severity=Warning sequence=1`. Exactly one line per alert, not one per second. | L | `HapticsCoordinatorTest`, `AlertsCrossStackTest` |
| 58 | RF 10 | Tap "Entendido". | No new line. | L | `HapticsCoordinatorTest` |
| 59 | RF 10 | Let the next alert arrive (for example the time alert). | `play Critical ...` or `play Warning ...` with the next `sequence`. | L | `HapticsCoordinatorTest` |
| 60 | RF 10 | While an alert is on screen, stop the watch app completely (Stop in Android Studio) and run it again. | The session is still running on the companion and the alert is still active: the log shows `skip alert ... reason=initial-snapshot` and no `play` line (you are looking at the screen, so it does not buzz again). | L | `HapticsCoordinatorTest` |
| 61 | RF 15 | Tap "Siguiente diapositiva" with the slide show closed (keys on), so the command fails. | One line `play CommandFailed reason=command-failed`, the error message on the watch, and in the companion "No se pudo enviar la tecla: la ventana elegida ya no existe". | L, S | `HapticsCoordinatorTest` |

Real vibration (how the pulses feel on a wrist) can only be judged on a physical watch, which this phase cannot connect (ADR-46). Record it as `not verified` in the sign-off (section 11).

#### Screen stays on during a session (rows 62 to 64)

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 62 | RNF 08, AC4 | Start a session and leave the watch alone, without touching it, for longer than the emulator's screen timeout (30 seconds is the Wear default). | The Control screen stays on. | V | `KeepScreenOnTest` |
| 63 | RF 08 | Pause the session and wait the same time. | The screen still stays on while paused. | V | `KeepScreenOnTest` |
| 64 | RNF 08 | Finish the session, tap "Guardar y volver al inicio", and wait the same time on Home. | The screen is allowed to turn off as usual. | V | `KeepScreenOnTest` |

Battery: keeping the display on for a whole talk costs noticeably more battery (ADR-62).

#### Closing the companion during a session (rows 65 to 68)

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 65 | RF 12 | With a session running, close the companion window (the X). | A question "Hay una presentación en curso" with the message "Si cierras Presenta ahora, la presentación se guardará en el historial como interrumpida y el reloj dejará de controlar las diapositivas." and "¿Quieres cerrar y guardar?". Choose `No`: the window stays, the session keeps running and the watch still works. | C | `MainViewModelTests` |
| 66 | RF 12, RF 13 | Close it again and choose `Sí`. | The window closes. The watch shows the summary with "La conexión se perdió antes de terminar". Start the companion again, pair again if the watch asks, and open "Historial" on the watch: there is **one** new record, marked as interrupted. | S | `MainViewModelTests` |
| 67 | RF 12 | With no session (or after finishing one), close the companion. | It closes at once without any question. No new history record. | none | `MainViewModelTests` |
| 68 | RF 12 | Pause a session and close the companion, choosing `Sí`. | The same as row 66 (a paused session also counts). | S | `MainViewModelTests` |

### G. Additional coverage (new rows)

| ID | Covers | Steps | Expected | Evidence | Automated |
|---|---|---|---|---|---|
| 69 | RF 03, AC3 | On Home press "Nueva presentación". Look at the defaults, then press "Menos diapositivas" until it stops, and "Menos minutos" until it stops. Press "Más diapositivas" and "Más minutos" a few times. | The screen reads "Nueva presentación" and "Configura tu exposición" with 20 "diapositivas" and 15 "min" by default. The slides stop at 1 and the minutes at 1; the plus buttons raise them one by one. The limits are 1 to 200 slides and 1 to 180 minutes (the upper limits are covered by tests; reaching them by tapping is optional). | S | `PresentaFlowTest`, `ViewModelTests` |
| 70 | RF 02, RF 04 | With no presentation running, press "Detener servidor" in the companion and wait until the watch Home shows "Sin conexión al computador". | There is no "Nueva presentación" button while disconnected (you cannot start without a connection); "Reintentar" is shown. | S | `PresentaFlowTest`, `HomeScreen` previews |
| 71 | RF 09, AC4 | Start a 5-minute session and start the phone stopwatch at the same moment. After 2 minutes on the phone, read the watch. | The watch clock reads 02:00 within 2 seconds and "Quedan 03:00" within 2 seconds. | S | `ControlStateFactoryTest`, `TimerTests` |
| 72 | RF 11, AC4 | Start 10 slides in 5 minutes and watch the pill under the clock. At the start do nothing; after the alerts that appear, press "Entendido". Then follow the table below. | See the table below the matrix: the pill changes with the pace. | S | `RecommendationTests`, `AlertStatesTest` |
| 73 | RF 12, RF 09 | Start 1 minute, 5 slides. Wait until about 15 seconds after the time ran out, then swipe up and press "Finalizar". | The summary reads "Terminaste 00:15 después del tiempo previsto" (with your overrun time). Compare with `05b-summary-over-time.png`. | S | `PresentaFlowTest` |
| 74 | RF 12, RF 13, RF 14, AC6 | Run a 5-slide session, reach slide 4, finish. Read the summary (time, slides, pace). Go Home, open "Historial", open the new row. | The history summary shows the same duration, slides and pace as the summary right after finishing. The companion's event "Presentación finalizada y guardada" appeared once. | S | `SqliteHistoryStoreTests`, `CrossStackTest` |
| 75 | RF 12, RF 15 *(optional, 6 minutes)* | Start a session. Press "Detener servidor" and wait 5 minutes and a bit more. Then press "Iniciar servidor"; the watch reconnects by itself, without "Reintentar" (ADR-71). | The watch ends up on Home. The companion lists "Presentación interrumpida por pérdida de conexión". In "Historial" there is a new row; opening it shows "La conexión se perdió antes de terminar". | S, C | `ConnectionPolicyTests` |
| 76 | RF 14, RNF 06 | After rows 19 to 21 (a saved record exists), close the companion (no session, so no question), start it again, enter the new code on the watch, and open "Historial". | The earlier record is still there. | S | `SqliteHistoryStoreTests` |
| 77 | RF 14 *(optional, next day)* | The day after a recorded session, open "Historial". | The row from the day before reads "Ayer". | S | `HistoryDatesTest` |
| 78 | RF 14 *(optional, two or more days later)* | Two or more days after a recorded session, open "Historial". | The row reads the day with two digits and the capitalized month (the months are "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"), in the style "08 Oct". | S | `HistoryDatesTest` |
| 79 | RF 14 *(optional)* | Finish a session shortly before midnight, then look at "Historial" before and after midnight. | Before midnight the row reads "Hoy"; after midnight the same row reads "Ayer". | S | `HistoryDatesTest` |
| 80 | RNF 07 | In PowerShell run `netstat -ano \| findstr :8765` with the companion running and "Permitir reloj físico en la red local" off. Then search Logcat for your pairing code: `adb logcat -d \| findstr <the six digits>`. | The server listens on `127.0.0.1:8765` only, and the code does not appear in Logcat. | L | `NetworkConfigTest`, `ConnectionPolicyTests`, `PairingTests` |
| 81 | RNF 07 *(optional)* | Switch "Permitir reloj físico en la red local" on and run the same `netstat` command. Choose Cancel if Windows asks about the firewall. Switch it off again and run it once more. | With the switch on the server also listens on `0.0.0.0:8765`; with it off, only on `127.0.0.1:8765`. | none | `ConnectionPolicyTests` |
| 82 | RNF 02, RNF 08 | **Font scale 1.3:** in the emulator open Settings > Display > Font size and choose the largest size. Walk through Home, "Nueva presentación", Control, an alert, the slide selector, the finish question, the summary and "Historial". Restore the size afterwards. | No text is cut off, buttons can still be pressed, → and ← still work. The app limits the scale on round screens (ADR-32), so text may stay smaller than the system setting. | S | `LayoutAccessibilityTest` |
| 83 | RNF 02 *(optional)* | **Small round screen:** if you have a second AVD with a small round screen (about 192 dp), repeat the walk-through of row 82 and the visual checks V1 to V12 on it. | The same screens fit; the known differences are in ADR-31 and `ui-direction.md` (no home icon, no alert icon, the summary scrolls). | S | `LayoutAccessibilityTest`, `ScreenshotTest` |
| 84 | RNF 05 | **Windows 10:** run the whole companion section on a Windows 10 computer. | Same behavior as Windows 11. If you have no such computer write `Blocked` and the note `not verified`. | none | none |
| 85 | RNF 05 | **Windows 11:** note the Windows version on the computer where you ran the campaign (Settings > System > About). | Windows 11 version recorded. | C | none |
| 86 | RF 05, RF 06, RF 15 | **Diagnostic report:** with the slide show running and its window chosen, press "Verificar", then "Copiar diagnóstico". Open Notepad, paste (Ctrl+V), save the text as `diagnostic.txt`. Read it once and remove file names you do not want to share. | The status line says "Diagnóstico copiado" (the full text is "Diagnóstico copiado. Revisa que no incluya nombres de archivos que no quieras compartir y pégalo en un archivo de texto."). The pasted text starts with `Presenta window diagnostic report`, lists the target, the foreground before and after, every step with its Win32 result, and ends with the line `A key WOULD be sent` or `NO key would be sent` and the reason. **No slide changes and no key is pressed.** The report has no pairing code. | C, file | `ForegroundRuleAndReportTests`, `TargetSelectionViewModelTests` |
| 87 | RF 15 | **Failure reason:** close the slide show and tap → on the watch (as in row 35), then open the slide show and choose it again. | The activity list shows "No se pudo enviar la tecla: la ventana elegida ya no existe" for the closed window. Every other failure shows its own reason (row 35 and the troubleshooting list). | C | `KeyTargetingTests`, `MainViewModelTests` |
| 88 | RF 05, RF 06 | **Other PowerPoint window:** with the show running, press "Actualizar lista" and look at the PowerPoint entries. Choose one that ends with ": no es la ventana de la presentación en pantalla" (the editing window, or a window whose class is not the slide show), press "Verificar", then tap → on the watch. | If the slide changes, the program handed the focus to the show window it owns and the key reached it (the rule of ADR-66). If it does not, the activity line gives the reason: run row 86 and send the report. In both cases choosing the entry that starts with "Sugerida:" works as in row 32. | C, file | `RealWindowDeliveryTests` (opt-in) |
| 89 | RF 05, RF 06 | **Only slide show chosen by itself:** start the PowerPoint slide show (no other slide show open), then open the companion and look at "Ventana de la presentación". | The entry that starts with "Sugerida:" is already selected and the status line reads "Se eligió sola la única presentación abierta. Pulsa Verificar para comprobarla." The window did not come to the front and nothing was sent. Pressing "Verificar" then brings it forward as in row 32. | C | `AutomaticTargetTests` |
| 90 | RF 05, RF 06 | **Two slide shows:** open two PowerPoint slide shows and press "Actualizar lista". | Nothing is selected and the status line reads "Hay varias presentaciones abiertas. Elige la que vas a mostrar." After you choose one by hand it reads as in row 31. | C | `AutomaticTargetTests` |
| 91 | RF 05, RF 06 | **Warning for the other PowerPoint window:** choose the PowerPoint entry that ends with ": no es la ventana de la presentación en pantalla". Then choose Notepad. | With the PowerPoint editing window chosen, an amber line under the status reads "Esta no es la ventana de la presentación en pantalla. Inicia la presentación y elige la entrada que empieza por Sugerida." With Notepad (or a browser) chosen there is no such line. | C | `AutomaticTargetTests` |
| 92 | RF 05, RF 06 | **Choosing does not verify:** with another window in front, click an entry in the list, then press "Actualizar lista". | Clicking an entry does not bring the window forward; only "Verificar" does. After refreshing, the entry you chose is still selected. | C | `AutomaticTargetTests` |
| 93 | RF 12, RF 13, RNF 06 | **Crash recovery:** start a session of 10 slides, tap → twice, and in Task Manager end the task "Presenta" (only that one). Open the companion again. Then on the watch open "Historial". Close and open the companion once more. | The activity list shows "Se guardó una presentación interrumpida" once and no session is running. "Historial" has one new row with the same name, slide 3 of 10 and "La conexión se perdió antes de terminar"; its time is at most a few seconds short of what really passed. After the second restart there is no second row and no such line. | C, S | `ActiveSessionTests`, `RememberedCodeCrossStackTest` |
| 94 | RF 01, RNF 06, RNF 07 | **Remembered code:** turn on "Recordar código de vinculación" and note the code. Close the companion (no session) and open it again. | The code is the same, and the paired watch comes back by itself (no pairing screen). The explanation under the switch reads "Si está apagado, el código cambia cada vez que abres Presenta y hay que escribirlo otra vez en el reloj". | C | `RememberedPairingCodeTests`, `RememberedCodeCrossStackTest` |
| 95 | RF 01, RNF 07 | **Regenerate while remembering:** with the switch on, press "Generar código nuevo", close and open the companion. | The new code is shown after the restart (not the old one). The watch, which still has the old code, shows the pairing screen "Vincular"; entering the new code connects it. | C | `RememberedPairingCodeTests` |
| 96 | RF 01, RNF 07 | **Off by default:** turn the switch off, close and open the companion. | The code is different after the restart and the watch asks for it ("Vincular"). | C | `RememberedPairingCodeTests` |
| 97 | RF 02, RF 15 | **No give-up during a session:** start a session, press "Detener servidor" and wait two minutes. Press "Iniciar servidor". | During the whole wait the watch shows the session with "Reconectando…" and the clock keeps running; it never shows "Sin conexión al computador" and no "Reintentar" is needed. Tapping → meanwhile does nothing. After "Iniciar servidor" the watch is "Conectado" again by itself with the counter and time of the companion. | S, C | `SessionReconnectionTest` |
| 98 | RF 02, RF 15 | **Rejected code during a session:** start a session, press "Detener servidor", press "Generar código nuevo", then "Iniciar servidor". | The watch shows the pairing screen "Vincular"; after entering the new code it returns to the Control screen of the same session. | S, C | `SessionReconnectionTest` |
| 99 | RF 01 | **Single instance:** with the companion open, start `Presenta.exe` a second time. | A message "Presenta ya está abierto. Búscalo en la barra de tareas." appears and the second one closes without opening a window; the first keeps working (its code does not change). | C | `SingleInstanceProcessTests`, `Phase8WindowTests` |
| 100 | RF 11, RNF 07 | **AI off by default:** start the companion normally, without setting any `PRESENTA_AI_*` variable, and look at the card "Recomendaciones con IA". Then run an alert scenario (rows 43 to 56). | The switch is disabled and the line reads "No disponible: falta configurar la IA en este computador. Los pasos están en el README de windows-companion." The privacy line reads "Solo se envían cifras de la presentación, nunca nombres ni archivos." The watch shows the usual wording ("Acelera ahora" and the like) in the alert. | C, S | `AiSectionViewModelTests`, `AiRecommendationTests` |
| 101 | RF 11, RNF 07 | **No key:** in PowerShell set only `PRESENTA_AI_BASE_URL` and `PRESENTA_AI_MODEL` (not the key), start the companion in that window. | The same as row 100: the switch is disabled and the line says it is not available. Nothing is sent anywhere. | C | `ConfigurationTests`, `EnricherTests` |
| 102 | RF 11, RNF 07 | **Configured, switch off, then on:** set the three variables with your own values (see `windows-companion/README.md`), start the companion, and turn on "Usar IA para redactar las recomendaciones". | Before turning it on the line reads "Apagada: se usan las frases de siempre." After, "Lista: se pedirá un texto cuando salte una alerta." Turning it off returns to the first. The switch is off again each time you start the companion. | C | `AiSectionViewModelTests` |
| 103 | RF 11 *(needs a real provider and a key)* | **Valid key with an alert scenario:** with the switch on, start the 15-minute session and stand still on slide 1 until the first alert (the fast version of row 43 works too). | The line passes through "Pidiendo un texto a la IA…" and shows "Último texto:" followed by one short sentence in Spanish. On the watch, the alert pill shows that sentence (two lines at most, then "…") instead of the usual wording; the pace pill under the clock keeps its usual wording; the alerts come at the same seconds as with the AI off. This was **not** run against a real provider by automation. | C, S | `AiRecommendationTests`, `AiTipLayoutTest`, `AiTextCrossStackTest` (fake client) |
| 104 | RF 11, RF 15 | **Invalid key:** set a wrong key, turn the switch on and cause an alert. | The line reads "La IA rechazó la clave: se usan las frases de siempre." The watch shows the usual wording and the presentation is not affected. Turning the switch off and on tries again. | C, S | `EnricherTests` |
| 105 | RF 11, RF 15 | **Offline:** with a valid configuration, turn off the computer's network (or point the address at a closed port on this computer) and cause alerts. | After three failures the line reads "En pausa por errores: se usan las frases de siempre."; the watch shows the usual wording; commands and the clock are not delayed. After about a minute (or turning the switch off and on) it tries again. | C, S | `EnricherTests`, `AiRecommendationTests` |
| 106 | RNF 07 | **What is sent:** follow the part named Check it yourself in `windows-companion/README.md` (a listener on `127.0.0.1:9099`) and cause an alert. | The body has the fixed instruction and one `Situation:` line with codes and rounded numbers. It contains no presentation name, no window title, no file name, no computer or user name. The `Authorization` header is the only place with your key, and it is not in any file, in the window, or in a diagnostic report ("Copiar diagnóstico"). | C | `ClientTests`, `SecretsTests`, `PromptTests` |
| 107 | RF 11, RNF 02 | **Watch display and fallback (no provider needed):** run `dotnet run --project windows-companion/tools/Presenta.DevHost -- --code 123456 --port 8765 --time-scale 30 --fake-ai` instead of the normal companion, pair the watch, start a session and wait for the first alert. Try the large font size too (row 82). | The alert pill shows `Fake AI tip: ...` (two lines at most, inside the round screen, with Next and Previous still easy to press). Stopping the host and starting it without `--fake-ai` shows the usual wording. | S, V | `AiTipLayoutTest`, `AiTextCrossStackTest` |

Row 72 detail (the pill under the clock follows the pace; all times are approximate, from the rules, within about 3 seconds; the exact times for the three scenarios above are asserted by tests, these are not):

| After | You did | The pill reads |
|---|---|---|
| start | nothing | "Calculando ritmo" (about the first 30 seconds) |
| about 30 s | pressed → about once every 30 seconds | "Buen ritmo" |
| about 34 s | stayed on slide 1; the alert "Vas atrasado" appeared; you pressed "Entendido" | "Acelera ligeramente" |
| about 67 s | still on slide 1, after "Entendido" | "Acelera ahora" |
| about 40 s (a new session) | jumped to slide 8; the alert "Vas muy rápido" appeared; "Entendido" | "Reduce el ritmo" |
| about 240 s (1 minute left) | after "Entendido" of the time alert | "Ve cerrando" |
| after 300 s | after "Entendido" of "Tiempo agotado" | "Termina cuanto antes" |

## 3. Alert scenario table in one place

The alert rows 43 to 56 above are the table. In short: plan 1 min / 5 slides: 16, 42, 61 s. Plan 2 min / 10 slides: 14, 84, 121 s. Plan 15 min / 20 slides: 95, 630, 840, 901 s. Repeat of an acknowledged pace alert: every 120 s. These numbers come from `AlertTimingTableTests`; if that test changes, change rows 43 to 56 with it.

## 4. Timing measurements: what to read and where

The objective measurements exist only in **debug** builds. Release builds contain none of them (tests: `TimingDiagnosticsTest` for the watch, the build-configuration test in `KeyTargetingTests` for the companion).

### T1. Command speed (RNF 03, rows 9, 27, 42)

RNF 03 asks that a command is reflected on the PC within about 2 seconds on a stable network. Two logs give the objective part:

- **Watch** (Logcat, tag `PresentaTiming`): `adb logcat -d -s PresentaTiming:D`. Each slide command writes one line, for example `command type=slide.next roundTripMs=42 ok=true`. `roundTripMs` is the time from sending the command to receiving its result. The companion presses the key first and answers afterwards (ADR-58), so this number includes the activation of the PowerPoint window and the key press.
- **Companion** (file `%LOCALAPPDATA%\Presenta\timing-debug.log`, written only by the debug build): each key writes a line such as `2026-11-08T12:00:00.0000000+00:00 PresentaTiming key=Next activateAndInjectMs=30`. `activateAndInjectMs` is the time spent bringing the chosen window to the front and injecting the key. Open it with `notepad "$env:LOCALAPPDATA\Presenta\timing-debug.log"`.

**What is not measured:** the time PowerPoint takes to repaint the new slide after it receives the key. Add that by eye: record the screen or use the phone's slow-motion video, and note the time from the tap to the visible slide change. The requirement is met when the largest `roundTripMs` plus your visual delay is about 2 seconds or less. Write the largest `roundTripMs`, the largest `activateAndInjectMs`, and your visual delay in the results sheet (measured column) for row 27.

### T2. Loss detection (RNF 04, rows 6, 28, 29)

RNF 04 asks that the connection state is updated within 5 seconds. The watch writes one line when it declares the connection lost, for example `connection-lost phase=Online sinceLastFrameMs=9 reconnectingAfterMs=0`:

- `sinceLastFrameMs` is the time between the last frame received from the companion and the moment the loss was declared. A clean stop closes the socket, so it is a few milliseconds; a silent companion (row 29) gives about 3000 to 3500 ms (the silence limit is 3 s, checked every 0.5 s).
- `reconnectingAfterMs` is the time from that moment to the state "Reconectando…" (it is 0: both happen in the same step).

Your stopwatch time (from pressing "Detener servidor" to seeing "Reconectando…") is the user-visible number; the log is the objective one. Both must be 5 seconds or less. Write both in the results sheet for row 28.

### Reference values from the automated cross-stack tests

`scripts/verify-all.ps1 -CrossStack` prints and records these in `automated-run-latest.md`: the round trip of 100 slide commands over loopback against the developer host (median and 95th percentile; the host prints keys, it presses none), and the time the Kotlin client needs to declare the loss after the host process is killed. Typical values on the development computer were a median below 1 ms, a 95th percentile below 1 ms, and a loss declared within 10 ms. Your manual numbers will be larger because they include the real key injection and the emulator.

## 5. Visual review checklist (screens that still await your approval)

Compare the real emulator against the pictures in [../ui-previews/large-round/](../ui-previews/large-round/) and the mockup sheet [../mockups/](../mockups/) (`presenta-screens (1).png`). Mockup screens are marked *mockup*; the others are derived screens you have not approved yet. For each screen note whether it matches, and answer `Approve` or `Change` with a reason. Take one `S` screenshot per screen.

| ID | Screen | Preview picture | How to reach it | Look for |
|---|---|---|---|---|
| V1 | Pairing (derived) | `10-pairing.png`, `10b-pairing-wrong-code.png`, `10c-pairing-locked-out.png`, `10d-pairing-connecting.png` | First launch, or a wrong code, or "Conectando…" while connecting. | "Vincular" and "Escribe el código del computador"; six digit boxes; the selected digit is clear; buttons readable; amber errors. |
| V2 | Paused (derived) | `03c-control-paused.png` | Hold the clock on Control. | "En pausa" in amber; dimmed clock; → and ← visible. |
| V3 | Overtime (derived) | `03d-control-overtime.png`, `04c-control-alert-overtime.png` | Let a short session run out. | "Tiempo agotado" and "Llevas 00:05 de más" in red; ring full and red; "Pasaste 00:05" after "Entendido". |
| V4 | Reconnecting (derived) | `03e-control-reconnecting.png`, `07b-home-reconnecting.png` | Press "Detener servidor" during a session and on Home. | "Reconectando…" in amber; controls still visible; clock keeps counting. |
| V5 | Slide selector (derived) | `08-slide-selector.png` | Tap the slide counter on Control. | "Ir a diapositiva", the number, "Siguiente número" and "Número anterior" buttons, "Listo". |
| V6 | Finish confirmation (derived) | `09-finish-confirmation.png` | Swipe up on Control. | "¿Finalizar presentación?", "Cancelar", "Finalizar". |
| V7 | Read-only summary (derived) | `05d-summary-read-only.png`, `05c-summary-interrupted.png` | Open a row in "Historial". | Same data as the summary, with "Volver" instead of "Guardar y volver al inicio". |
| V8 | Home (mockup 01) | `01-home-connected.png` | After pairing. | "Presenta", "PC conectado", "Nueva presentación", "Historial". |
| V9 | Setup (mockup 02) | `02-setup.png`, `02b-setup-limits.png` | "Nueva presentación". | "Configura tu exposición", the two steppers, "Iniciar". |
| V10 | Control and alert (mockups 03 and 04) | `03-control-normal.png`, `03b-control-calculating.png`, `04-control-alert.png`, `04b-control-alert-behind.png` | During a session, and when an alert appears. | Counter, clock, "Quedan 03:00", pace pill; alert in amber with "Entendido" and the arrows still visible. |
| V11 | Summary (mockup 05) | `05-summary.png`, `05b-summary-over-time.png` | Finish a session. | "Presentación finalizada", "DURACIÓN REAL", the time, slides, pace, the message. |
| V12 | History (mockups 06 and 06b) | `06-history.png`, `06b-history-empty.png` | "Historial" with and without records. | Rows "Hoy" / "Ayer" / "08 Oct" style, time, pace; the empty message "Aún no hay presentaciones guardadas". |

Also remember: the previews come from Robolectric, so anti-aliasing and the typeface can differ slightly from the real emulator. The known differences from the mockup sheet are listed in [../ui-direction.md](../ui-direction.md).

## 6. Platform coverage (RNF 05)

Windows 11 is verified by running the campaign on it (row 85). **Windows 10 is not verified** unless you run row 84 on such a computer. The companion uses only documented Windows calls that exist in Windows 10, but nothing was run there.

## 7. Requirements coverage

Every requirement maps to rows of this guide. A requirement is validated when all its non-optional rows pass.

| Requirement | Rows |
|---|---|
| RF 01 Connect watch and Windows app | 1, 2, 3, 4, 5, 39, 94, 95, 96, 99 |
| RF 02 Visible connection state | 1, 4, 6, 7, 70, 97, 98, V4 |
| RF 03 Configure slides and duration | 8, 69, 31, V9 |
| RF 04 Start once connected | 8, 31, 40, 70 |
| RF 05 Next slide | 9, 11, 14, 30, 32, 33, 34, 36, 37, 38, 86, 88, 89, 90, 91, 92 |
| RF 06 Previous slide | 10, 12, 30, 32, 33, 34, 86, 88, 89, 90, 91, 92 |
| RF 07 Slide counter and correction | 13, 14, 38 |
| RF 08 Start, pause, finish | 8, 15, 18, 19, 63 |
| RF 09 Elapsed and remaining time | 8, 17, 45, 71, 73 |
| RF 10 Alerts and haptics | 16, 17, 43 to 61 |
| RF 11 Pace and recommendation | 8, 16, 72, 100 to 105, 107, V10 |
| RF 12 Finish and summary | 18, 19, 20, 26, 65 to 68, 73, 74, 75, 93, V11 |
| RF 13 Stored data | 19, 66, 74, 93 |
| RF 14 History | 21, 22, 74, 75, 76, 77, 78, 79, V12 |
| RF 15 Clear messages, session not closed | 6, 23, 24, 25, 26, 28, 35, 40, 41, 61, 75, 86, 87, 97, 98, 104, 105 |
| RNF 01 Controls without navigation | 9, 16 |
| RNF 02 Short text, contrast, round screen | 82, 83, 107, V1 to V12 |
| RNF 03 Within about 2 s | 27, 42, T1 |
| RNF 04 Within 5 s | 6, 28, 29, T2 |
| RNF 05 Windows 10 and 11 | 84, 85 |
| RNF 06 History persists | 39, 76, 93, 94 |
| RNF 07 LAN only, nothing sensitive | 2, 3, 80, 81, 94, 95, 96, 100, 101, 102, 106 |
| RNF 08 Consistent states | 15, 17, 23, 62, 64, 82, V2 to V4 |

## 8. The seven acceptance criteria of the prototype

| Code | Criterion | Rows |
|---|---|---|
| AC1 | From the emulator the watch connects to the Windows app and shows the connection state. | 1, 4, 6, 7 |
| AC2 | Next and previous on the watch send the matching key to the open presentation. | 9, 10, 30, 32 |
| AC3 | The user can configure duration and number of slides before starting. | 8, 31, 69 |
| AC4 | The control screen clearly shows counter, elapsed time, remaining time and a pace message. | 8, 62, 71, 72 |
| AC5 | On a critical state change a visual alert is shown and a haptic response is attempted on a compatible device. | 16, 43 to 61 (the haptic attempt is proven through the Logcat lines; the feel needs a physical watch) |
| AC6 | On finishing, the record is saved to history and a session summary is shown. | 19, 20, 21, 74 |
| AC7 | The main screens can be walked through and visually validated in the Wear OS emulator. | V1 to V12 |

## 9. Results sheet

Fill one line per test. Leave `Skip` only for optional rows. Put numbers in the measured column where the row asks for them (rows 27, 28, 29, 42, 71).

| ID | Pass / Fail / Blocked / Skip | Notes (what you saw if it was not as expected) | Measured |
|---|---|---|---|
| 1 | | | |
| 2 | | | |
| 3 | | | |
| 4 | | | |
| 5 | | | |
| 6 | | | |
| 7 | | | |
| 8 | | | |
| 9 | | | |
| 10 | | | |
| 11 | | | |
| 12 | | | |
| 13 | | | |
| 14 | | | |
| 15 | | | |
| 16 | | | |
| 17 | | | |
| 18 | | | |
| 19 | | | |
| 20 | | | |
| 21 | | | |
| 22 | | | |
| 23 | | | |
| 24 | | | |
| 25 | | | |
| 26 | | | |
| 27 | | | slowest roundTripMs / activateAndInjectMs / visual delay: |
| 28 | | | stopwatch s / sinceLastFrameMs: |
| 29 | | | stopwatch s: |
| 30 | | | |
| 31 | | | |
| 32 | | | |
| 33 | | | |
| 34 | | | |
| 35 | | | |
| 36 | | | |
| 37 | | | |
| 38 | | | |
| 39 | | | |
| 40 | | | |
| 41 | | | |
| 42 | | | slowest roundTripMs / visual delay: |
| 43 | | | |
| 44 | | | |
| 45 | | | |
| 46 | | | |
| 47 | | | |
| 48 | | | |
| 49 | | | |
| 50 | | | |
| 51 | | | |
| 52 | | | |
| 53 | | | |
| 54 | | | |
| 55 | | | |
| 56 | | | |
| 57 | | | |
| 58 | | | |
| 59 | | | |
| 60 | | | |
| 61 | | | |
| 62 | | | |
| 63 | | | |
| 64 | | | |
| 65 | | | |
| 66 | | | |
| 67 | | | |
| 68 | | | |
| 69 | | | |
| 70 | | | |
| 71 | | | watch minus phone, s: |
| 72 | | | |
| 73 | | | |
| 74 | | | |
| 75 | | | |
| 76 | | | |
| 77 | | | |
| 78 | | | |
| 79 | | | |
| 80 | | | |
| 81 | | | |
| 82 | | | |
| 83 | | | |
| 84 | | | |
| 85 | | | Windows version: |
| 86 | | | attach the diagnostic file: |
| 87 | | | |
| 88 | | | |
| 89 | | | |
| 90 | | | |
| 91 | | | |
| 92 | | | |
| 93 | | | |
| 94 | | | |
| 95 | | | |
| 96 | | | |
| 97 | | | |
| 98 | | | |
| 99 | | | |
| 100 | | | |
| 101 | | | |
| 102 | | | |
| 103 | | | |
| 104 | | | |
| 105 | | | |
| 106 | | | |
| 107 | | | |

Visual review (section 5):

| ID | Matches the preview? (Yes / No) | Approve / Change | Reason or requested change |
|---|---|---|---|
| V1 | | | |
| V2 | | | |
| V3 | | | |
| V4 | | | |
| V5 | | | |
| V6 | | | |
| V7 | | | |
| V8 | | | |
| V9 | | | |
| V10 | | | |
| V11 | | | |
| V12 | | | |

Other notes (texts that read badly in Spanish, anything confusing):

&nbsp;

## 10. Defects log

Copy one block per problem. Number them `D-01`, `D-02`, and so on. Severity: **Critical** (the prototype cannot be used, a wrong slide is shown, a crash, data lost), **Major** (a main feature does not work as expected but there is a way around it), **Minor** (small, the feature works), **Cosmetic** (looks or wording only).

| Field | Value |
|---|---|
| ID | D-01 |
| Severity | |
| Test ID | |
| Steps to reproduce | |
| Expected | |
| Actual | |
| Evidence file | |
| Watch app (debug build date) | |
| Companion (debug, date) | |
| Emulator / Android version | |
| Windows version | |
| Slideshow program and version | |

## 11. Sign-off

The prototype is **accepted** when all of the following hold. Write `Yes` or `No` for each.

| # | Pass criterion | Yes / No |
|---|---|---|
| 1 | Every non-optional row of sections A to G is `Pass` (`Blocked` rows are explained and none concerns a main feature). | |
| 2 | Rows 1 to 4, 8 to 10, 19 to 21 and 30 to 32 (connect, control a real slideshow, save and list) are `Pass` with PowerPoint. | |
| 3 | RNF 03: the largest `roundTripMs` plus the visual delay is about 2 seconds or less (rows 27 and 42). | |
| 4 | RNF 04: the loss is shown within 5 seconds, by stopwatch and by `sinceLastFrameMs` (row 28). | |
| 5 | All alert rows 43 to 55 appear within about 2 seconds of the stated second, amber except overtime, and red only for overtime. | |
| 6 | The Logcat rows 57 to 61 show one `play` line per alert and none for acknowledgments. | |
| 7 | The companion closing question works (rows 65 to 68). | |
| 8 | The visual review V1 to V12 is approved, or every requested change is recorded in the defects log. | |
| 9 | No open defect of severity Critical or Major. | |
| 10 | The acceptance criteria AC1 to AC7 each have their rows passing. | |

Known and accepted limits (write the decision): real vibration on a physical watch is not verified; a physical watch on a Wi-Fi address is not supported yet (ADR-46); Windows 10 is not verified unless row 84 passed.

| Field | Value |
|---|---|
| Name | |
| Date | |
| Result | Accepted / Not accepted |
| .NET SDK version (P2) | |
| Companion build | Debug, built on: |
| Watch app build | Debug, built on: |
| Automated run attached (`automated-run-latest.md` timestamp) | |

## 12. How to send results back

Send these, in one message or one folder:

1. This guide's **results sheet** (section 9) and **visual review** table (section 5), filled in. Copy the tables as text, or send the whole file.
2. The **defects log** blocks (section 10) for every `Fail`.
3. The **sign-off** table (section 11).
4. Evidence files named with the test ID, and the diagnostic text of row 86 whenever a key did not reach PowerPoint. At least: `S` screenshots for V1 to V12; `S` or `C` for every `Fail` or `Blocked` row; the Logcat file `logcat.txt` from P14 (taken after rows 27, 28 and 57 to 61); the companion timing file `timing-debug.log` (copy it from `%LOCALAPPDATA%\Presenta\`) after rows 27 and 42; a short video of row 32 or 42 if you can.
5. `docs/validation/automated-run-latest.md` from P1.
6. Anything unexpected that is not a test row, in your own words.

Do not send the pairing code of a computer you use for other things; the code only lives until the companion closes.
