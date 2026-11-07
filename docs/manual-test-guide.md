# Manual test guide: first run on the emulator

This guide walks you through the first manual test of Presenta: the Wear OS app on the emulator controlling a slideshow on your Windows PC. You do not need to know how the code works. Follow the steps in order, compare what you see with the "Expected" column, and write what happened in the results table at the end.

Everything the automated tests could prove has already been proven (see `docs/testing-strategy.md`). This guide covers what only a person can check: real key presses in a real slideshow, how the screens look and feel, and how fast things happen.

## 1. What you need

| Item | Notes |
|---|---|
| This repository | Folder `C:\Users\ASUS\Desktop\Reloj\Presenta`. |
| .NET 10 SDK | Already installed (checked in `docs/environment.md`). |
| Android Studio | Already installed, with the AVD `Wear_OS_Large_Round`. |
| A slideshow program | PowerPoint, LibreOffice Impress, Google Slides in a browser, or a PDF viewer that moves with the arrow keys. Any program where Right arrow goes to the next slide and Left arrow goes back. |
| A test deck | 5 to 8 slides, each one clearly different (a big number on each slide works well). |
| A stopwatch | Your phone is fine. |
| About 30 minutes | |

Before you start, close other presentations so the keys cannot go to the wrong place.

## 2. Start the companion on the PC

1. Open PowerShell in the repository folder:
   ```
   cd C:\Users\ASUS\Desktop\Reloj\Presenta
   dotnet run --project windows-companion/src/Presenta.Companion.App
   ```
   The first run takes a minute to build.
2. A dark window titled **Presenta** opens. The server starts by itself.
3. Write down the **six-digit code** shown in large cyan numbers. This is the pairing code. It changes every time the companion starts (or when you press "Generar código nuevo").
4. Check the window shows:
   - "Servidor activo" at the top right,
   - "Puerto 8765",
   - the line "Emulador del reloj: ws://10.0.2.2:8765",
   - the watch status "Esperando al reloj",
   - "Sin presentación activa",
   - the section "Ventana de la presentación" with a list of windows, "Actualizar lista", "Verificar" and the status "Ninguna ventana elegida",
   - both switches: "Permitir reloj físico en la red local" off and "Enviar teclas a la presentación" on.
5. Leave this window open for the whole test. Do not close it until the end.

## 3. Start the emulator

1. Open Android Studio.
2. Open **Device Manager** (the phone icon on the right side, or *View > Tool Windows > Device Manager*).
3. Find **Wear OS Large Round** and press the play triangle to start it. Wait until you see the watch face. The first start can take a few minutes.

## 4. Run the watch app

1. In Android Studio choose *File > Open* and select the **`wear-app`** folder (not the repository root). Wait for "Gradle sync" to finish (progress bar at the bottom). The first sync downloads files and can take several minutes.
2. At the top, check that the selected device is **Wear OS Large Round** and the run configuration is **app**.
3. Press the green **Run** triangle. The app installs and opens on the emulator.

If you want to preview the screens without the PC, open `wear-app/app/src/main/kotlin/com/presenta/wear/ui/preview/Previews.kt` and use the Compose preview panel; pictures of every screen are also in `docs/ui-previews/`.

## 5. What you should see at each step

| Step | Watch screen | Main texts |
|---|---|---|
| First launch (no code stored) | Pairing | "Vincular", "Escribe el código del computador", six digit boxes, a big digit between − and +, buttons "←" and "Siguiente" |
| Entering the last digit | Pairing | the button now says "Conectar" |
| After connecting | Home | "Presenta", pill "PC conectado", buttons "Nueva presentación" and "Historial" |
| Looking for the PC | Home / Control | "Reconectando…" |
| PC not reachable for 30 s | Disconnected | "Sin conexión al computador", "Verifica que la aplicación de Windows esté abierta", button "Reintentar" |
| Setup | Setup | "Nueva presentación", "Configura tu exposición", two steppers (diapositivas, min), "Iniciar" |
| During a presentation | Control | "Conectado", "01 / 8", the clock, "Quedan 03:00", a pace pill, ← and → buttons, a ring that fills with elapsed time |
| Alert | Control | for example "Quedan 1 min" or "Vas atrasado", an amber pill, "Entendido" (← and → still work) |
| Finished | Summary | "Presentación finalizada", "DURACIÓN REAL", the time, the slides, the pace, "Guardar y volver al inicio" |
| History | History | "Historial", rows such as "Hoy" with time and pace; tap a row for its summary and "Volver" |

