package com.presenta.wear.format

/**
 * Cleans the optional tip the companion sends in a recommendation's `freeText` (ADR-78). The companion already sends one
 * short line; this is the watch's own guard, so that anything odd falls back to the wording of the recommendation code.
 */
object RecommendationText {
    /** The protocol allows 80 characters (the schema's maxLength). */
    const val MAX_LENGTH = 80

    private val whitespace = Regex("\\s+")

    /** One plain line of 1 to [MAX_LENGTH] characters, or null when there is nothing safe to show. */
    fun clean(raw: String?): String? {
        if (raw == null) return null
        if (raw.any { it.isISOControl() && it != '\n' && it != '\r' && it != '\t' }) return null
        val line = raw.replace(whitespace, " ").trim()
        return line.takeIf { it.isNotEmpty() && it.length <= MAX_LENGTH }
    }
}
