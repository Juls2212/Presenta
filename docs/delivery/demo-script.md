# Demo script (about 6 minutes, emulator only)

The demo shows the watch controlling a real PowerPoint slide show on the PC, the deterministic alerts of a short session, the optional AI wording, the summary and the history. It uses the Wear OS emulator (`Wear_OS_Large_Round`) next to PowerPoint on one Windows computer. The bullets under `Say` are suggestions in short English; adapt them to your own words and language.

Related: [../validation/smoke-test.md](../validation/smoke-test.md) (the 12-row check to run once before the demo), [../manual-test-guide.md](../manual-test-guide.md) (setup and troubleshooting), [../validation/validation-guide.md](../validation/validation-guide.md) (all timings).

Using the downloadables: instead of building from source you can use the files in `releases/` (see [release.md](release.md) and `releases/LEEME.md`). Unzip `Presenta-Companion-win-x64.zip`, start `Iniciar-Presenta.ps1` (put the AI settings in a `.env` next to it, or leave it out to run without the AI), and install `Presenta-Wear.apk` on the emulator with `adb install -r`. The checklist below applies unchanged, except that `demo-preflight.ps1` and `run-companion.ps1` are repository scripts and are not in the zip.

## 1. Pre-demo checklist (about 10 minutes, the day before and again 15 minutes before)

Run `./scripts/demo-preflight.ps1` first. It checks the tools, the AVD, the port and the AI variables (presence only) and prints a reminder list. It starts nothing.

| # | Check | How |
|---|---|---|
| 1 | Preflight shows no FAIL. | `./scripts/demo-preflight.ps1`. A WARN for `adb devices` is fine before the emulator is started. |
| 2 | **Emulator date and time are automatic.** | In the emulator: Settings > System > Date & time > automatic. A wrong date makes the history show the wrong day instead of "Hoy". |
| 3 | The emulator is started and the **debug** watch app is installed. | Device Manager > `Wear_OS_Large_Round` > start. Run the `app` configuration from Android Studio (open the `wear-app` folder). |
| 4 | **Window layout.** | Android Studio (holding the emulator) on one side, the PowerPoint slide show on the other, the companion window on a second area or monitor if you have one. All three visible without moving windows during the demo. |
| 5 | **PowerPoint runs the deck in a window**, not full screen, not as administrator. | A deck of 10 slides, each with a big number 1 to 10. *Slide Show > Set Up Slide Show > Browsed by an individual (window)*, then F5. Full screen was not verified with the real program (guide row 33). |
| 6 | **Companion started with the remembered code on.** | Start it (see below). Turn on the switch "Recordar código de vinculación" once, pair the watch once, and from then on the watch reconnects after restarts without typing. |
| 7 | **AI variables only in the PowerShell session** that starts the companion. | Easiest: copy `.env.example` to `.env`, fill in the key, and start with `./scripts/run-companion.ps1`. Or type the three `PRESENTA_AI_*` variables in the window (see `windows-companion/README.md`). Never put the key in a file you share. The AI is off by default: turn the switch "Usar IA para redactar las recomendaciones" on in the companion after it starts. |
| 8 | **Firewall.** | If Windows asks whether Presenta may communicate on networks, choose Cancel. The emulator does not need it. |
| 9 | **`adb reverse` fallback ready.** | Only if the watch cannot reach the PC: `& "C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe" reverse tcp:8765 tcp:8765` and the `localhost` address override of `docs/manual-test-guide.md` (section 5). |
| 10 | Close other presentations and heavy programs. | So keys cannot reach the wrong window and the emulator stays fast. |
| 11 | **Rehearse once** with the real alert times. | Run the flow below once. For a rehearsal without PowerPoint use the fast host (guide row 107): `dotnet run --project windows-companion/tools/Presenta.DevHost -- --code 123456 --port 8765 --time-scale 30 --fake-ai`. |

Start the companion: `./scripts/run-companion.ps1` (with `.env`) or `dotnet run --project windows-companion/src/Presenta.Companion.App` from the project root. Only one companion can run at a time.

## 2. The live flow