## 6. Checklist

Mark each row Pass, Fail, or Skip in the results table at the end (section 8). The numbers match.

### A. Pairing and connection

| # | Do this | Expected |
|---|---|---|
| 1 | Run the app for the first time. | The pairing screen "Vincular" appears with 000000 and the first digit highlighted. |
| 2 | Enter a **wrong** code, for example 000000: press "Siguiente" five times, then "Conectar". | The watch stays on the pairing screen and shows "Código incorrecto" in amber. The companion window lists "Código de vinculación incorrecto". |
| 3 | Try wrong codes four more times (five wrong attempts in total), then once more. | The watch shows "Demasiados intentos. Espera un momento". The companion lists "Demasiados intentos: conexión bloqueada por un momento". After about 30 seconds the right code works again. |
| 4 | Enter the **right** code from the companion window. Use + and − to set each digit (9 wraps to 0), "Siguiente" to move on, "←" to go back one digit. Turn the emulator's crown (or use its rotary control) to change the digit too. On the sixth digit press "Conectar". | The watch shows Home with the pill "PC conectado". The companion shows "Reloj conectado" and the event "Reloj conectado". |
| 5 | Close the app on the emulator and open it again. | It goes straight to Home without asking for the code (the code is remembered). |
| 6 | In the companion window press **Detener servidor**. | Within about 4 seconds the watch shows "Reconectando…" and after about 30 seconds "Sin conexión al computador" with "Reintentar". |
| 7 | Press **Iniciar servidor** in the companion, then "Reintentar" on the watch. | The watch returns to Home with "PC conectado". |

### B. Running a presentation

Before this section: open your test deck, start the slideshow (usually **F5**), and choose that window in the companion's "Ventana de la presentación" section (section E, rows 30 and 31). The companion brings that window to the front by itself before each key, so you may click the emulator freely. Keys are sent to the chosen window only.

| # | Do this | Expected |
|---|---|---|
| 8 | On the watch: "Nueva presentación". Set the slides to the number of slides in your deck (for example 5) and minutes to 3. Press "Iniciar". | The Control screen shows "01 / 5", a clock at 00:00 and "Quedan 03:00". The pill says "Calculando ritmo" for the first seconds. The companion shows the session name (made by the watch, like "Presentación 2 nov"), "Diapositiva 1 de 5" and the remaining time counting down. |
| 9 | Press the big **→** button. | The slideshow moves to slide 2 within about 2 seconds. The watch shows "02 / 5". The companion lists "Siguiente enviado". |
| 10 | Press **←**. | The slideshow goes back to slide 1. The companion lists "Anterior enviado". |
| 11 | Press → until the last slide, then → once more. | The slideshow stops on the last slide; the last press sends no key and nothing new appears in the companion list. |
| 12 | On slide 1 press ← . | Nothing changes on the PC and nothing new is listed. |
| 13 | **Correction:** on the watch tap the slide number ("03 / 5"). The screen "Ir a diapositiva" opens. Press + once, then "Listo". | The watch counter changes. **The slideshow does not move.** The companion lists "Contador ajustado a la diapositiva 4". |
| 14 | In the companion switch **off** "Enviar teclas a la presentación". Press → on the watch. | The counter moves but the slideshow does not. The list says "Diapositiva 5 (sin enviar tecla)". Switch it **on** again and check → moves the slideshow again. |
| 15 | **Pause:** press and hold the clock on the watch. | "En pausa" appears in amber, the clock freezes and looks dimmed. The companion shows "En pausa". Hold again to resume. |
| 16 | **Alert:** start a new presentation of 3 minutes and stay on slide 1 (do not press →). Wait about one minute: the alert appears once you are clearly behind schedule, and the exact moment depends on the number of slides. | The Control screen switches to the alert layout, for example "Vas atrasado" with "Tienes 4 diapositivas pendientes", an amber pill and "Entendido". **→ and ← still work while the alert is shown.** Press → and check the slideshow moves. Press "Entendido": the normal screen returns. |
| 17 | Let the time run out. | Near the end "Queda 1 min" (or seconds) appears; after the end "Tiempo agotado" and "Llevas 00:05 de más" in red. "Pasaste 00:05" in red shows on the normal screen. |
| 18 | **Finish:** swipe up on the Control screen. | The question "¿Finalizar presentación?" appears with "Cancelar" and "Finalizar". "Cancelar" returns to Control. |
| 19 | Swipe up again and press "Finalizar". | The summary shows "Presentación finalizada", "DURACIÓN REAL", the time, the number of slides, "Ritmo promedio … diap/min" and a message such as "Terminaste dentro del tiempo previsto" or "Terminaste 00:42 después del tiempo previsto". The companion lists "Presentación finalizada y guardada". |
| 20 | Press "Guardar y volver al inicio". | Home. (The record was already saved by the PC; this button only returns home.) |
| 21 | Press "Historial". | A row "Hoy" with the time and the pace. Tap it to see the same summary with a "Volver" button. |
| 22 | For the empty state: close the companion, start it with a new database `dotnet run --project windows-companion/src/Presenta.Companion.App -- --database C:\Temp\empty.db`, pair again and open "Historial". | "Aún no hay presentaciones guardadas" and a back button. |

