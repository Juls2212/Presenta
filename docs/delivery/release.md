# Release artifacts

Two downloadable files live in `releases/`, plus their hashes and a Spanish quick-start for the course instructor.

| File | Kind | Size | SHA-256 |
|---|---|---|---|
| `Presenta-Companion-win-x64.zip` | Windows companion, Release, self-contained (win-x64), version 1.0.0, with the application icon | 77,858,783 bytes (74.3 MiB) | `612f1940e720ae47fd674a0b866596e3fcbbb5dd15d0ddda3c61b0dfcb369c74` |
| `Presenta-Wear.apk` | Wear OS app, **debug** build (signed with the standard Android debug key), real repository | 39,575,940 bytes (37.7 MiB) | `e02ae6ba3fdbab3a05757eb9b42f55859c2356caed1c93d4467c8219ec2e845e` |
| `SHA256SUMS.txt` | `<hash>  <filename>` for the two files above | | |
| `LEEME.md` | Spanish instructions for a non-developer (also copied inside the zip) | | |

The zip is below the 90 MB limit, so no framework-dependent variant was produced. If you rebuild and the zip grows past 90 MB, also publish a framework-dependent zip (`-r win-x64 --self-contained false`, which needs the .NET 10 Desktop Runtime and should say so in `LEEME.md`): common Git hosts reject files above 100 MB.

## What the companion zip contains

One top-level folder, `Presenta-Companion/`, with:

- `Presenta.exe` (the existing assembly name, carrying the application icon with its seven sizes) and the .NET 10 runtime it needs: no .NET installation is required. `Presenta.runtimeconfig.json` lists `includedFrameworks` (Microsoft.NETCore.App, Microsoft.WindowsDesktop.App, Microsoft.AspNetCore.App), which is how a self-contained app is recognised. Not trimmed, not single-file (WPF with many native libraries is more reliable as a folder).
- `Iniciar-Presenta.ps1`: reads an optional `.env` **next to the script** with the same rules as `scripts/run-companion.ps1` (only `PRESENTA_*` names, process-scoped, values never printed), then starts `Presenta.exe`. Without a `.env` the app starts and the AI stays off.
- `.env.example` (placeholders only, same settings as the repository one) and `LEEME.md`.

It contains no `.env`, key, settings, database, source, test projects or `.pdb` files.

## Commands used

Companion (from the repository root):

```
cd windows-companion
dotnet publish src/Presenta.Companion.App/Presenta.Companion.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:Version=1.0.0 -p:AssemblyVersion=1.0.0.0 -p:FileVersion=1.0.0.0 -p:IncludeSourceRevisionInInformationalVersion=false ^
  -p:DebugType=none -p:DebugSymbols=false -p:PublishTrimmed=false -p:PublishSingleFile=false ^
  -o src/Presenta.Companion.App/bin/Release/package/Presenta-Companion
```

