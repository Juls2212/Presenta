package com.presenta.wear

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import com.presenta.wear.ui.navigation.PresentaApp

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val container = (application as PresentaApplication).container
        setContent { PresentaApp(container) }
    }
}