### C. When things go wrong

| # | Do this | Expected |
|---|---|---|
| 23 | During a presentation, press **Detener servidor** in the companion. | The watch keeps showing the Control screen; the status line turns amber with "Reconectando…". The clock keeps counting. Pressing → shows "No se pudo enviar el comando" for a few seconds and does not end the session. The companion window still shows the session. |
| 24 | Press **Iniciar servidor** within 30 seconds. | The watch reconnects by itself, "Conectado" returns, and the counter and time are the same as in the companion window. |
| 25 | Stop the server and wait more than 30 seconds, then start it and press "Reintentar" when the disconnected screen appears. | It reconnects and the session is restored. |
| 26 | Close the companion window completely while a presentation runs, start it again and look at the watch. | The watch cannot connect with the old code: it returns to the pairing screen with "Código incorrecto" (the new run has a new code). Enter the new code. (A session that was running when the program was closed is lost; nothing is saved for it.) |

### D. Measurements

| # | Do this | Expected |
|---|---|---|
| 27 | **Command speed (RNF 03):** with the slideshow focused, press → on the watch and watch the PC. Do it 10 times. Note the slowest time by eye (or record the screen). | The slide changes within about 2 seconds each time. |
| 28 | **Loss detection (RNF 04):** with the watch on Home or Control, start the stopwatch at the moment you press "Detener servidor" and stop it when the watch shows "Reconectando…". Repeat 3 times. | At most 5 seconds. (A clean stop is usually noticed at once. The automated tests show that a server that goes silent is noticed in at most 3.5 seconds.) |
| 29 | Optional silent loss: if you have Sysinternals `pssuspend`, suspend `Presenta.exe` (`pssuspend Presenta`), start the stopwatch, and stop it when the watch shows "Reconectando…". Resume with `pssuspend -r Presenta`. | At most 5 seconds. |

### E. Slide control targeting (Phase 5.1)

This section proves that Next and Previous reach the slideshow even when another program (here Android Studio, which holds the emulator) is in front. Use PowerPoint. The companion lists only windows that are visible and have a title.

Prepare a `.pptx` with at least 5 slides, each with a large number (1 to 5). In PowerPoint use *Slide Show > Set Up Slide Show* and choose **Browsed by an individual (window)**, then press **F5** so the show runs in a window. Later you will repeat the main steps once in full screen.

