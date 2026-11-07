package com.presenta.wear.ui.components

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Fill
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

enum class IconKind {
    Logo,
    Monitor,
    MonitorOff,
    Warning,
    Check,
    Clock,
    History,
    Refresh,
    ArrowBack,
    ArrowForward,
    Plus,
    Minus,
    Chevron,
    DoubleChevron,
    Pause,
    Play,
}

/** Decorative icon drawn with simple strokes (no icon library). Accessibility text belongs to the parent. */
@Composable
fun PresentaIcon(kind: IconKind, tint: Color, modifier: Modifier = Modifier, size: Dp = 20.dp) {
    Canvas(modifier.size(size)) { drawIcon(kind, tint) }
}

private fun DrawScope.drawIcon(kind: IconKind, tint: Color) {
    val s = size.minDimension
    val stroke = Stroke(width = s * 0.1f, cap = StrokeCap.Round, join = StrokeJoin.Round)

    fun p(x: Float, y: Float) = Offset(x * s, y * s)

    fun line(x1: Float, y1: Float, x2: Float, y2: Float) =
        drawLine(tint, p(x1, y1), p(x2, y2), strokeWidth = stroke.width, cap = StrokeCap.Round)

    fun polyline(vararg points: Pair<Float, Float>) {
        val path = Path().apply {
            moveTo(points[0].first * s, points[0].second * s)
            for (i in 1 until points.size) lineTo(points[i].first * s, points[i].second * s)
        }
        drawPath(path, tint, style = stroke)
    }

    when (kind) {
        IconKind.ArrowBack -> {
            line(0.86f, 0.5f, 0.14f, 0.5f)
            polyline(0.44f to 0.2f, 0.14f to 0.5f, 0.44f to 0.8f)
        }
        IconKind.ArrowForward -> {
            line(0.14f, 0.5f, 0.86f, 0.5f)
            polyline(0.56f to 0.2f, 0.86f to 0.5f, 0.56f to 0.8f)
        }
        IconKind.Plus -> {
            line(0.2f, 0.5f, 0.8f, 0.5f)
            line(0.5f, 0.2f, 0.5f, 0.8f)
        }
        IconKind.Minus -> line(0.2f, 0.5f, 0.8f, 0.5f)
        IconKind.Check -> polyline(0.18f to 0.52f, 0.42f to 0.76f, 0.84f to 0.28f)
        IconKind.Chevron -> polyline(0.36f to 0.18f, 0.66f to 0.5f, 0.36f to 0.82f)
        IconKind.DoubleChevron -> {
            polyline(0.16f to 0.22f, 0.44f to 0.5f, 0.16f to 0.78f)
            polyline(0.5f to 0.22f, 0.78f to 0.5f, 0.5f to 0.78f)
        }
        IconKind.Pause -> {
            line(0.34f, 0.2f, 0.34f, 0.8f)
            line(0.66f, 0.2f, 0.66f, 0.8f)
        }
        IconKind.Play -> {
            val play = Path().apply {
                moveTo(0.28f * s, 0.14f * s)
                lineTo(0.84f * s, 0.5f * s)
                lineTo(0.28f * s, 0.86f * s)
                close()
            }
            drawPath(play, tint, style = Fill)
        }
        IconKind.Clock -> {
            drawCircle(tint, radius = s * 0.4f, center = p(0.5f, 0.5f), style = stroke)
            polyline(0.5f to 0.28f, 0.5f to 0.5f, 0.66f to 0.6f)
        }
        IconKind.Warning -> {
            val path = Path().apply {
                moveTo(0.5f * s, 0.12f * s)
                lineTo(0.92f * s, 0.84f * s)
                lineTo(0.08f * s, 0.84f * s)
                close()
            }
            drawPath(path, tint, style = stroke)
            line(0.5f, 0.4f, 0.5f, 0.6f)
            drawCircle(tint, radius = s * 0.045f, center = p(0.5f, 0.72f), style = Fill)
        }
        IconKind.Monitor, IconKind.MonitorOff, IconKind.Logo -> {
            drawRoundRect(
                tint,
                topLeft = p(0.08f, 0.16f),
                size = Size(0.84f * s, 0.58f * s),
                cornerRadius = CornerRadius(s * 0.1f),
                style = stroke,
            )
            line(0.5f, 0.74f, 0.5f, 0.88f)
            line(0.3f, 0.88f, 0.7f, 0.88f)
            if (kind == IconKind.MonitorOff) line(0.1f, 0.06f, 0.9f, 0.94f)
            if (kind == IconKind.Logo) {
                val play = Path().apply {
                    moveTo(0.42f * s, 0.3f * s)
                    lineTo(0.64f * s, 0.45f * s)
                    lineTo(0.42f * s, 0.6f * s)
                    close()
                }
                drawPath(play, tint, style = Fill)
            }
        }
        IconKind.History -> {
            drawArc(
                tint,
                startAngle = 130f,
                sweepAngle = 280f,
                useCenter = false,
                topLeft = p(0.12f, 0.12f),
                size = Size(0.76f * s, 0.76f * s),
                style = stroke,
            )
            polyline(0.5f to 0.3f, 0.5f to 0.5f, 0.64f to 0.58f)
            polyline(0.1f to 0.28f, 0.14f to 0.5f, 0.34f to 0.44f)
        }
        IconKind.Refresh -> {
            drawArc(
                tint,
                startAngle = -40f,
                sweepAngle = 290f,
                useCenter = false,
                topLeft = p(0.14f, 0.14f),
                size = Size(0.72f * s, 0.72f * s),
                style = stroke,
            )
            polyline(0.62f to 0.1f, 0.84f to 0.24f, 0.68f to 0.42f)
        }
    }
}
