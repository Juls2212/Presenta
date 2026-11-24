# Manual test setup and troubleshooting

This file only explains how to set up the machines and what to do when something does not work. The tests themselves, the expected results and the results sheet are in [validation/validation-guide.md](validation/validation-guide.md); there is no second list of test steps here.

Everything the automated tests could prove has already been proven (see `docs/testing-strategy.md`). The validation guide covers what only a person can check: real key presses in a real slideshow, how the screens look and feel, and how fast things happen.

## 1. What you need

| Item | Notes |
|---|---|
| This repository | Folder `C:\Users\ASUS\Desktop\Reloj\Presenta`. |
| .NET 10 SDK | Already installed (checked in `docs/environment.md`). |
| Android Studio | Already installed, with the AVD `Wear_OS_Large_Round`. |
| A slideshow program | PowerPoint, LibreOffice Impress, Google Slides in a browser, or a PDF viewer that moves with the arrow keys. Any program where Right arrow goes to the next slide and Left arrow goes back. |
| A test deck | 5 to 8 slides, each one clearly different (a big number on each slide works well). |
| A stopwatch | Your phone is fine. |
| About 30 minutes | |

Before you start, close other presentations so the keys cannot go to the wrong place.

## 2. Start the companion on the PC

1. Open PowerShell in the repository folder:
   ```
   cd C:\Users\ASUS\Desktop\Reloj\Presenta
   dotnet run --project windows-companion/src/Presenta.Companion.App
   ```
   The first run takes a minute to build.
2. A dark window titled **Presenta** opens. The server starts by itself.
3. Write down the **six-digit code** shown in large cyan numbers. This is the pairing code. It changes every time the companion starts (or when you press "Generar código nuevo").
4. Check the window shows:
   - "Servidor activo" at the top right,
   - "Puerto 8765",
   - the line "Emulador del reloj: ws://10.0.2.2:8765",
   - the watch status "Esperando al reloj",
   - "Sin presentación activa",
   - the section "Ventana de la presentación" with a list of windows, "Actualizar lista", "Verificar" and the status "Ninguna ventana elegida",
   - both switches: "Permitir reloj físico en la red local" off and "Enviar teclas a la presentación" on.
5. Leave this window open for the whole test. Do not close it until the end.

## 3. Start the emulator

1. Open Android Studio.
2. Open **Device Manager** (the phone icon on the right side, or *View > Tool Windows > Device Manager*).
3. Find **Wear OS Large Round** and press the play triangle to start it. Wait until you see the watch face. The first start can take a few minutes.

## 4. Run the watch app

1. In Android Studio choose *File > Open* and select the **`wear-app`** folder (not the repository root). Wait for *Gradle sync* to finish (progress bar at the bottom). The first sync downloads files and can take several minutes.
2. At the top, check that the selected device is **Wear OS Large Round** and the run configuration is **app**.
3. Press the green **Run** triangle. The app installs and opens on the emulator.

If you want to preview the screens without the PC, open `wear-app/app/src/main/kotlin/com/presenta/wear/ui/preview/Previews.kt` and use the Compose preview panel; pictures of every screen are also in `docs/ui-previews/`.

## 5. Troubleshooting

| Problem | What to do |
|---|---|
| The watch never reaches the PC ("Reconectando…" forever, or "Sin conexión al computador"), and the companion shows "Servidor activo" | The emulator may not reach `10.0.2.2`. Use the fallback: run `adb reverse tcp:8765 tcp:8765` and tell the app to use `localhost` (next row). |
| Using `adb reverse` and `localhost` | In a new PowerShell: `& "C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe" reverse tcp:8765 tcp:8765`, then set the address once with: `& "C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe" shell run-as com.presenta.wear sh -c "echo ws://localhost:8765 > files/server_address.txt"` and restart the app on the emulator. This works only for the debug build you run from Android Studio. To go back to the default address: `... shell run-as com.presenta.wear rm files/server_address.txt`. |
| `adb` is `not recognized` | `adb` is not on your PATH. Use the full path: `C:\Users\ASUS\AppData\Local\Android\Sdk\platform-tools\adb.exe` (this is the SDK folder listed in `docs/environment.md`). Or add that folder to PATH. |
| Windows asks `Allow Presenta to communicate on these networks?` | For the emulator you can choose Cancel; the emulator uses the loopback address. You only need to allow it when you turn on "Permitir reloj físico en la red local". |
| Log shows `Cleartext HTTP traffic not permitted` | The app only allows plain connections to `10.0.2.2`, `localhost` and `127.0.0.1`. Check the address is one of those. A physical watch on a Wi-Fi address is **not supported yet** (deferred, see the roadmap). |
| "Código incorrecto" although you typed the number you wrote down | The companion creates a new code each time it starts or when you press "Generar código nuevo". Read the code in the window again. |
| The companion says the port is in use ("No se pudo iniciar el servidor", followed by the reason) | Another program or another copy of Presenta uses port 8765. Close it, or start the companion with another port: `dotnet run --project windows-companion/src/Presenta.Companion.App -- --port 8766` (the watch then needs the matching address, see the `adb reverse`/override rows). |
| Next/Previous work in the watch but the slideshow does not move | Keys go only to the window chosen in "Ventana de la presentación". Check that section: the status must be "Lista: la ventana está disponible"; press "Actualizar lista", choose the slideshow window again and press "Verificar". Also check "Enviar teclas a la presentación" is on and that the slideshow is running. |
| The activity list says "No se pudo enviar la tecla" followed by a reason | The reason after the colon tells what failed: no window chosen, the window no longer exists, several identical windows, administrator window, Windows did not let the window come to the front, the window never reached the front, or Windows did not accept the key. Press "Verificar", then "Copiar diagnóstico", paste the text into a file and send it (row 86 of the validation guide). |
| The watch shows "Elige la ventana de la presentación en el computador" | Keys are on but no usable window is chosen (none, closed, or several identical windows). Choose the slideshow window in the companion, or switch "Enviar teclas a la presentación" off. |
| The watch shows "No se pudo enviar el comando a la presentación" | Windows did not let the companion bring the window to the front. Press "Verificar" and read the message; click the slideshow window once and try again. The counter did not move. |
| The status says the window runs as administrator | Windows blocks keys into administrator programs. Run Presenta as administrator too, or run the slideshow normally. |
| `gradlew` fails with `jlink executable ... does not exist` naming the Java of another program (for example an editor extension) | Gradle reused a background daemon started by that program. In your PowerShell window run `$env:GRADLE_OPTS = '-Dorg.gradle.daemon=false'` and the command again: it then uses a single-use daemon with your own `JAVA_HOME`. Nothing else needs to change. |
| The emulator is very slow | Close other heavy programs; wait for it to finish starting. |
| Gradle sync fails in Android Studio | Check you opened the `wear-app` folder, and that there is internet access for the first sync. |

## 6. After the test

Close the companion window (if a presentation is running it asks first; see row 65 of the validation guide). If you used `adb reverse`, you can remove it with `adb reverse --remove-all`. Send the filled-in results back as described in section 12 of the validation guide.