| # | Do this | Expected |
|---|---|---|
| 30 | Start the companion and note the pairing code. In "Ventana de la presentación" press **Actualizar lista**. Choose the entry that starts with "Sugerida:" (the slide-show window, titled "Presentación de PowerPoint - [your file]"). If there is no suggested entry, choose the slide-show window by title. Then press **Verificar**. | The status reads "Lista: la ventana está disponible" after choosing. After "Verificar" the slide-show window comes to the front and the status reads "Verificada: la ventana pasó al frente". No slide change happens (Verificar presses no key). |
| 31 | Start the `Wear_OS_Large_Round` emulator in Android Studio, run the watch app, and pair it with the code. On the watch choose "Nueva presentación", set **5** slides and **5** minutes, and press "Iniciar". | The watch shows "01 / 5". The companion shows "Presentación iniciada". |
| 32 | **Click inside the emulator** (Android Studio is now in front) and tap **→** on the watch. | PowerPoint comes to the front by itself and shows slide 2. The watch shows "02 / 5". Repeat until slide 5, clicking the emulator before each tap. Then tap **←** twice. At every step the slide shown in PowerPoint equals the number on the watch. |
| 33 | Stop the slide show. Start it again in **full screen** (*Slide Show > Set Up Slide Show > Presented by a speaker (full screen)*, then F5). Press "Actualizar lista", choose the suggested entry again, press "Verificar". Start a new 5-slide presentation on the watch and repeat row 32 for a few taps. | The same behavior with the full-screen show. |
| 34 | **Wrong-application check:** open Notepad and type a few lines of text. Keep PowerPoint's show visible. In the companion press "Actualizar lista" and choose the Notepad window; press "Verificar". Tap → and ← on the watch. | The cursor moves in Notepad (Right and Left arrows) and PowerPoint does **not** advance. Choose the slide-show window again afterwards. |
| 35 | **Closed target:** close the slide show (Esc) and tap → on the watch. | The watch shows an error message "Elige la ventana de la presentación en el computador", the counter on the watch does **not** change, and the session keeps running. The companion status line says the window was not found. |
| 36 | Restart the slide show, press "Actualizar lista" in the companion, choose the slide-show window, press "Verificar", and tap → on the watch. | It works again and the counter and slide match. |
| 37 | **Minimized target:** minimize the PowerPoint slide-show window (or the whole PowerPoint) and tap → on the watch. | The status line reads "Minimizada: se restaurará al enviar una tecla". The window is restored and advances one slide. |
| 38 | **Keys off:** switch "Enviar teclas a la presentación" off. Tap → on the watch. | The counter changes, nothing happens in PowerPoint, and the companion lists "Diapositiva N (sin enviar tecla)". With keys off you may even clear the choice: no window is needed. Switch the keys on again. |
| 39 | **Restart the companion:** close it, start it again, and look at the "Ventana de la presentación" section (with the slide show still open). | The previous choice is recognized without choosing it again (status "Lista: la ventana está disponible"). If you close the slide show first, the status says the window was not found. |
| 40 | **Start with no target:** with "Enviar teclas a la presentación" on, close the slide show, so no usable window is chosen, and on the watch try "Iniciar" for a new presentation (finish the running one first). | The presentation does not start, and the watch shows "Elige la ventana de la presentación en el computador". Open the show, choose it, and "Iniciar" works. |
| 41 | **Elevated window (optional):** open PowerPoint "as administrator" and choose its slide-show window. | The status line explains that the window runs as administrator and that Windows blocks keys into it; the watch shows the same message as in row 35. Close it and use the normal one. |
| 42 | **Speed:** with Android Studio in front, tap → ten times, noting the slowest delay from the tap to the slide change (stopwatch or screen recording). | At most about 2 seconds each time. Note the slowest. |

If the key does not arrive: press "Verificar". If "Verificar" says Windows did not let the window come forward, click the slide-show window once and try again, and tell me the exact message.

## 7. Troubleshooting

