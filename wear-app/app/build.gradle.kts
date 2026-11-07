plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.roborazzi)
    alias(libs.plugins.kotlin.serialization)
}

// ---- opt-in cross-stack test: real Kotlin client against the real C# server (needs the .NET SDK) ----
val crossStackRequested = gradle.startParameter.taskNames.any { it.endsWith("crossStackTest") }
val devHostProject = rootProject.file("../windows-companion/tools/Presenta.DevHost")
val devHostDll = devHostProject.resolve("bin/Debug/net10.0/Presenta.DevHost.dll")

android {
    namespace = "com.presenta.wear"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.presenta.wear"
        minSdk = 30
        targetSdk = 36
        versionCode = 1
        versionName = "0.5.0"

        // Build-time switch: pass -PuseFakeRepository=true to run the app on the fake repository (previews and
        // tests always use the fake directly). The default is the real repository that talks to the companion.
        buildConfigField("boolean", "USE_FAKE_REPOSITORY", (project.findProperty("useFakeRepository") ?: "false").toString())
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    lint {
        abortOnError = true
        warningsAsErrors = false
    }

    testOptions {
        unitTests {
            isIncludeAndroidResources = true
            isReturnDefaultValues = true
            all { test ->
                // The cross-stack test needs the .NET SDK, so it only runs through the opt-in task:
                //   gradlew :app:crossStackTest
                if (crossStackRequested) {
                    test.filter.includeTestsMatching("*CrossStackTest")
                    test.systemProperty("presenta.devhost.dll", devHostDll.absolutePath)
                    test.outputs.upToDateWhen { false }
                } else {
                    test.filter.excludeTestsMatching("*CrossStackTest")
                }
                // Robolectric for Android 16 (SDK 36) reaches into JDK internals; JDK 21 needs this export.
                test.jvmArgs("--add-exports=java.base/jdk.internal.access=ALL-UNNAMED")
                test.maxHeapSize = "2g"
            }
        }
    }
}

dependencies {
    implementation(libs.wear.compose.material3)
    implementation(libs.wear.compose.foundation)
    implementation(libs.wear.compose.navigation)
    implementation(libs.androidx.navigation.compose)
    implementation(libs.compose.ui)
    implementation(libs.compose.ui.tooling.preview)
    implementation(libs.wear.tooling.preview)
    implementation(libs.activity.compose)
    implementation(libs.lifecycle.viewmodel.compose)
    implementation(libs.lifecycle.runtime.compose)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.okhttp)
    implementation(libs.datastore.preferences)

    debugImplementation(libs.compose.ui.tooling)
    debugImplementation(libs.compose.ui.test.manifest)

    testImplementation(libs.junit)
    testImplementation(libs.robolectric)
    testImplementation(libs.androidx.test.ext.junit)
    testImplementation(libs.compose.ui.test.junit4)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.roborazzi)
    testImplementation(libs.roborazzi.compose)
    testImplementation(libs.roborazzi.junit.rule)
    testImplementation(libs.okhttp.mockwebserver)
}

val buildDevHost = tasks.register<Exec>("buildDevHost") {
    group = "verification"
    description = "Builds the console host of the Windows companion used by the cross-stack test."
    workingDir = devHostProject
    commandLine("dotnet", "build", "--nologo", "-v", "q")
}

tasks.register("crossStackTest") {
    group = "verification"
    description = "Runs the real watch client against the real companion server (opt-in; needs the .NET SDK)."
    dependsOn(buildDevHost, "testDebugUnitTest")
}

// The test task must run after the host is built.
tasks.matching { it.name == "testDebugUnitTest" }.configureEach {
    if (crossStackRequested) {
        dependsOn(buildDevHost)
    }
}
