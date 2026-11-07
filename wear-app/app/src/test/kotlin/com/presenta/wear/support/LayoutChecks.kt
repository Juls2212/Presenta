package com.presenta.wear.support

import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.SemanticsNodeInteractionsProvider
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.test.onNodeWithTag
import kotlin.math.hypot

/** Automated accessibility and round-screen checks used by the UI tests. */
object LayoutChecks {
    private val hasText = SemanticsMatcher.keyIsDefined(SemanticsProperties.Text)

    private fun SemanticsNode.label(): String {
        val text = config.getOrNull(SemanticsProperties.Text)?.joinToString { it.text }
        val description = config.getOrNull(SemanticsProperties.ContentDescription)?.joinToString()
        return description ?: text ?: "<no label>"
    }

    /** Problems found, empty when everything is fine. [dpFactor] is pixels per dp (2 in the harness). */
    fun problems(
        provider: SemanticsNodeInteractionsProvider,
        sizeDp: Int,
        dpFactor: Float = 2f,
        allowNarrowTouchTargets: Boolean = false,
        minHeightFor: (label: String) -> Float = { MIN_TOUCH_DP },
        textBottomFraction: Float = 1f,
    ): List<String> {
        val problems = mutableListOf<String>()
        val frame = provider.onNodeWithTag(SCREEN_TAG).fetchSemanticsNode().boundsInRoot
        val radius = frame.width / 2f
        val center = frame.center

        // 1. Every text stays inside the circle, and texts do not overlap each other.
        val texts = provider.onAllNodes(hasText, useUnmergedTree = true).fetchSemanticsNodes()
            .filter { it.boundsInRoot.width > 0 && it.boundsInRoot.height > 0 }
            // Scrolling content (history list, summary on Small Round) is only checked in the part shown first.
            .filter { it.boundsInRoot.bottom <= frame.top + frame.height * textBottomFraction }
        for (node in texts) {
            // A text that was cut off (clipped or ellipsized) is a defect even when its box is inside the circle.
            val layouts = mutableListOf<TextLayoutResult>()
            node.config.getOrNull(SemanticsActions.GetTextLayoutResult)?.action?.invoke(layouts)
            if (layouts.firstOrNull()?.hasVisualOverflow == true) problems += "Text is cut off at ${sizeDp}dp: '${node.label()}'"
            val r = node.boundsInRoot
            val corners = listOf(r.topLeft, r.topRight, r.bottomLeft, r.bottomRight)
            val outside = corners.any { hypot(it.x - center.x, it.y - center.y) > radius + 0.5f }
            if (outside) problems += "Text outside the round screen at ${sizeDp}dp: '${node.label()}' $r"
        }
        for (i in texts.indices) {
            for (j in i + 1 until texts.size) {
                val a = texts[i].boundsInRoot.deflate(1f)
                val b = texts[j].boundsInRoot.deflate(1f)
                if (a.overlaps(b)) problems += "Texts overlap: '${texts[i].label()}' and '${texts[j].label()}'"
            }
        }

        // 2. Every interactive element is labelled and has a large enough touch target.
        val clickable = provider.onAllNodes(hasClickAction()).fetchSemanticsNodes()
        for (node in clickable) {
            val label = node.label()
            if (label == "<no label>" || label.isBlank()) problems += "Interactive element without text or description: ${node.boundsInRoot}"
            val widthDp = node.boundsInRoot.width / dpFactor
            val heightDp = node.boundsInRoot.height / dpFactor
            // Items of a scrolling list can be partly outside the viewport; their visible bounds are smaller than they are.
            val clippedByViewport = node.boundsInRoot.height < node.size.height - 0.5f || node.boundsInRoot.width < node.size.width - 0.5f
            if (!clippedByViewport) {
                if (heightDp < minHeightFor(label) - 0.5f) problems += "Touch target too short (${heightDp}dp): '$label'"
                if (widthDp < MIN_TOUCH_DP - 0.5f && !allowNarrowTouchTargets) problems += "Touch target too narrow (${widthDp}dp): '$label'"
            }
            if (node.config.getOrNull(SemanticsActions.OnClick) == null && node.config.getOrNull(SemanticsActions.OnLongClick) == null) {
                problems += "No click action: '$label'"
            }
        }
        return problems
    }

    private fun Rect.deflate(amount: Float) = Rect(left + amount, top + amount, right - amount, bottom - amount)

    const val MIN_TOUCH_DP = 48f
}