| Problem | What to do |
|---|---|
| The watch never reaches the PC ("Reconectando…" forever, or "Sin conexión al computador"), and the companion shows "Servidor activo" | The emulator may not reach `10.0.2.2`. Use the fallback: run `adb reverse tcp:8765 tcp:8765` and tell the app to use `localhost` (next row). |
| Using `adb reverse` and `localhost` | In a new PowerShell: `& "C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe" reverse tcp:8765 tcp:8765`, then set the address once with: `& "C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe" shell run-as com.presenta.wear sh -c "echo ws://localhost:8765 > files/server_address.txt"` and restart the app on the emulator. This works only for the debug build you run from Android Studio. To go back to the default address: `... shell run-as com.presenta.wear rm files/server_address.txt`. |
| `adb` is "not recognized" | `adb` is not on your PATH. Use the full path: `C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe` (this is the SDK folder listed in `docs/environment.md`). Or add that folder to PATH. |
| Windows asks "Allow Presenta to communicate on these networks?" | For the emulator you can choose Cancel; the emulator uses the loopback address. You only need to allow it when you turn on "Permitir reloj físico en la red local". |
| Log shows "Cleartext HTTP traffic not permitted" | The app only allows plain connections to `10.0.2.2`, `localhost` and `127.0.0.1`. Check the address is one of those. A physical watch on a Wi-Fi address is **not supported yet** (planned for a later phase). |
| "Código incorrecto" although you typed the number you wrote down | The companion creates a new code each time it starts or when you press "Generar código nuevo". Read the code in the window again. |
| The companion says the port is in use ("No se pudo iniciar el servidor…") | Another program or another copy of Presenta uses port 8765. Close it, or start the companion with another port: `dotnet run --project windows-companion/src/Presenta.Companion.App -- --port 8766` (the watch then needs the matching address, see the `adb reverse`/override rows). |
| Next/Previous work in the watch but the slideshow does not move | Keys go only to the window chosen in "Ventana de la presentación". Check that section: the status must be "Lista: la ventana está disponible"; press "Actualizar lista", choose the slideshow window again and press "Verificar". Also check "Enviar teclas a la presentación" is on and that the slideshow is running. |
| The watch shows "Elige la ventana de la presentación en el computador" | Keys are on but no usable window is chosen (none, closed, or several identical windows). Choose the slideshow window in the companion, or switch "Enviar teclas a la presentación" off. |
| The watch shows "No se pudo enviar el comando a la presentación" | Windows did not let the companion bring the window to the front. Press "Verificar" and read the message; click the slideshow window once and try again. The counter did not move. |
| The status says the window runs as administrator | Windows blocks keys into administrator programs. Run Presenta as administrator too, or run the slideshow normally. |
| The emulator is very slow | Close other heavy programs; wait for it to finish starting. |
| Gradle sync fails in Android Studio | Check you opened the `wear-app` folder, and that there is internet access for the first sync. |

## 8. Results

Fill this in as you go.

| # | Pass / Fail / Skip | What you saw (especially if it was not as expected) |
|---|---|---|
| 1 | | |
| 2 | | |
| 3 | | |
| 4 | | |
| 5 | | |
| 6 | | |
| 7 | | |
| 8 | | |
| 9 | | |
| 10 | | |
| 11 | | |
| 12 | | |
| 13 | | |
| 14 | | |
| 15 | | |
| 16 | | |
| 17 | | |
| 18 | | |
| 19 | | |
| 20 | | |
| 21 | | |
| 22 | | |
| 23 | | |
| 24 | | |
| 25 | | |
| 26 | | |
| 30 | | |
| 31 | | |
| 32 | | |
| 33 | | |
| 34 | | |
| 35 | | |
| 36 | | |
| 37 | | |
| 38 | | |
| 39 | | |
| 40 | | |
| 41 | | |
| 42 | | |

| Measurement | Result | Target |
|---|---|---|
| Slowest slide change after pressing → (10 tries), seconds | | about 2 |
| Time until "Reconectando…" after stopping the server, try 1 / 2 / 3, seconds | | 5 or less |
| Time until "Reconectando…" with a suspended companion (optional), seconds | | 5 or less |
| Did `10.0.2.2` work, or did you need `adb reverse`? | | |
| Slideshow program and version used | | |
| Slowest slide change after tapping → with Android Studio in front (row 42), seconds | | about 2 |

Other notes (screens that looked wrong, texts that read badly in Spanish, anything confusing):

&nbsp;

## 9. After the test

Close the companion window (it stops the server cleanly). If you used `adb reverse`, you can remove it with `adb reverse --remove-all`. Send the filled-in results back so the next phase can fix what failed.
