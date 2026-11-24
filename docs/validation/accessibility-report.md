# Accessibility report

One place for all the accessibility evidence of Presenta, with the test that proves each item and an honest list of what is not verified. It consolidates what was already written in [../ui-direction.md](../ui-direction.md), [../decisions.md](../decisions.md) (ADR-31, ADR-32) and [../testing-strategy.md](../testing-strategy.md); nothing here is new behavior. Counts come from `automated-run-latest.md`.

Scope: the **watch app** is the main subject (round screens, touch, spoken labels, color). The Windows companion window has no automated accessibility checks; its state is listed at the end.

## 1. Summary

| Area | Result | Proven by |
|---|---|---|
| Text contrast (watch) | All 13 color pairs used by the UI are at least 4.5:1 (lowest 5.18:1). | `ContrastTest` (4 tests, run on every build); ratios recomputed independently in this phase. |
| Touch targets (watch) | Large Round: every interactive element is at least 48 dp. Small Round: 5 documented exceptions, all secondary controls; Previous and Next stay 48 dp. | `LayoutAccessibilityTest`, `PairingUiTest`, `AiTipLayoutTest`. |
| Font scale | Layouts hold at font scales 1.0 and 1.3 on both round sizes; the app caps the scale on screens that must fit (a trade-off). | `LayoutAccessibilityTest`, `PairingUiTest`, `AiTipLayoutTest`; ADR-32. |
| Text inside the round screen, no overlap, no cut-off text | Checked for all 26 states on both sizes and both scales. | `LayoutAccessibilityTest`. |
| Spoken labels | Every clickable element has a Spanish label; key elements have descriptions. | `LayoutAccessibilityTest` (labels), `PairingUiTest`, `PresentaFlowTest` (by description). |
| Traversal (focus) order | Verified for the pairing screen only. | `PairingUiTest` (`the_focus_order_follows_the_reading_order`). |
| Color is never the only signal | Every alert and state has text and an icon; red is for overtime only. | `AlertStatesTest`, the 26 rendered states; ADR-60 rules. |
| Real TalkBack, real device | **Not verified.** | Nothing automated. |

## 2. Contrast

Rule: WCAG 2.x ratio of at least 4.5:1 for every text or icon pair. `ContrastTest` fails the build below that. The test computes ratios from the color constants of the theme, with the 14% pills composited over the night background.

| Pair | Ratio | Test |
|---|---|---|
| Critical red `#FF5C5C` on night `#0C1424` | 6.08:1 | `text_on_night_background` |
| Night on critical red | 6.08:1 | `text_on_buttons` |
| Critical red on its 14% pill over night | 5.18:1 | `text_on_cards_and_pills` |
| Warm white `#F5EFE6` on night | 16.10:1 | `text_on_night_background` |
| Muted text `#B8B3AF` on night | 8.86:1 | `text_on_night_background` |
| Cyan `#22D3EE` on night (and night on the cyan button) | 10.18:1 | `text_on_night_background`, `text_on_buttons` |
| Amber `#FFB020` on night (and night on amber) | 10.06:1 | `text_on_night_background`, `text_on_buttons` |
| Night on the warm-white button | 16.10:1 | `text_on_buttons` |
| Warm white on navy `#1E2D4C` | 11.97:1 | `text_on_buttons` |
| Warm white on card `#152139` | 14.04:1 | `text_on_cards_and_pills` |
| Muted text on card | 7.72:1 | `text_on_cards_and_pills` |
| Cyan on its 14% pill over night | about 7.7:1 | `text_on_cards_and_pills` |
| Amber on its 14% pill over night | 7.80:1 | `text_on_cards_and_pills` |

The three accents (red, amber, cyan) also differ from each other (red against amber 1.66:1, against cyan 1.68:1; the test requires more than 1.2:1) in `critical_red_is_distinct_from_amber_and_cyan`. Documented in `ui-direction.md` as 7.73:1 for the cyan pill; the independent recomputation gives 7.74:1 (rounding of the composited color), which changes nothing.

Not covered: the ring track color against the night background (a graphic, not text), and the companion window palette (it reuses the same colors, unmeasured).

## 3. Touch targets

Rule: at least 48 dp tall and wide where physically possible. The harness renders a round window of 227 dp (Large Round) and 192 dp (Small Round) and measures every node with a click action.

| Where | Rule | Proven by |
|---|---|---|
| Large Round, all screens and states | Everything interactive is at least 48 dp. No exception. | `LayoutAccessibilityTest` (26 states, scales 1.0 and 1.3) |
| Small Round, Control: Previous and Next | 48 dp. | `LayoutAccessibilityTest` |
| Small Round, Control: slide counter (description "Diapositiva 1 de 21. Toca para elegir otra") and the clock block (pause by long press) | 36 dp tall, 48 dp wide. Secondary controls. | `LayoutAccessibilityTest`, ADR-31 |
| Small Round, Setup: the steppers | 44 dp tall and 42 dp wide. | `LayoutAccessibilityTest`, ADR-31 |
| Small Round, Setup: "Iniciar" | 44 dp tall. | `LayoutAccessibilityTest`, ADR-31 |
| Small Round, alert: "Entendido" | 44 dp tall. | `LayoutAccessibilityTest`, ADR-31 |
| Pairing screen (digit buttons, "Siguiente"), the AI tip alert | 48 dp at both sizes and scales checked. | `PairingUiTest` (three layout tests), `AiTipLayoutTest` |
| History list and the summary on Small Round (scrolling content) | Checked where first shown (the visible part). | `LayoutAccessibilityTest` (`bottomFraction`), ADR-31 |