The session used in the demo is **2 minutes, 10 slides**. The alert seconds are the ones asserted by `AlertTimingTableTests` (guide rows 46 to 48) for a presenter who stays on slide 1 at the start: "Vas atrasado" at **14 s**, "Quedan 36 s" at **84 s**, "Tiempo agotado" at **121 s**. Allow about 2 seconds for the companion's one-second tick and the network. Pausing stops the clock, so a pause delays the later alerts by its length; in this flow the pause comes after the time alert.

Times below are approximate wall-clock time since the start of the demo. `T` is the moment you press "Iniciar".

| Time | Step | Do | Say |
|---|---|---|---|
| 0:00 | Intro | Show the companion window and the watch Home (pill "PC conectado"). | The watch controls the PC through a small server on this computer. It is already paired; the code is remembered. |
| 0:20 | Window | In the companion, section "Ventana de la presentación": the slide show is already chosen ("Sugerida:" entry, status "Se eligió sola la única presentación abierta. Pulsa Verificar para comprobarla."). Press "Verificar". | Keys go only to the chosen window, never to whichever window is in front. "Verificar" brings it forward and presses nothing. The status then reads "Verificada: la ventana pasó al frente". |
| 0:45 | Configure | On the watch: "Nueva presentación". Set **10** slides and **2** minutes. Press "Iniciar" at about 1:00 (this is `T`). | Slides and minutes are set on the watch. The PC owns the clock; the watch only shows it. |
| 1:00 to 1:14 (`T` to `T`+14 s) | Control screen | Do **not** tap yet. Point at the counter "01 / 10", the clock, "Quedan 02:00", the ring and the pace pill ("Calculando ritmo"). | Counter, elapsed time, remaining time and pace on one screen. The pace needs a few seconds of data first. |
| about 1:15 (`T`+14 s) | Alert | The alert appears: "Vas atrasado" and "Tienes 9 diapositivas pendientes", amber, with "Entendido". Do not press "Entendido" yet. | The rules are deterministic: after the warm-up, being a slide behind plan raises an alert. On a real watch this is also a short double vibration; on the emulator you only see it. |
| 1:15 to 1:45 | Controls during the alert | Tap **→** on the watch (click inside the emulator first, so Android Studio is in front). PowerPoint comes forward by itself and shows slide 2. Look at the companion: "Siguiente enviado". | The controls never leave the main screen, even during an alert. Android Studio is in front, and the slide still changes. |
| 1:30 (a few seconds after the alert) | AI wording | Look at the card "Recomendaciones con IA" and at the alert pill on the watch. | The optional AI only rewrites the tip as one short Spanish sentence. It never decides pace, alerts or timing, and it only gets codes and rounded numbers, never names or files. If it is off or fails, the usual wording is shown. |
| 1:45 | Acknowledge | Press "Entendido". | Back to the normal screen. The same alert would come back after two minutes, and only if it were not acknowledged. |
| 1:50 | Next and previous | Tap **→** twice (slides 3 and 4), then **←** once (slide 3). Companion: "Siguiente enviado", "Anterior enviado". | One tap, one key. At the last slide or the first one nothing is sent. |
| 2:05 | Correction | Advance the slide show by hand to slide 4 with the keyboard, as if you had moved it yourself, so the counter (3) and the slide (4) disagree. On the watch tap the counter, press + once, then "Listo". Companion: "Contador ajustado a la diapositiva 4". | If the slide shown and the counter ever disagree, this fixes the counter without pressing any key. Be quick: the next alert is due at about 2:24. |
| about 2:24 (`T`+84 s) | Time alert | "Quedan 36 s" appears in amber. Point at it for a few seconds. | Near the end the alert becomes a time alert: it is critical (one long vibration on a real watch, then a short one). |
| 2:35 | Pause | Press and hold the clock: "En pausa". Hold again after about 8 seconds to resume. | Pause stops the clock; paused time does not count. |
| 2:55 | Finish | Swipe up. The question "¿Finalizar presentación?" appears. Press "Finalizar". | Finishing is a deliberate gesture with a confirmation, so it cannot happen by accident. Do it before about 3:10 unless you want the overtime alert. |
| 3:10 | Summary | Read the summary: "Presentación finalizada", "DURACIÓN REAL", the slide count, "Ritmo promedio" and the time message. Companion: "Presentación finalizada y guardada". Press "Guardar y volver al inicio". | The PC saved it automatically; there is no save button to forget. |
| 3:40 | History | Press "Historial". A row "Hoy" appears; open it. | The history lives on the PC, in a small database, and the watch only asks for it. It survives restarting the companion. |
| 4:10 | Wrap-up | Return to Home. Show the companion window once more. | A deterministic engine on the PC, a thin watch client, and an optional AI that cannot change the outcome. |
| 4:40 to 6:00 | Questions | | Be ready to say what is not verified (section 4). |

