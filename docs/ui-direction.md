# UI direction

The mockup sheet "Presenta · Lámina de pantallas" (Wear OS, 454 × 454 px, round; see `mockups/README.md`) is the visual source of truth. UI strings are Spanish and live in resource files.

## Palette

| Role | Hex |
|---|---|
| Night (background) | `#0C1424` |
| Warm white (text, light buttons) | `#F5EFE6` |
| Cyan (primary action) | `#22D3EE` |
| Amber (alert) | `#FFB020` |
| Secondary and stepper buttons | `#1E2D4C` |
| Card / surface | `#152139` |
| Ring track (approx., measured from the raster, ±1) | `#111725` |
| Critical red (proposed, overtime only) | `#FF5C5C` |

**Final red: `#FF5C5C`, unchanged.** Secondary text on night uses `#B8B3AF` (warm white at lower emphasis). Contrast ratios are computed by `ContrastTest` (WCAG 2.x formula, run on every build; the test fails below 4.5:1):

| Pair | Ratio |
|---|---|
| Critical red `#FF5C5C` on night | 6.08:1 |
| Night text or icon on critical red | 6.08:1 |
| Critical red on its 14% red pill over night | 5.18:1 |
| Warm white on night | 16.10:1 |
| Muted text `#B8B3AF` on night | 8.86:1 |
| Cyan on night (and night text on the cyan button) | 10.18:1 |
| Amber on night (and night icon on amber) | 10.06:1 |
| Night text on the warm-white button | 16.10:1 |
| Warm white on navy `#1E2D4C` | 11.97:1 |
| Warm white on card `#152139` | 14.04:1 |
| Muted text on card | 7.72:1 |
| Cyan or amber on their 14% pills over night | 7.73:1 and 7.80:1 |

Red is used only for overtime: the arc, the "Pasaste" line, and the overtime alert.

## Screens from the sheet

| # | Screen | Content |
|---|---|---|
| 01 | Home, connected | Cyan icon, "Presenta", pill "PC conectado", cyan button "Nueva presentación", outlined button "Historial". |
| 02 | Setup | "Nueva presentación", "Configura tu exposición", two circular steppers ("20 diapositivas", "15 min") with − and + buttons, cyan "Iniciar". |
| 03 | Control (key screen) | "● Conectado", large "07 / 20", clock and elapsed "08:32", "Quedan 06:28", pace pill "Buen ritmo", large back (navy) and forward (cyan, dominant) buttons, progress arc. |
| 04 | Alert (amber) | Warning icon, "Quedan 5 min", "Tienes 9 diapositivas pendientes", amber pill "Acelera ligeramente", outlined amber "Entendido", back and forward still visible, amber arc. Controls are not blocked. |
| 05 | Final summary | Cyan ring and check, "Presentación finalizada", "DURACIÓN REAL", "14:32", "20 diapositivas", "Ritmo promedio 1.37 diap/min", "Terminaste dentro del tiempo previsto", warm-white button "Guardar y volver al inicio". |
| 06 | History | "Historial", rows ("Hoy", "Ayer", "08 Oct") with clock and time and pace "1.37 diap/min", chevron, back button. |
| 06b | History, empty | "Aún no hay presentaciones guardadas". |
| 07 | Disconnected | Dashed ring, crossed-out monitor, "Sin conexión al computador", "Verifica que la aplicación de Windows esté abierta", warm-white "Reintentar". |

Navigation views (5): Home (01, 07), Setup (02), Control (03, 04), Result/History (05, 06, 06b). Alert is listed as a view in the source document but is implemented as a state of Control (ADR-21).

## Approved decisions

1. The session name is auto-generated; there is no text field.
2. Pause and resume by long-pressing the clock, with an amber "En pausa" indicator.
3. Tapping "07 / 20" opens a slide selector with − and +.
4. Finish by swiping up on Control, opening a confirmation ("¿Finalizar presentación?", "Cancelar", "Finalizar").
5. "Reconectando" is an amber variant of screen 07.
6. Red is reserved for overtime.
7. History rows keep the chevron and open a read-only summary (screen 05 variant with a "Volver" button).

## Derived screens and variants (built in Phases 3 and 5, for user review)

Rendered screenshots are in `ui-previews/large-round/` and `ui-previews/small-round/`, one PNG per state with the same file names. Open `Previews.kt` in Android Studio for the live versions.

