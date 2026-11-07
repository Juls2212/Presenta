package com.presenta.wear.ui.preview

import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalResources
import androidx.compose.ui.tooling.preview.PreviewParameterProvider
import com.presenta.wear.data.fake.FakeData
import com.presenta.wear.format.HistoryTimeFormatter
import com.presenta.wear.format.PaceFormat
import com.presenta.wear.format.StringMapper
import com.presenta.wear.model.ConnectionState
import com.presenta.wear.model.ErrorCode
import com.presenta.wear.model.SessionSnapshot
import com.presenta.wear.model.SessionStatus
import com.presenta.wear.ui.screens.control.ControlActions
import com.presenta.wear.ui.screens.control.ControlOverlay
import com.presenta.wear.ui.screens.control.ControlScreen
import com.presenta.wear.ui.screens.control.ControlUiState
import com.presenta.wear.ui.screens.history.HistoryRowUi
import com.presenta.wear.ui.screens.history.HistoryScreen
import com.presenta.wear.ui.screens.history.HistoryUiState
import com.presenta.wear.ui.screens.home.HomeScreen
import com.presenta.wear.ui.screens.home.HomeUiState
import com.presenta.wear.ui.screens.pairing.PairingActions
import com.presenta.wear.ui.screens.pairing.PairingScreen
import com.presenta.wear.ui.screens.pairing.PairingUiState
import com.presenta.wear.ui.screens.setup.SetupActions
import com.presenta.wear.ui.screens.setup.SetupScreen
import com.presenta.wear.ui.screens.setup.SetupUiState
import com.presenta.wear.ui.screens.summary.SummaryScreen
import com.presenta.wear.ui.screens.summary.SummaryUiState
import com.presenta.wear.ui.theme.PresentaTheme
import com.presenta.wear.ui.viewmodel.ControlStateFactory
import java.time.Clock
import java.time.Instant
import java.time.ZoneId

/** One screen or state, rendered with fake data. Used by `@Preview` functions and by the screenshot tests. */
class PreviewCase(val name: String, val content: @Composable () -> Unit)

/** Every screen and state of the approved mockups and of the derived variants. */
object PreviewCatalog {
    /** Fixed "now" so day labels (Hoy, Ayer, 08 Oct) are stable. */
    val fixedClock: Clock = Clock.fixed(Instant.parse("2026-10-31T20:00:00Z"), ZoneId.of("America/Bogota"))

    private val noActions = ControlActions({}, {}, {}, {}, {}, {}, {}, {}, {}, {}, {}, {})
    private val noSetupActions = SetupActions({}, {}, {}, {}, {})
    private val noPairingActions = PairingActions({}, {}, {}, {})

    private fun control(
        snapshot: SessionSnapshot,
        connection: ConnectionState = ConnectionState.Connected,
        overlay: ControlOverlay = ControlOverlay.None,
        message: ErrorCode? = null,
    ): ControlUiState = requireNotNull(
        ControlStateFactory.build(
            connection = connection,
            status = SessionStatus.Active(snapshot, receivedAtMs = 0),
            nowMs = 0,
            overlay = overlay,
            selectorSlide = snapshot.currentSlide,
            message = message,
        ),
    )

    private fun case(name: String, content: @Composable () -> Unit) = PreviewCase(name) { PresentaTheme { content() } }

    val cases: List<PreviewCase> = listOf(
        case("01-home-connected") { HomeScreen(HomeUiState(ConnectionState.Connected), {}, {}, {}) },
        case("02-setup") { SetupScreen(SetupUiState(), null, noSetupActions) },
        case("03-control-normal") { ControlScreen(control(FakeData.controlNormal), noActions) },
        case("04-control-alert") { ControlScreen(control(FakeData.controlAlert), noActions) },
        case("05-summary") { SummaryScreen(SummaryUiState(FakeData.summaryWithinTime, readOnly = false), {}) },
        case("06-history") { HistoryPreview() },
        case("06b-history-empty") { HistoryScreen(HistoryUiState.Empty, {}, {}) },
        case("07-home-disconnected") { HomeScreen(HomeUiState(ConnectionState.Disconnected), {}, {}, {}) },
        case("07b-home-reconnecting") { HomeScreen(HomeUiState(ConnectionState.Reconnecting), {}, {}, {}) },
        case("03b-control-calculating") { ControlScreen(control(FakeData.controlCalculating), noActions) },
        case("03c-control-paused") { ControlScreen(control(FakeData.controlPaused), noActions) },
        case("03d-control-overtime") { ControlScreen(control(FakeData.controlOvertime), noActions) },
        case("03e-control-reconnecting") {
            ControlScreen(control(FakeData.controlNormal, connection = ConnectionState.Reconnecting), noActions)
        },
        case("03f-control-error-message") {
            ControlScreen(control(FakeData.controlNormal, message = ErrorCode.InvalidMessage), noActions)
        },
        case("04b-control-alert-behind") { ControlScreen(control(FakeData.controlBehindAlert), noActions) },
        case("04c-control-alert-overtime") { ControlScreen(control(FakeData.controlOvertimeAlert), noActions) },
        case("08-slide-selector") {
            ControlScreen(control(FakeData.controlNormal, overlay = ControlOverlay.SlideSelector), noActions)
        },
        case("09-finish-confirmation") {
            ControlScreen(control(FakeData.controlNormal, overlay = ControlOverlay.FinishConfirmation), noActions)
        },
        case("05b-summary-over-time") { SummaryScreen(SummaryUiState(FakeData.summaryOverTime, readOnly = false), {}) },
        case("05c-summary-interrupted") { SummaryScreen(SummaryUiState(FakeData.summaryInterrupted, readOnly = false), {}) },
        case("05d-summary-read-only") { SummaryScreen(SummaryUiState(FakeData.summaryWithinTime, readOnly = true), {}) },
        case("10-pairing") { PairingScreen(PairingUiState(digits = listOf(4, 8, 2, 0, 0, 0), index = 3), noPairingActions) },
        case("10b-pairing-wrong-code") {
            PairingScreen(PairingUiState(digits = listOf(4, 8, 2, 9, 1, 3), index = 5, error = ErrorCode.InvalidPairingCode), noPairingActions)
        },
        case("10c-pairing-locked-out") {
            PairingScreen(PairingUiState(digits = listOf(4, 8, 2, 9, 1, 3), index = 5, error = ErrorCode.PairingLockedOut), noPairingActions)
        },
        case("10d-pairing-connecting") {
            PairingScreen(PairingUiState(digits = listOf(4, 8, 2, 9, 1, 3), index = 5, connecting = true), noPairingActions)
        },
        case("02b-setup-limits") { SetupScreen(SetupUiState(slides = 200, minutes = 1), null, noSetupActions) },
    )

    @Composable
    private fun HistoryPreview() {
        val mapper = StringMapper(LocalResources.current)
        val formatter = HistoryTimeFormatter(fixedClock)
        val rows = FakeData.history(fixedClock).map { item ->
            HistoryRowUi(
                item = item,
                dayText = mapper.dayLabel(formatter.dayLabel(item.finishedAtEpochMs)),
                timeText = formatter.timeOfDay(item.finishedAtEpochMs),
                paceText = PaceFormat.slidesPerMinute(item.averageSlidesPerMinute),
            )
        }
        HistoryScreen(HistoryUiState.Loaded(rows), {}, {})
    }
}

class PreviewCaseProvider : PreviewParameterProvider<PreviewCase> {
    override val values: Sequence<PreviewCase> = PreviewCatalog.cases.asSequence()
}
