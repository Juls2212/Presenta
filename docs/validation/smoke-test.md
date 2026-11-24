# Smoke test (10 minutes, critical path)

Use this when there is time for one short manual check only. It walks the path a demo takes, in 12 rows, and says which acceptance criteria (AC1 to AC7 of [validation-guide.md](validation-guide.md), section 8) and which requirements each row proves. It does **not** replace the full guide: rows that need more time (all alert timings, reconnection, crash recovery, font scales, visual review) stay in [validation-guide.md](validation-guide.md).

Everything an automated check can prove is already in `automated-run-latest.md`; this test confirms the same path with the real emulator and a real PowerPoint slide show.

Since Phase 11 the companion window has a navigation rail with six pages (Inicio, Vincular, Ventana, IA y alertas, Actividad, Configuración); the rows below name the page to open. The checks are unchanged.

## Before you start (2 minutes, not counted in the 10)

1. In PowerShell, from the project root: `./scripts/demo-preflight.ps1`. It must show no FAIL. A WARN for `adb devices` is normal if the emulator is not started yet; a WARN for the AI variables only means the AI will be off (row 12 then checks the fallback).
2. Start the emulator `Wear_OS_Large_Round` from Android Studio and run the **debug** build of the watch app (the app must be installed already).
3. Open your 5-slide test deck in PowerPoint and start the slide show in a **window** (F5). Each slide has a big number 1 to 5.
4. Start the companion: `./scripts/run-companion.ps1` (loads `.env`, so the AI is available) or `dotnet run --project windows-companion/src/Presenta.Companion.App`.
5. Open a folder outside the repository for evidence (for example `C:\Users\ASUS\Desktop\presenta-evidence`).

Setup details and troubleshooting are in [../manual-test-guide.md](../manual-test-guide.md). Mark each row `Pass` or `Fail` in the table at the end. Spanish text in double quotes is exactly what the app shows. Numbers in a text (such as "02 / 5") change with the situation.

## The 12 rows

| # | Min | Covers | Steps | Expected | Pass | Fail |
|---|---|---|---|---|---|---|
| 1 | 0:30 | RF 01, RF 02, AC1 | Look at the companion window after it starts: the bar at the top, the page "Inicio" and the page "Vincular". Write down the six-digit pairing code (page "Vincular"). | The bar at the top shows "Servidor activo". "Inicio" shows "Sin presentación activa" and, in the list "Antes de empezar", the row "Reloj" with "Esperando al reloj". "Vincular" shows the six digits, each in its own box. The page "Configuración" shows "Puerto 8765". | | |
| 2 | 1:00 | RF 01, RF 02, RNF 07, AC1 | On the watch, if the pairing screen "Vincular" is shown, enter the code (+ and − change a digit, "Siguiente" moves on, "Conectar" on the sixth). If the watch is already paired with the remembered code it goes straight to Home. | Watch Home shows the pill "PC conectado". The companion shows "Reloj conectado" (page "Vincular") and the dot beside "Vincular" changes shape from a ring to a filled circle. | | |
| 3 | 1:00 | RF 05, RF 06, AC2 | In the companion, open the page "Ventana" (title "Ventana de la presentación") and press "Actualizar lista". With a single slide show open the entry starting with "Sugerida:" is already selected. Otherwise choose it. Press "Verificar". | The status line first reads "Se eligió sola la única presentación abierta. Pulsa Verificar para comprobarla." (only when it chose by itself). After "Verificar" the slide-show window comes to the front and the status reads "Verificada: la ventana pasó al frente". No slide changes. | | |
| 4 | 0:45 | RF 03, RF 04, RF 08, RF 09, AC3, AC4 | On the watch: "Nueva presentación". Set **5** slides and **5** minutes. Press "Iniciar". | The watch shows "01 / 5", a clock at 00:00 and "Quedan 05:00", and a pace pill. On "Inicio" the progress ring and the figures count the session, and the activity list shows "Presentación iniciada". | | |
| 5 | 1:15 | RF 05, RNF 01, RNF 03, AC2 | **Click inside the emulator** so Android Studio is in front, then tap **→** on the watch. Repeat four times, clicking the emulator before each tap. | PowerPoint comes to the front by itself and moves one slide each time, within about 2 seconds, to slide 5. The watch counter reads "05 / 5" at the end and the number on the slide is 5. The companion lists "Siguiente enviado" each time. | | |
| 6 | 0:30 | RF 06, AC2 | Tap **←** once. | PowerPoint shows slide 4 and the watch reads "04 / 5". The companion lists "Anterior enviado". | | |
| 7 | 0:45 | RF 07 | **Correction:** tap the slide number on the watch. The screen "Ir a diapositiva" opens. Press − once (slide 3), then "Listo". | The watch counter changes to 3. **PowerPoint stays on slide 4**, because a correction presses no key. The companion lists "Contador ajustado a la diapositiva 3". | | |
| 8 | 0:45 | RF 08, RNF 08 | Press and hold the clock on the watch. Look at the screen, then hold it again. | "En pausa" appears in amber and the clock freezes; the companion shows "En pausa". The second hold resumes counting. | | |
| 9 | 0:45 | RF 12, RF 13, AC6 | Swipe up on the Control screen, read "¿Finalizar presentación?", press "Finalizar". Then press "Guardar y volver al inicio". | The summary shows "Presentación finalizada", "DURACIÓN REAL", the slide count and "Ritmo promedio". The companion lists "Presentación finalizada y guardada". The watch returns to Home. | | |
| 10 | 0:30 | RF 14, RNF 06, AC6 | Press "Historial". Tap the new row. | A row "Hoy" with the time is listed; opening it shows the same duration, slides and pace as the summary in row 9, with a "Volver" button. | | |
| 11 | 1:30 | RF 10, RF 11, RNF 01, AC5 | Start a new presentation with **5** slides and **1** minute. Do not tap anything for 16 seconds. When the alert appears, tap **→** and look at PowerPoint, then tap "Entendido". | At about 16 seconds the watch shows "Vas atrasado" and "Tienes 4 diapositivas pendientes" in amber. **→ and ← still work during the alert**: the slideshow moves to slide 2. "Entendido" returns to the normal Control screen. | | |
| 12 | 0:45 | RF 11, RF 15, RNF 07 | Right after the alert of row 11 (give the provider a few seconds) open the page "IA y alertas" in the companion and look at the section "Recomendaciones con IA" and at the alert pill on the watch. If you already pressed "Entendido", wait for the next alert (about 42 seconds after the start). Then swipe up and finish this session ("Finalizar"). | **AI on** (variables set and switch "Usar IA para redactar las recomendaciones" on): the section shows "Último texto" followed by one short sentence, and the alert pill on the watch shows it. **AI off or unavailable:** the section explains why (for example "Apagada: se usan las frases de siempre.") and the watch shows the usual wording (for example "Acelera ahora"). Either result passes: the fallback is the designed behavior. The session ends with "Presentación finalizada y guardada". | | |

