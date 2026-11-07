package com.presenta.wear.ui.navigation

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalResources
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.viewmodel.initializer
import androidx.lifecycle.viewmodel.viewModelFactory
import androidx.navigation.NavBackStackEntry
import androidx.navigation.NavHostController
import androidx.navigation.NavType
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.navArgument
import androidx.wear.compose.navigation.SwipeDismissableNavHost
import androidx.wear.compose.navigation.composable
import androidx.wear.compose.navigation.rememberSwipeDismissableNavController
import com.presenta.wear.AppContainer
import com.presenta.wear.format.HistoryTimeFormatter
import com.presenta.wear.format.PaceFormat
import com.presenta.wear.format.StringMapper
import com.presenta.wear.ui.components.ScreenFrame
import com.presenta.wear.ui.screens.control.ControlActions
import com.presenta.wear.ui.screens.control.ControlScreen
import com.presenta.wear.ui.screens.history.HistoryRowUi
import com.presenta.wear.ui.screens.history.HistoryScreen
import com.presenta.wear.ui.screens.history.HistoryUiState
import com.presenta.wear.model.PairingState
import com.presenta.wear.ui.screens.home.HomeScreen
import com.presenta.wear.ui.screens.pairing.PairingActions
import com.presenta.wear.ui.screens.pairing.PairingScreen
import com.presenta.wear.ui.screens.setup.SetupActions
import com.presenta.wear.ui.screens.setup.SetupScreen
import com.presenta.wear.ui.screens.summary.SummaryScreen
import com.presenta.wear.ui.screens.summary.SummaryUiState
import com.presenta.wear.ui.theme.PresentaTheme
import com.presenta.wear.ui.viewmodel.AppViewModel
import com.presenta.wear.ui.viewmodel.ControlViewModel
import com.presenta.wear.ui.viewmodel.HistoryLoadState
import com.presenta.wear.ui.viewmodel.HistoryViewModel
import com.presenta.wear.ui.viewmodel.HomeViewModel
import com.presenta.wear.ui.viewmodel.PairingViewModel
import com.presenta.wear.ui.viewmodel.SetupViewModel
import java.time.LocalDate
import kotlinx.coroutines.delay

object Routes {
    const val HOME = "home"
    const val PAIRING = "pairing"
    const val SETUP = "setup"
    const val CONTROL = "control"
    const val SUMMARY = "summary"
    const val HISTORY = "history"
    const val HISTORY_ITEM_ARG = "finishedAt"
    const val HISTORY_ITEM = "history/{$HISTORY_ITEM_ARG}"

    fun historyItem(finishedAtEpochMs: Long) = "history/$finishedAtEpochMs"
}

private const val MESSAGE_DURATION_MS = 3_000L
private const val SESSION_GONE_DELAY_MS = 600L

/**
 * Root of the watch app. Four navigation views: Home, Setup, Control, and Result/History. The alert is a state of
 * Control, never a destination. Swipe-to-dismiss is disabled on Control and Summary so a stray swipe cannot leave
 * a running presentation. Screens only depend on the repository through the view models.
 */
