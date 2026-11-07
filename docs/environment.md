# Environment

Results of read-only checks on the development machine. Nothing was installed.

| Item | Result |
|---|---|
| OS | Microsoft Windows 11 Pro, version 10.0.26200, 64-bit |
| JDK | OpenJDK 21.0.10 LTS (Eclipse Temurin); `JAVA_HOME` = `C:\Program Files\Eclipse Adoptium\jdk-21.0.10.7-hotspot` |
| Android Studio | Found at `C:\Program Files\Android\Android Studio`; build 261.26222.65.2613.15948027 |
| Other IDE found | IntelliJ IDEA 2025.3.3 (`C:\Program Files\JetBrains`) |
| `ANDROID_HOME` / `ANDROID_SDK_ROOT` | Not set |
| Android SDK path | `C:\Users\ASUS\AppData\Local\Android\Sdk` (exists) |
| Installed platforms | `android-37.0` |
| Build tools | 36.0.0 |
| System images | `android-37.0` / `android-wear-signed` / `x86_64` |
| `adb` | Not on `PATH`; present in the SDK at `platform-tools\adb.exe`, version 1.0.41 (37.0.1-15733141) |
| `emulator` | Not on `PATH`; present in the SDK at `emulator\emulator.exe`, version 37.1.11.0 |
| AVDs | `Wear_OS_Large_Round` (454 × 454, density 320, x86_64, target `android-37.0`, Play Store enabled) |
| Attached devices | None (`adb devices` listed no devices) |
| .NET SDK | 10.0.400 (`dotnet --list-sdks`) |
| .NET runtimes | Microsoft.NETCore.App 10.0.11, Microsoft.AspNetCore.App 10.0.11, Microsoft.WindowsDesktop.App 10.0.11 |
| WPF | Windows Desktop runtime 10.0.11 and the `Microsoft.WindowsDesktop.App.Ref` pack directory are present. `dotnet workload list` shows no workloads installed, and none is needed for WPF on .NET 10. Actual project creation was not attempted, so a successful WPF build is not yet confirmed. |
| Gradle | Not on `PATH`; the project Gradle wrapper will be used |

Notes:

- Running `adb devices` started the local adb daemon as a side effect of the check.
- `adb` and `emulator` must be invoked by full path or added to `PATH` by the user.

## Prerequisites to install

- None blocking were identified for Phases 1–4.
- Optional: add `%LOCALAPPDATA%\Android\Sdk\platform-tools` and `emulator` to `PATH` for convenience.
- Optional: a Small Round Wear OS AVD (about 192 dp) for the small-screen check in Phase 9.
