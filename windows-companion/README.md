# windows-companion

Windows backend and desktop window for Presenta (C#, .NET 10). It owns the WebSocket server, pairing, the session logic, keyboard control, and the history. The Wear OS app only connects and speaks protocol v1 (`../shared-contracts/protocol-v1.md`).

## Projects

| Path | Purpose |
|---|---|
| `src/Presenta.Domain` | Pure session logic (state machine, timer, counter, pace, alerts, recommendations, summary) and the `ISlideKeyboard` and `IHistoryStore` abstractions. No I/O. |
| `src/Presenta.Protocol` | Wire models and strict JSON serialization for protocol v1. |
| `src/Presenta.Persistence` | History in SQLite through EF Core. |
| `src/Presenta.Server` | `CompanionService` (pairing, one watch, one session, keys, history, disconnection policy) and `CompanionHost` (Kestrel WebSocket endpoint). |
| `src/Presenta.Targeting` | The presentation window: candidate list, saved identity, resolution, activation and `WindowSlideKeyboard` (the `ISlideKeyboard` the app uses). Portable; no Win32. |
| `src/Presenta.Keyboard.Windows` | The only Win32 code: `Win32WindowSystem` (enumerate, activate, confirm foreground) and the `SendInput` Right and Left arrows. |
| `src/Presenta.Companion.ViewModels` | View models and the Spanish `UiStrings.resx`. No WPF types. |
| `src/Presenta.Companion.App` | The WPF window (`Presenta.exe`). |
| `tools/Presenta.DevHost` | Console host of the real server with a keyboard that only prints `KEY Right` or `KEY Left`. For the watch's cross-stack test and diagnostics; it never presses a key. |
| `tests/*` | Unit and integration tests; `Presenta.TestSupport` has the fake clock, keyboard, window system, and history store. `Presenta.Desktop.Tests` (with the helper `Presenta.WindowProbe`) are opt-in tests that open real windows. |

## Build and test

Requires the .NET 10 SDK only (no Visual Studio workload; WPF builds from the command line).

```
dotnet build Presenta.Companion.sln -warnaserror
dotnet test Presenta.Companion.sln
```

## Run

```
dotnet run --project src/Presenta.Companion.App
```

or open `Presenta.Companion.sln` in Visual Studio and start `Presenta.Companion.App`. Optional arguments (after `--` with `dotnet run`):

| Argument | Meaning |
|---|---|
| `--port 8765` | TCP port (default 8765). |
| `--database C:\path\history.db` | History file (default `%LOCALAPPDATA%\Presenta\history.db`). |
| `--target-settings C:\path\target.json` | File that remembers the chosen presentation window (default `%LOCALAPPDATA%\Presenta\target.json`). |

What to expect: a dark window titled "Presenta" opens and the server starts by itself. It shows the server state, a six-digit pairing code in large type (with a button to generate a new one), the port and the addresses a watch can use, the "Ventana de la presentación" section (see below), two switches, the watch status (waiting or connected), the active presentation (name, slide, remaining time), and the latest events. The window text is Spanish.

## Choosing the presentation window

The section "Ventana de la presentación" lists the visible windows (title and program), with a PowerPoint slide-show window suggested first. Choose one, then press "Verificar": the companion brings that window to the front and tells you whether it arrived. "Verificar" never presses a key. "Actualizar lista" reads the open windows again. The status line says whether the window is ready, minimized (it will be restored), not found, ambiguous (several identical windows), or running as administrator (Windows blocks keys into it unless Presenta also runs as administrator). The choice is remembered in `%LOCALAPPDATA%\Presenta\target.json` by program, window class, and part of the title, so it survives restarting the companion as long as that window exists again.

Known limits: Windows may refuse to bring a window forward in rare cases (then nothing is sent and an error appears); focus is not given back to the previous window; programs that run exclusive full screen, several monitors, and browsers (Google Slides, Canva: identified by title only, and the title changes with the tab) were not exercised.

## Developer host (no keys, no window)

```
dotnet run --project tools/Presenta.DevHost -- --code 123456 [--port 0] [--database path] [--grace-seconds 300] [--heartbeat-seconds 15]
```

It prints `LISTENING port=N`, prints `KEY Right` or `KEY Left` instead of pressing keys, accepts `rebind` (restart the listener) and `quit` on standard input, and shuts down cleanly. `--code` fixes the pairing code and exists for tests only. The watch's `gradlew :app:crossStackTest` uses it.

## Ports and addresses

| Case | Address the watch uses |
|---|---|
| Emulator | `ws://10.0.2.2:8765` (the emulator forwards this to the PC's loopback) |
| Emulator, fallback | run `adb reverse tcp:8765 tcp:8765`, then `ws://localhost:8765` |
| Physical watch on the same Wi-Fi | `ws://<PC LAN address>:8765`, after turning on "Permitir reloj físico en la red local" |

By default the server listens on `127.0.0.1` only. The switch rebinds it to all interfaces on the same port; connected watches reconnect.

### Windows Firewall (physical watch only)

Windows may ask to allow the app on first use of the network switch. If it does not, allow inbound TCP on the port for private networks, for example from an administrator PowerShell:

```
New-NetFirewallRule -DisplayName "Presenta" -Direction Inbound -Protocol TCP -LocalPort 8765 -Action Allow -Profile Private
```

Remove it later with `Remove-NetFirewallRule -DisplayName "Presenta"`. Nothing in this project changes firewall settings.

## Behavior in short

- **Pairing:** the first message must be `hello` with the code. Five wrong codes from one address lock it out for 30 seconds.
- **Keys:** Next presses the Right arrow and Previous the Left arrow, only when the slide counter would really change; `slide.set` never presses a key. A switch in the window turns key sending off.
- **Where keys go (Phase 5.1):** to the window chosen in "Ventana de la presentación", never to whichever window happens to be in front. Before each key the companion restores the window if it is minimized, brings it to the front, confirms it is in front, and only then presses the key; otherwise it presses nothing and the watch shows an error. The key is delivered first and the counter moves only afterwards, so the watch and the slideshow stay in step. With keys on, `session.start` is refused until a usable window is chosen.
- **Session:** it keeps running if the watch disconnects. A watch that reconnects gets the current state at once. After 5 minutes without a watch the session ends as interrupted and is saved. Ending a session always saves one history record automatically.
- **One watch at a time:** a second valid connection replaces the first.
- **Heartbeat:** the watch should send `ping` about every 2 seconds; a connection silent for 15 seconds is closed. The companion sends `state.update` at least every 5 seconds while a watch is connected.

## Using the domain (for developers)

- Create a `SessionConfiguration` with `SessionConfiguration.Create(...)`, then a `PresentationSession` with the configuration and a `TimeProvider`.
- Operations return typed results instead of throwing. Slide operations report whether the counter changed; send a key only when it did.
- Call `Tick()` about once per second while a session runs (`CompanionService` does this). The session is not thread-safe; `CompanionService` serializes access.

## Not verified automatically

- Real key injection into PowerPoint, Android Studio in the foreground, and the 2-second latency: by hand (see `../docs/manual-test-guide.md`, section E). The opt-in `Presenta.Desktop.Tests` prove delivery with small test windows only.
- How the window looks. It was built and started, but not seen.
- A physical watch, the emulator, and the Windows Firewall.