The exceptions are written into the test and fail the build if they grow. The header comment of `LayoutAccessibilityTest` still mentions only the stepper width; the code lists all five (documentation inconsistency in a code comment, listed in the delivery report, not changed).

## 4. Font scale

| Item | Evidence |
|---|---|
| Scales 1.0 and 1.3 on both round sizes: text inside the circle, no text cut off or ellipsized (except the documented AI tip case), no overlapping texts, touch targets hold. | `LayoutAccessibilityTest`, `PairingUiTest` (Large Round 1.0 and 1.3, Small Round 1.0), `AiTipLayoutTest` (both sizes, both scales: a 41-character tip fits; an 80-character tip stays inside the circle and may end in an ellipsis at 1.3). |
| The system font scale is capped on screens that must fit without scrolling: 1.15 on Large Round and 1.0 on Small Round (Home, Setup, Control, Summary, history messages). The history list keeps the full scale. Long headlines shrink to fit instead of being cut. | ADR-32; the cap is in the shared component code and exercised at scale 1.3 by the tests above. |

Trade-off, stated plainly: a user with the largest system font gets less text growth than they asked for on these screens (none on Small Round). It is a known limitation to revisit on a real watch.

## 5. Spoken labels (content descriptions)

| Item | Evidence |
|---|---|
| Every element with a click action has text or a description, and a click action; a missing label is a test failure (`Interactive element without text or description`). | `LayoutChecks.problems` inside `LayoutAccessibilityTest` (all 26 states, 4 setups) |
| Control: "Siguiente diapositiva", "Diapositiva anterior", the counter as "Diapositiva 1 de 21. Toca para elegir otra", the clock as "Tiempo transcurrido 00:10. Mantén pulsado para pausar", all found by description and enabled during alerts. | `PresentaFlowTest` |
| Setup steppers: "Más diapositivas", "Siguiente número" in the selector, and the values "20 diapositivas", "15 minutos". | `PresentaFlowTest` |
| Pairing: the code summary "Código: 0 0 0 0 0 0. Dígito 1 de 6 seleccionado" and "Dígito 1 de 6: 0"; buttons "Bajar dígito", "Subir dígito", "Dígito anterior", "Siguiente". | `PairingUiTest` (`the_code_summary_and_the_selected_digit_have_spoken_descriptions`) |
| The AI tip keeps the same accessibility description as the usual wording. | `AiTipLayoutTest` |
| The Control ring is exposed as a progress range (elapsed over planned). | Set in `ControlScreen.kt`; **no test asserts it** (listed under not verified). |

## 6. Traversal order

Verified only on the pairing screen: the clickable elements are in reading order ("Bajar dígito", "Subir dígito", "Dígito anterior", "Siguiente") in `PairingUiTest`. On the other screens the order is the one Compose derives from the layout; no test asserts it, and no TalkBack run confirms it.

## 7. Color and redundancy

| Rule | Evidence |
|---|---|
| Color is never the only signal: every state and alert has text (a design rule of `ui-direction.md`; the alert texts are asserted by `AlertStatesTest`). The icons that accompany the states are visible in the 26 rendered states in `docs/ui-previews/` but no test asserts them. | `AlertStatesTest`, `LayoutAccessibilityTest` (all states render and are labelled). |
| Red is used for overtime only (the ring, the headline, the pill); every other alert, including the last minute, is amber. A tested rule. | `AlertStatesTest`, ADR-60. |
| Alerts are accompanied by a haptic pattern (warning: two short pulses; critical: one long then one short; failed command: one short pulse). | `HapticsCoordinatorTest` (patterns and when they play). Feel not verified. |

## 8. What is not verified

| Not verified | Why | How to close it |
|---|---|---|
| Real TalkBack (or any screen reader) on a watch: reading order, the wording as spoken, gestures with the reader on. | No device, and the tests inspect the semantics tree, not speech. | A physical Wear OS watch with TalkBack on; or the emulator with the reader turned on. |
| A real device: real fonts and rendering, brightness and contrast outdoors, the real rotary input. | Previews and layout tests come from Robolectric, so anti-aliasing and the typeface can differ. | A physical watch; visual review V1 to V12 of the validation guide (emulator) covers part of it. |
| How the vibration feels. | The emulator vibrates nothing you can feel; Logcat lines only. | A physical watch (guide rows 57 to 61 for the Logcat part). |
| Traversal order on screens other than pairing. | No test. | TalkBack run. |
| The ring's progress semantics. | No test. | TalkBack run, or add a test (a candidate improvement, not done in this phase). |
| Font scale above 1.3, and the real system scale on Small Round. | Tests stop at 1.3; the app caps the scale. | Emulator row 82 and 83 of the guide. |
| The Windows companion window: keyboard navigation, screen reader, contrast of its own palette, DPI scaling. | No automated accessibility checks for WPF; only an `AutomationProperties.LiveSetting` on its status lines. | Manual review with Narrator and keyboard only. |
| Color vision (for example protanopia) simulation. | Not done; the design relies on text and icons next to color. | A simulation on the previews. |
