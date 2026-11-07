package com.presenta.wear.data.remote

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import kotlinx.coroutines.flow.first

/** Default companion address for the Wear OS emulator: the emulator's alias for the host's loopback. */
object ServerDefaults {
    const val ADDRESS = "ws://10.0.2.2:8765"
}

data class PairingSettings(val code: String?, val serverAddress: String)

/**
 * Persistent pairing settings. The code is a short LAN pairing code shown by the companion, not a credential
 * (it changes whenever the companion restarts and only gates a local connection); it is stored unencrypted.
 */
interface PairingStore {
    suspend fun load(): PairingSettings

    suspend fun saveCode(code: String)
}

/**
 * [PairingStore] on DataStore Preferences. The server address is stored too (written with its default together with the first code);
 * [addressOverride] (debug builds only, see ServerAddressOverride) takes precedence without being stored.
 */
class DataStorePairingStore(
    private val dataStore: DataStore<Preferences>,
    private val addressOverride: String? = null,
) : PairingStore {
    override suspend fun load(): PairingSettings {
        val preferences = dataStore.data.first()
        return PairingSettings(
            code = preferences[CODE_KEY],
            serverAddress = addressOverride ?: preferences[ADDRESS_KEY] ?: ServerDefaults.ADDRESS,
        )
    }

    /** Saves the code and, in the same write, the server address if none is stored yet. */
    override suspend fun saveCode(code: String) {
        dataStore.edit {
            it[CODE_KEY] = code
            if (it[ADDRESS_KEY] == null) it[ADDRESS_KEY] = ServerDefaults.ADDRESS
        }
    }

    private companion object {
        val CODE_KEY = stringPreferencesKey("pairing_code")
        val ADDRESS_KEY = stringPreferencesKey("server_address")
    }
}