@Composable
fun PresentaApp(container: AppContainer, navController: NavHostController = rememberSwipeDismissableNavController()) {
    val appViewModel: AppViewModel = viewModel(factory = viewModelFactory { initializer { AppViewModel(container.repository) } })
    val homeViewModel: HomeViewModel = viewModel(factory = viewModelFactory { initializer { HomeViewModel(container.repository) } })
    val setupViewModel: SetupViewModel = viewModel(factory = viewModelFactory { initializer { SetupViewModel(container.repository) } })
    val controlViewModel: ControlViewModel = viewModel(
        factory = viewModelFactory {
            initializer { ControlViewModel(container.repository, container.monotonicClock, container.displayTicks) }
        },
    )
    val pairingViewModel: PairingViewModel = viewModel(factory = viewModelFactory { initializer { PairingViewModel(container.repository) } })
    val historyViewModel: HistoryViewModel = viewModel(factory = viewModelFactory { initializer { HistoryViewModel(container.repository) } })

    val mapper = StringMapper(LocalResources.current)
    val historyFormatter = remember(container.wallClock) { HistoryTimeFormatter(container.wallClock) }

    val sessionActive by appViewModel.sessionActive.collectAsState()
    val summary by appViewModel.lastSummary.collectAsState()
    val pairingState by appViewModel.pairing.collectAsState()
    val currentRoute = navController.currentBackStackEntryAsState().value?.destination?.route

    LaunchedEffect(sessionActive) {
        if (sessionActive && navController.currentDestination?.route != Routes.CONTROL) {
            navController.navigate(Routes.CONTROL) { popUpTo(Routes.HOME) }
        }
    }
    // The pairing screen appears when there is no code yet or the companion rejected it, and goes away once paired.
    LaunchedEffect(pairingState) {
        val route = navController.currentDestination?.route
        when (pairingState) {
            PairingState.NeedsCode, is PairingState.Rejected ->
                if (route != Routes.PAIRING) navController.navigate(Routes.PAIRING) { popUpTo(Routes.HOME) }
            PairingState.Paired -> if (route == Routes.PAIRING) navController.popBackStack(Routes.HOME, inclusive = false)
            PairingState.Connecting -> Unit
        }
    }
    LaunchedEffect(summary) {
        if (summary != null && navController.currentDestination?.route != Routes.SUMMARY) {
            navController.navigate(Routes.SUMMARY) { popUpTo(Routes.HOME) }
        }
    }

    PresentaTheme {
        SwipeDismissableNavHost(
            navController = navController,
            startDestination = Routes.HOME,
            userSwipeEnabled = currentRoute != Routes.CONTROL && currentRoute != Routes.SUMMARY,
        ) {
            composable(Routes.HOME) {
                val state by homeViewModel.state.collectAsState()
                HomeScreen(
                    state = state,
                    onNewPresentation = { navController.navigate(Routes.SETUP) },
                    onHistory = { navController.navigate(Routes.HISTORY) },
                    onRetry = {
                        when (pairingState) {
                            PairingState.NeedsCode, is PairingState.Rejected -> navController.navigate(Routes.PAIRING)
                            else -> homeViewModel.retry()
                        }
                    },
                )
            }
            composable(Routes.PAIRING) {
                val state by pairingViewModel.state.collectAsState()
                PairingScreen(
                    state = state,
                    actions = PairingActions(
                        onIncrease = pairingViewModel::increase,
                        onDecrease = pairingViewModel::decrease,
                        onPreviousDigit = pairingViewModel::previousDigit,
                        onNextOrConnect = pairingViewModel::nextOrConnect,
                    ),
                )
            }
            composable(Routes.SETUP) {
                val state by setupViewModel.state.collectAsState()
                val sessionName = mapper.sessionName(LocalDate.now(container.wallClock))
                SetupScreen(
                    state = state,
                    errorText = state.error?.let(mapper::error),
                    actions = SetupActions(
                        onSlidesDecrease = setupViewModel::decreaseSlides,
                        onSlidesIncrease = setupViewModel::increaseSlides,
                        onMinutesDecrease = setupViewModel::decreaseMinutes,
                        onMinutesIncrease = setupViewModel::increaseMinutes,
                        onStart = { setupViewModel.start(sessionName) {} },
                    ),
                )
            }
            composable(Routes.CONTROL) {
                val state by controlViewModel.state.collectAsState()
                val current = state
                if (current == null) {
                    // The session ended without a summary (for example it was interrupted while the watch was away).
                    LaunchedEffect(Unit) {
                        delay(SESSION_GONE_DELAY_MS)
                        if (appViewModel.lastSummary.value == null) navController.popBackStack(Routes.HOME, inclusive = false)
                    }
                    ScreenFrame {}
                } else {
                    LaunchedEffect(current.message) {
                        if (current.message != null) {
                            delay(MESSAGE_DURATION_MS)
                            controlViewModel.clearMessage()
                        }
                    }
                    ControlScreen(
                        state = current,
                        actions = ControlActions(
                            onNext = controlViewModel::next,
                            onPrevious = controlViewModel::previous,
                            onTogglePause = controlViewModel::togglePause,
                            onOpenSelector = controlViewModel::openSelector,
                            onSelectorDecrease = controlViewModel::selectorDecrease,
                            onSelectorIncrease = controlViewModel::selectorIncrease,
                            onSelectorConfirm = controlViewModel::selectorConfirm,
                            onSelectorCancel = controlViewModel::selectorCancel,
                            onOpenFinish = controlViewModel::openFinish,
                            onFinishCancel = controlViewModel::finishCancel,
                            onFinishConfirm = controlViewModel::finishConfirm,
                            onAcknowledge = controlViewModel::acknowledgeAlert,
                        ),
                    )
                }
            }
            composable(Routes.SUMMARY) {
                val item = summary
                if (item == null) {
                    ScreenFrame {}
                } else {
                    SummaryScreen(
                        state = SummaryUiState(item, readOnly = false),
                        onDone = {
                            appViewModel.consumeSummary()
                            navController.popBackStack(Routes.HOME, inclusive = false)
                        },
                    )
                }
            }
            composable(Routes.HISTORY) {
                LaunchedEffect(Unit) { historyViewModel.load() }
                val loadState by historyViewModel.state.collectAsState()
                val uiState = when (val s = loadState) {
                    HistoryLoadState.Loading -> HistoryUiState.Loading
                    is HistoryLoadState.Failed -> HistoryUiState.Failed(mapper.error(s.code))
                    is HistoryLoadState.Loaded ->
                        if (s.items.isEmpty()) {
                            HistoryUiState.Empty
                        } else {
                            HistoryUiState.Loaded(
                                s.items.map { item ->
                                    HistoryRowUi(
                                        item = item,
                                        dayText = mapper.dayLabel(historyFormatter.dayLabel(item.finishedAtEpochMs)),
                                        timeText = historyFormatter.timeOfDay(item.finishedAtEpochMs),
                                        paceText = PaceFormat.slidesPerMinute(item.averageSlidesPerMinute),
                                    )
                                },
                            )
                        }
                }
                HistoryScreen(
                    state = uiState,
                    onOpen = { navController.navigate(Routes.historyItem(it.finishedAtEpochMs)) },
                    onBack = { navController.popBackStack() },
                )
            }
            composable(
                route = Routes.HISTORY_ITEM,
                arguments = listOf(navArgument(Routes.HISTORY_ITEM_ARG) { type = NavType.LongType }),
            ) { entry: NavBackStackEntry ->
                val key = entry.arguments?.getLong(Routes.HISTORY_ITEM_ARG) ?: 0L
                val item = historyViewModel.find(key)
                if (item == null) {
                    ScreenFrame {}
                } else {
                    SummaryScreen(
                        state = SummaryUiState(item, readOnly = true),
                        onDone = { navController.popBackStack() },
                    )
                }
            }
        }
    }
}