Total: about 10 to 11 minutes with normal pauses between taps. If you are running late, skip row 7, then row 12, and note it.

## What each acceptance criterion needs

| AC | Proved by rows | Notes |
|---|---|---|
| AC1 watch connects and shows the state | 1, 2 | The loss and reconnection states are in guide rows 6, 7, 23, 24, 97. |
| AC2 next and previous reach the presentation | 3, 5, 6 | Full screen, wrong window, closed window: guide rows 33 to 37. |
| AC3 configure duration and slides | 4 | Limits: guide row 69. |
| AC4 counter, times and pace message | 4, 5, 8 | Clock accuracy against a stopwatch: guide row 71. |
| AC5 alert and haptic attempt | 11 | The alert is shown here. Vibration is proven in Logcat only (guide rows 57 to 61); how it feels needs a physical watch. |
| AC6 save to history and summary | 9, 10 | Restart persistence: guide rows 76 and 93. |
| AC7 screens walked through and validated visually | partly 1 to 12 | The smoke test passes through Home, Setup, Control, alert, pause, finish question, summary and history, but the visual review V1 to V12 of the guide is not done here. |

Requirements touched: RF 01 to RF 15 except RF 09 beyond the first clock reading (see guide row 71); RNF 01, RNF 03, RNF 06, RNF 07 and RNF 08 in part. RNF 02 (font sizes), RNF 04 (loss within 5 seconds) and RNF 05 (Windows 10) are not covered by this test.

## If a row fails: what to capture

Put these in the evidence folder, named with the row number (for example `S05-`):

1. A screenshot of the watch at the moment of the failure (the camera icon in the emulator's side toolbar).
2. A screenshot of the companion window, including the pages "Ventana" and "Actividad".
3. The **activity line** text, copied exactly. A failed key always says why after "No se pudo enviar la tecla:".
4. For rows 3, 5, 6 (keys): press "Verificar", then "Copiar diagnóstico", paste into a text file (`diagnostic.txt`), read it once and remove file names you do not want to share. The status line reads "Diagnóstico copiado" when it worked.
5. For watch-side problems: `& "C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe" logcat -d -s PresentaHaptics:D PresentaTiming:D > logcat.txt`.
6. The output of `./scripts/demo-preflight.ps1` (it prints no secret).

Then record the failure in the defects log (section 10 of the guide) with the row number.

## Results

| Item | Value |
|---|---|
| Date and time | |
| Windows version | |
| .NET SDK | |
| Slide-show program and version | |
| AI | on, off or unavailable (circle one) |
| Evidence folder | |

| Row | Result (Pass / Fail / Blocked) | Notes, evidence file names |
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

Verdict: all 12 rows Pass means the critical path of the demo works on this machine. Any Fail on rows 1 to 6 means the demo should not be run until it is understood; a Fail on rows 11 or 12 can be demoed around (the alert and AI text are extras on top of the deterministic engine).
