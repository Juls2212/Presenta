package com.presenta.wear.support

import java.io.File
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/** Access to the language-neutral contracts in `shared-contracts/` (the test working directory is `wear-app/app`). */
object Contracts {
    val root: File = File("../../shared-contracts").also { check(it.isDirectory) { "shared-contracts not found at ${it.absolutePath}" } }

    val examples: List<File> get() = jsonFiles(File(root, "examples"))

    val invalidExamples: List<File> get() = jsonFiles(File(root, "examples/invalid"))

    val schemaMessageTypes: Set<String>
        get() = File(root, "schemas").listFiles().orEmpty()
            .map { it.name.removeSuffix(".schema.json") }
            .filter { it != "common" && it != "envelope" && it.isNotEmpty() }
            .toSet()

    /** The values of an enum definition in common.schema.json. */
    fun schemaEnum(name: String): List<String> =
        Json.parseToJsonElement(File(root, "schemas/common.schema.json").readText())
            .jsonObject.getValue("\$defs").jsonObject.getValue(name).jsonObject.getValue("enum").jsonArray
            .map { it.jsonPrimitive.content }

    /** The message type is the part of the file name before the first "--". */
    fun typeOf(file: File): String = file.name.removeSuffix(".json").substringBefore("--")

    private fun jsonFiles(dir: File): List<File> = dir.listFiles { f -> f.isFile && f.name.endsWith(".json") }.orEmpty().sortedBy { it.name }

    /** Structural equality where numbers are compared by value (`1` equals `1.0`). */
    fun equivalent(a: JsonElement, b: JsonElement): Boolean = when {
        a is JsonObject && b is JsonObject -> a.keys == b.keys && a.keys.all { equivalent(a.getValue(it), b.getValue(it)) }
        a is JsonArray && b is JsonArray -> a.size == b.size && a.indices.all { equivalent(a[it], b[it]) }
        a is JsonPrimitive && b is JsonPrimitive -> {
            val x = a.content.toDoubleOrNull()
            val y = b.content.toDoubleOrNull()
            if (!a.isString && !b.isString && x != null && y != null) x == y else a.isString == b.isString && a.content == b.content
        }
        else -> false
    }
}
