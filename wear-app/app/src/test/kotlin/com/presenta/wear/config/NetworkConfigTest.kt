package com.presenta.wear.config

import java.io.File
import javax.xml.parsers.DocumentBuilderFactory
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.w3c.dom.Document
import org.w3c.dom.Element

/** Cleartext traffic is allowed for exactly three hosts, and the INTERNET permission is declared. */
class NetworkConfigTest {
    private val main = File("src/main")

    private fun parse(file: File): Document {
        assertTrue("${file.absolutePath} not found", file.isFile)
        return DocumentBuilderFactory.newInstance().apply { isNamespaceAware = true }.newDocumentBuilder().parse(file)
    }

    private val androidNs = "http://schemas.android.com/apk/res/android"

    @Test
    fun cleartext_is_allowed_only_for_the_emulator_alias_localhost_and_loopback() {
        val config = parse(File(main, "res/xml/network_security_config.xml")).documentElement

        val base = config.getElementsByTagName("base-config").item(0) as Element
        assertEquals("false", base.getAttribute("cleartextTrafficPermitted"))

        val domainConfigs = config.getElementsByTagName("domain-config")
        assertEquals("a single domain-config", 1, domainConfigs.length)
        val domainConfig = domainConfigs.item(0) as Element
        assertEquals("true", domainConfig.getAttribute("cleartextTrafficPermitted"))
        val domains = (0 until domainConfig.getElementsByTagName("domain").length).map {
            val domain = domainConfig.getElementsByTagName("domain").item(it) as Element
            assertEquals("no subdomains", "false", domain.getAttribute("includeSubdomains"))
            domain.textContent.trim()
        }
        assertEquals(listOf("10.0.2.2", "localhost", "127.0.0.1"), domains)
    }

    @Test
    fun no_wildcard_or_debug_overrides_widen_the_policy() {
        val text = File(main, "res/xml/network_security_config.xml").readText()

        assertTrue(!text.contains("debug-overrides"))
        assertTrue(!text.contains("*"))
        assertTrue(!text.contains("0.0.0.0"))
        assertEquals("only the base config and one domain config", 1, Regex("<base-config").findAll(text).count())
    }

    @Test
    fun the_manifest_points_to_the_config_and_declares_internet() {
        val manifest = parse(File(main, "AndroidManifest.xml")).documentElement

        val permissions = (0 until manifest.getElementsByTagName("uses-permission").length).map {
            (manifest.getElementsByTagName("uses-permission").item(it) as Element).getAttributeNS(androidNs, "name")
        }
        assertEquals(listOf("android.permission.INTERNET"), permissions)

        val application = manifest.getElementsByTagName("application").item(0) as Element
        assertEquals("@xml/network_security_config", application.getAttributeNS(androidNs, "networkSecurityConfig"))
        assertTrue("cleartext must not be enabled globally", application.getAttributeNS(androidNs, "usesCleartextTraffic").isEmpty())
    }

    @Test
    fun the_default_server_address_is_covered_by_the_policy() {
        val host = com.presenta.wear.data.remote.ServerDefaults.ADDRESS.removePrefix("ws://").substringBefore(':')

        assertTrue(File(main, "res/xml/network_security_config.xml").readText().contains(">$host<"))
    }

    @Test
    fun the_debug_address_override_does_not_exist_in_the_main_or_release_source_sets() {
        assertTrue(File("src/debug/kotlin/com/presenta/wear/ServerAddressOverride.kt").readText().contains("server_address.txt"))
        assertTrue(!File("src/release/kotlin/com/presenta/wear/ServerAddressOverride.kt").readText().contains("readText"))
        assertTrue(!File("src/main/kotlin/com/presenta/wear/ServerAddressOverride.kt").exists())
    }
}