| Preview | State | Design |
|---|---|---|
| `01-home-connected` | Home, connected (mockup 01) | Cyan icon, "Presenta", pill, cyan "Nueva presentación", outlined "Historial". The icon is dropped on Small Round. |
| `02-setup`, `02b-setup-limits` | Setup (mockup 02) | Two circular steppers with progress arcs, 48 dp − and + buttons, "Iniciar". Defaults 20 slides and 15 minutes; limits 1 to 200 slides and 1 to 180 minutes. Holding − or + repeats. |
| `03-control-normal` | Control (mockup 03) | "Conectado", "07 / 20", clock and "Quedan 06:28", pace pill, navy Previous and dominant cyan Next, and a ring that shows **elapsed time** (since Phase 5). |
| `03b-control-calculating` | Control during the warm-up | Muted pill "Calculando ritmo". |
| `03c-control-paused` | Paused (derived) | Amber "En pausa" replaces the connection line; the clock is dimmed. |
| `03d-control-overtime` | Overtime, alert acknowledged (derived) | Red arc, "Pasaste 01:10" in red, red pill "Termina cuanto antes". |
| `03e-control-reconnecting` | Connection lost during a session (derived) | Amber "Reconectando…" in the status line; controls stay. |
| `03f-control-error-message` | A command failed (derived) | The pill is replaced for 3 seconds by an amber message such as "Ahora no se puede hacer eso". The session continues. |
| `04-control-alert` | Alert (mockup 04) | Amber icon, "Quedan 5 min", "Tienes 9 diapositivas pendientes", amber pill, outlined "Entendido", Previous and Next enabled, amber arc. |
| `04b-control-alert-behind` | Pace alert (derived) | "Vas atrasado" with pending slides and "Acelera ahora". |
| `04c-control-alert-overtime` | Overtime alert (derived) | Red accent: "Tiempo agotado", "Llevas 01:10 de más". |
| `05-summary` | Summary (mockup 05) | Cyan ring and check, "DURACIÓN REAL", slides, average pace, message, warm-white "Guardar y volver al inicio" (navigation only). |
| `05b-summary-over-time` | Over time (derived) | Amber ring, "Terminaste 02:05 después del tiempo previsto". |
| `05c-summary-interrupted` | Interrupted (derived) | Amber ring and warning, "La conexión se perdió antes de terminar". |
| `05d-summary-read-only` | Read-only from history (derived) | Same as 05 with a "Volver" button. |
| `06-history` | History (mockup 06) | "Historial", rows with day, clock time, pace, and chevron; scrolls (crown supported); back button at the end. |
| `06b-history-empty` | Empty history (mockup 06b) | Dashed icon, "Aún no hay presentaciones guardadas", back button. |
| `07-home-disconnected` | Disconnected (mockup 07) | Dashed ring, crossed-out monitor, title, hint, warm-white "Reintentar". |
| `07b-home-reconnecting` | Reconnecting (derived) | Same layout with an amber ring, icon, and title: "Reconectando…", "Buscando el computador". |
| `08-slide-selector` | Slide selector (derived) | "Ir a diapositiva", large number, − and +, "Listo". |
| `09-finish-confirmation` | Finish confirmation (derived) | "¿Finalizar presentación?", "Cancelar", "Finalizar". |
| `10-pairing` | Pairing (derived, Phase 5) | "Vincular" and the hint "Escribe el código del computador"; six digit chips with the selected one in cyan; the selected digit large between a − and a + button (48 dp each, the crown also changes it); a back button for the previous digit and "Siguiente", which becomes "Conectar" on the sixth digit. The last code used is prefilled. |
| `10b-pairing-wrong-code` | Pairing, wrong code (derived) | The hint is replaced by the amber message "Código incorrecto". |
| `10c-pairing-locked-out` | Pairing, lockout (derived) | The amber message "Demasiados intentos. Espera un momento" wraps onto two lines. |
| `10d-pairing-connecting` | Pairing, connecting (derived) | "Conectando…" and a disabled button. |

The pairing screen appears when no code is stored or the companion rejected the stored one (for example after the companion restarted and chose a new code), and goes away once paired. From the disconnected screen, "Reintentar" opens it in that case. A numeric keypad was rejected because 48 dp keys do not fit a round screen (ADR-47).

Behavior of the derived interactions: pause and resume with a long press on the clock block; tapping the slide counter opens the selector; swiping up on Control opens the finish confirmation; the system back gesture closes the overlays. Swipe-to-dismiss is disabled on Control and Summary.

### Differences from the mockup sheet

Compared by eye against the sheet (no pixel overlay was made), the screens keep the palette, hierarchy, copy, and element order. Known differences:

- Buttons, pills, and history rows are taller than the sheet (about 48 to 52 dp against about 34 dp) to meet the 48 dp touch rule, so spacing is tighter and the stepper circles are smaller (54 dp).
- The history list scrolls: the third row and the back button need a scroll, whereas the sheet shows everything at once. The first row is always fully visible.
- On Large Round the summary button wraps onto two lines and is narrower than in the sheet.
- Icons are drawn with simple strokes and differ slightly from the sheet's icon set. The typeface is the system sans-serif (Roboto on Wear OS), which may differ from the sheet's face; this could not be verified.
- Small Round (not in the sheet) drops the home icon and the alert icon, and the summary scrolls. See ADR-31 and ADR-32.
- The Control ring now fills with elapsed time rather than slide progress (ADR-50); the previews of Control show the ring at 57%, which is 08:32 of 15:00.
- Renders come from Robolectric, so anti-aliasing can differ from a real watch.

## Decided in Phase 5

The Control ring represents **elapsed time**: elapsed divided by planned duration, clamped to 0..1 and interpolated between snapshots. The slide number stays textual. In overtime the ring is full and red. The ring is also exposed to accessibility as a progress range. If it should show slide progress after all, only what the view model passes as `progress` changes.

## Design rules

- Touch targets are at least 48 dp; the few Small Round exceptions are listed in ADR-31.
- Minimal text.
- Large forward button.
- Color is never the only signal (icons and text accompany every state).
- Designed for round screens: about 227 dp (Large Round), also checked at about 192 dp (Small Round).
- No generic dashboard look.
