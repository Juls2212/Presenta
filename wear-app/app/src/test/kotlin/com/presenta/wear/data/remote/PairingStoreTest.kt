package com.presenta.wear.data.remote

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.PreferenceDataStoreFactory
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.stringPreferencesKey
import java.io.File
import java.nio.file.Files
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** DataStore held in memory: same interface, no file. */
private class InMemoryPreferencesDataStore : DataStore<Preferences> {
    private val state = MutableStateFlow(emptyPreferences())

    override val data: Flow<Preferences> = state

    override suspend fun updateData(transform: suspend (t: Preferences) -> Preferences): Preferences {
        val updated = transform(state.value)
        state.value = updated
        return updated
    }
}

/**
 * Most tests use an in-memory DataStore. One test uses a real file with a single write: DataStore cannot replace an
 * existing file on Windows' JVM (a second write fails to rename), although it does on Android, where the app runs.
 */
class PairingStoreTest {
    private val directory: File = Files.createTempDirectory("presenta-datastore").toFile()
    private val scopes = mutableListOf<CoroutineScope>()

    private fun fileStore(file: File, override: String? = null): DataStorePairingStore {
        val scope = CoroutineScope(Dispatchers.IO + Job()).also { scopes += it }
        return DataStorePairingStore(PreferenceDataStoreFactory.create(scope = scope) { file }, override)
    }

    @After
    fun tearDown() {
        scopes.forEach { it.cancel() }
        directory.deleteRecursively()
    }

    @Test
    fun a_new_store_has_no_code_and_the_default_emulator_address() = runBlocking {
        val settings = DataStorePairingStore(InMemoryPreferencesDataStore()).load()

        assertNull(settings.code)
        assertEquals("ws://10.0.2.2:8765", settings.serverAddress)
        assertEquals(ServerDefaults.ADDRESS, settings.serverAddress)
    }

    @Test
    fun a_saved_code_comes_back_and_the_address_is_stored_with_it() = runBlocking {
        val dataStore = InMemoryPreferencesDataStore()
        val store = DataStorePairingStore(dataStore)

        store.saveCode("482913")

        assertEquals("482913", store.load().code)
        assertEquals("ws://10.0.2.2:8765", dataStore.data.first()[stringPreferencesKey("server_address")])
    }

    @Test
    fun a_newer_code_replaces_the_old_one() = runBlocking {
        val store = DataStorePairingStore(InMemoryPreferencesDataStore())
        store.saveCode("111111")

        store.saveCode("222222")

        assertEquals("222222", store.load().code)
    }

    @Test
    fun a_stored_address_is_kept_when_a_new_code_is_saved() = runBlocking {
        val dataStore = InMemoryPreferencesDataStore()
        dataStore.updateData { it.toMutablePreferences().apply { set(stringPreferencesKey("server_address"), "ws://example.local:9000") } }
        val store = DataStorePairingStore(dataStore)

        store.saveCode("333333")

        assertEquals("ws://example.local:9000", store.load().serverAddress)
    }

    @Test
    fun a_debug_override_wins_but_is_not_stored() = runBlocking {
        val dataStore = InMemoryPreferencesDataStore()
        val store = DataStorePairingStore(dataStore, addressOverride = "ws://localhost:8765")
        store.saveCode("444444")

        assertEquals("ws://localhost:8765", store.load().serverAddress)
        assertEquals("ws://10.0.2.2:8765", dataStore.data.first()[stringPreferencesKey("server_address")])
    }

    @Test
    fun the_code_and_the_address_survive_a_restart_through_a_real_file() = runBlocking {
        val file = File(directory, "presenta.preferences_pb")
        fileStore(file).saveCode("555666") // a single write
        scopes.forEach { it.cancel() } // close the first data store before opening the file again
        scopes.clear()

        val settings = fileStore(file).load()

        assertEquals("555666", settings.code)
        assertEquals(ServerDefaults.ADDRESS, settings.serverAddress)
    }

    @Test
    fun the_in_memory_pairing_store_used_by_other_tests_behaves_the_same() = runBlocking {
        val fake = InMemoryPairingStore(code = null)

        assertNull(fake.load().code)
        fake.saveCode("777777")
        assertEquals("777777", fake.load().code)
    }
}
