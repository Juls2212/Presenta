package com.presenta.wear.ui.preview

import androidx.compose.runtime.Composable
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.tooling.preview.PreviewParameter
import androidx.wear.tooling.preview.devices.WearDevices

/** Open this file in Android Studio and use the Compose preview: one preview per screen and state. */
@Preview(device = WearDevices.LARGE_ROUND, showBackground = true, backgroundColor = 0xFF000000, name = "Large Round")
@Composable
fun LargeRoundPreview(@PreviewParameter(PreviewCaseProvider::class) case: PreviewCase) {
    case.content()
}

@Preview(device = WearDevices.SMALL_ROUND, showBackground = true, backgroundColor = 0xFF000000, name = "Small Round")
@Composable
fun SmallRoundPreview(@PreviewParameter(PreviewCaseProvider::class) case: PreviewCase) {
    case.content()
}