Optional if there is time: let the time run out instead of finishing at 2:55. At `T`+121 s (about 3:01, later by the length of the pause) the red "Tiempo agotado" appears (red is used for overtime only), then finish. This adds about 30 seconds.

## 3. Contingencies

| Problem | What to do | If it still fails |
|---|---|---|
| **The watch cannot connect** (stays on "Reconectando…" or shows "Sin conexión al computador"). | Check the companion shows "Servidor activo" and "Puerto 8765". Press "Reintentar" on the watch. | Run the `adb reverse` fallback (checklist item 9) and restart the watch app. Say the connection is local and continue with the companion window. |
| **Wrong or expired code** ("Código incorrecto"). | The code changes every start unless "Recordar código de vinculación" is on. Read the code in the companion window and enter it again. After "Generar código nuevo" the watch must be paired again too. | After five wrong attempts the watch must wait about 30 seconds. |
| **Keys do not advance the slides.** | Read the activity line: after "No se pudo enviar la tecla:" it says why. Press "Verificar" and then click the slide-show window once. If the watch says "Elige la ventana de la presentación en el computador", choose the slide-show window in "Ventana de la presentación" ("Actualizar lista" first). | Press "Copiar diagnóstico" (the status reads "Diagnóstico copiado"), paste it into a text file and keep it for after the demo. As a last resort switch off "Enviar teclas a la presentación" and advance PowerPoint by hand: the counter and alerts still work. |
| **A different PowerPoint window is chosen** (an amber line says it is not the slide-show window). | Choose the entry that starts with "Sugerida:". | If there is none, start the slide show and press "Actualizar lista". |
| **AI is offline or the key is invalid.** | Nothing is needed: the watch shows the usual wording. The card says "La IA rechazó la clave: se usan las frases de siempre." or "En pausa por errores: se usan las frases de siempre." | Turn the switch off and on to retry once. Say this is the designed fallback. |
| **The model returned no text.** | The card reads "El modelo no devolvió texto: se usan las frases de siempre. Prueba con un modelo de chat que no sea de razonamiento o sube el límite de tokens." | Continue; fix the model or `PRESENTA_AI_MAX_TOKENS` after the demo. |
| **Emulator date or time is wrong.** | In the emulator: Settings > System > Date & time > automatic. If the date is still wrong, do a cold boot of the AVD from Device Manager. | Say the history shows the day relative to the watch clock and continue. |
| **The companion is already open** (a second start shows "Presenta ya está abierto. Búscalo en la barra de tareas."). | Use the first window from the taskbar. Only one companion can run at a time. | If it is not visible, end the task `Presenta` in Task Manager (only that one) and start it again. A session that was running is saved as interrupted at the next start. |
| **The alert did not appear at 14 s.** | You may have pressed → or the counter changed before it. Alert seconds are for a presenter who stays on slide 1. | Continue with the other steps, or start a new session and stay still. |
| **The port is busy** (the companion shows an error about the port). | Close the other program or the second companion that uses port 8765. | Start the companion with `-- --port 8766` and use the address override (manual guide, section 5). |

## 4. What to say honestly if asked

- Verified by automated tests: the engine (timer, pace, alerts), the protocol, the server, the watch screens on two round sizes, history, reconnection and the AI path with a local stand-in. See `docs/validation/automated-run-latest.md`.
- Verified only on this setup: the emulator and a windowed PowerPoint slide show on Windows 11 (once you ran the smoke test).
- Not verified: a physical watch, real vibration, a real network (LAN), Windows 10, PowerPoint in full screen, and any real AI provider unless you ran it yourself.