Then copy `Iniciar-Presenta.ps1`, `.env.example` and `LEEME.md` into that output folder and zip the folder `Presenta-Companion` so it is the only top-level entry (the zip was written with Python's `zipfile`, forward-slash paths, deflate level 9, no archive comment). The output folder is under `bin/`, which is git-ignored.

Watch (needs JDK 21 and the Android SDK):

```
cd wear-app
$env:GRADLE_OPTS='-Dorg.gradle.daemon=false'
.\gradlew.bat :app:assembleDebug --no-daemon
copy app\build\outputs\apk\debug\app-debug.apk ..\releases\Presenta-Wear.apk
```

No `-PuseFakeRepository=true` is passed, so the app uses the real repository (`BuildConfig.USE_FAKE_REPOSITORY = false` in the generated `BuildConfig.java`).

Hashes: `sha256sum Presenta-Companion-win-x64.zip Presenta-Wear.apk > SHA256SUMS.txt` (or `Get-FileHash -Algorithm SHA256`).

## Notes and caveats

- **Release warning (fixed in Phase 13):** the Release configuration used to fail on warning CS0219 (`duringAlert` in `AiRecommendationEnricher.cs` was only read inside `#if DEBUG` logging), so the first package was built with `-p:TreatWarningsAsErrors=false`. Its declaration and assignment are now inside the same `#if DEBUG` blocks (behavior-neutral), and the package is built with warnings as errors, without any override.
- **APK kind:** a release APK needs a signing key, and keeping a key in the repository is not acceptable, so the debug APK is shipped. It is installable on the emulator, flagged debuggable, signed by the Android debug certificate, and keeps the `adb run-as` address override documented in the manual test guide. Version: package `com.presenta.wear`, versionCode 1, versionName 0.5.0 (set in `wear-app/app/build.gradle.kts`, not changed here), minSdk 30, targetSdk 36, declares `android.hardware.type.watch`.
- The executable is not code-signed, so Windows SmartScreen and the firewall may ask on first run (explained in `releases/LEEME.md`).
- `*.apk` is ignored by `.gitignore` in general; `.gitignore` now contains `!releases/` and `!releases/*.apk` so the downloads can be committed. `.env`, `bin/`, `obj/` and `build/` stay ignored. `.gitattributes` was not touched.
- Large files: if your Git host limits file size (100 MB on common hosts; the current files are below it), prefer attaching the zip and APK to a repository release instead of committing them, and keep `SHA256SUMS.txt` and `LEEME.md` in the repository.

## Verification done without running anything

- Zip: 559 entries, one top-level folder, CRC test passed, empty archive comment; required files present (`Presenta.exe`, `Presenta.dll`, runtime config and deps, launcher, `.env.example`, `LEEME.md`, `hostfxr.dll`, `coreclr.dll`); no `.env`, `.pdb`, source, test, database, settings or log entries.
- Runtime configuration: `includedFrameworks` present and `deps.json` targets `.NETCoreApp,Version=v10.0/win-x64`.
- Icon: `Assets/presenta.ico` is a valid ICO with 16, 24, 32, 48, 64, 128 and 256 px frames (copy identical to `docs/mockups/presenta.ico`); `Presenta.exe` has an icon group with those seven images (read with the resource API as a data file, not run) and the main window icon is embedded in `Presenta.dll`.
- Executable metadata: file version 1.0.0.0, product version 1.0.0, company and product "Presenta".
- Launcher parser: the `Read-DotEnv` function was loaded by dot-sourcing and run against a temporary file with placeholder values (valid line, quoted value, `export`, a non-`PRESENTA_` name, a line without `=`, an invalid name, an empty value): it returned the expected values and reported lines 6 and 7. The temporary file was deleted. The executable was not started.
- APK: `aapt2 dump badging` (package, versions, SDKs) and `apksigner verify` (valid, debug certificate) from the installed build tools 36.0.0; the file was not installed anywhere.
- Scans of every text file in the zip and every entry of the APK for key-like strings and the forbidden words: nothing found (the only text-pattern hit is the documented variable name `PRESENTA_AI_API_KEY=` in `LEEME.md`, with no value).
- `scripts/check-guide-strings.ps1 -Files releases/LEEME.md`: every quoted Spanish text exists in the resource files.
- Release build of `Presenta.Companion.sln` succeeded with warnings as errors. Phase 13 changed companion source, so the full `verify-all.ps1 -CrossStack` was run (1138 companion tests, 270 watch tests, 13 cross-stack, all passing). The APK was not rebuilt for the release: its SHA-256 is unchanged from Phase 12.

## Checklist before publishing (you do the Git steps)

1. Verify the hashes: `cd releases; Get-FileHash -Algorithm SHA256 *.zip, *.apk` and compare with `SHA256SUMS.txt`.
2. Unzip into a clean folder (not inside the repository), run `Iniciar-Presenta.ps1` with and without a `.env`, and check the window says "Servidor activo".
3. Install the APK on the `Wear_OS_Large_Round` emulator, pair, choose the PowerPoint window and run the smoke test (`docs/validation/smoke-test.md`).
4. Confirm that no `.env` is staged (`.env` and `.env.*` are ignored; only `.env.example` is tracked).
5. Commit `releases/` (or attach the two big files to a release if the host limits apply), and keep the hashes in sync if you rebuild.

## Rebuild

Repeat the commands above, regenerate `SHA256SUMS.txt`, and update the table at the top of this file.
