package com.presenta.wear

import android.app.Application
import androidx.datastore.preferences.preferencesDataStore
import com.presenta.wear.data.MonotonicClock
import com.presenta.wear.data.PresentaRepository
import com.presenta.wear.data.SystemMonotonicClock
import com.presenta.wear.data.fake.FakePresentaRepository
import com.presenta.wear.data.remote.DataStorePairingStore
import com.presenta.wear.data.remote.OkHttpSocketFactory
import com.presenta.wear.data.remote.RemotePresentaRepository
import java.time.Clock
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow

/** Hand-written dependency holder (no DI framework). Tests build their own with fake clocks. */
class AppContainer(
    val repository: PresentaRepository,
    val monotonicClock: MonotonicClock,
    val wallClock: Clock,
    /** Display refresh signal for the live clock on Control. Tests pass a signal they control. */
    val displayTicks: Flow<Unit> = defaultDisplayTicks(),
) {
    companion object {
        fun defaultDisplayTicks(): Flow<Unit> = flow {
            while (true) {
                emit(Unit)
                delay(250)
            }
        }
    }
}

private val android.content.Context.presentaDataStore by preferencesDataStore(name = "presenta")

class PresentaApplication : Application() {
    /**
     * The only composition point. By default the app talks to the companion (RemotePresentaRepository);
     * build with `-PuseFakeRepository=true` to run it on the fake repository instead.
     */
    val container: AppContainer by lazy {
        val wall = Clock.systemDefaultZone()
        val repository: PresentaRepository = if (BuildConfig.USE_FAKE_REPOSITORY) {
            FakePresentaRepository(SystemMonotonicClock, wall)
        } else {
            RemotePresentaRepository(
                scope = CoroutineScope(SupervisorJob() + Dispatchers.Default),
                sockets = OkHttpSocketFactory(),
                store = DataStorePairingStore(presentaDataStore, ServerAddressOverride.read(filesDir)),
                clock = SystemMonotonicClock,
            )
        }
        AppContainer(repository = repository, monotonicClock = SystemMonotonicClock, wallClock = wall)
    }
}
