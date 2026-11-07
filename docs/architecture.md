# Architecture

## Overview

Presenta is a **modular monolith** kept in one repository with a clear frontend/backend separation. It is not a microservice system.

- `wear-app/` is the Wear OS frontend. It owns screens, navigation, UI state, and view models. It never touches Windows keyboard APIs or OS automation.
- `windows-companion/` is the Windows backend plus desktop UI. It owns the WebSocket server, connection state, pairing, session business logic, keyboard simulation, and history.
- `shared-contracts/` holds language-neutral protocol specs (JSON Schema + Markdown). Each side keeps its own models.

## Boundary rules

1. The watch and the companion communicate only through the v1 protocol defined in `shared-contracts/`.
2. The watch never sends keystrokes or OS commands; it sends intent messages (for example `slide.next`).
3. The companion's domain layer is the single authority for the timer, slide counter, pace, alerts, and recommendations.
4. The domain and protocol return structured data (codes and numeric parameters). Localization lives only in the UI layers (watch resources, WPF resource file).
5. Keyboard simulation sits behind an interface; the domain never references `SendInput` directly.
6. Recommendations sit behind `IRecommendationProvider`; V1 ships only the rule-based provider.
7. Server, domain, protocol, and persistence code contain no Spanish text.
8. Keys are sent only when the domain reports that the slide counter actually changed. `slide.set` never sends keys.

## Component diagram

```mermaid
flowchart LR
    subgraph Watch["wear-app (Wear OS)"]
        UI["Compose screens and navigation"]
        VM["View models and UI state"]
        WC["WebSocket client"]
        DS["DataStore preferences"]
        UI --> VM
        VM --> WC
        VM --> DS
    end

    subgraph PC["windows-companion (.NET 10)"]
        WPF["WPF desktop UI"]
        KS["Kestrel WebSocket server"]
        PAIR["Pairing and connection state"]
        DOM["Session domain: timer, counter, pace, alerts"]
        REC["IRecommendationProvider (rule-based)"]
        KEY["ISlideKeyboard"]
        SEND["SendInput implementation"]
        FAKE["Fake keyboard (tests)"]
        HIST["History: EF Core + SQLite"]
        WPF --> KS
        KS --> PAIR
        KS --> DOM
        DOM --> REC
        DOM --> KEY
        KEY --> SEND
        KEY -.-> FAKE
        DOM --> HIST
    end

    CONTRACTS["shared-contracts (JSON Schema, protocol v1)"]
    WC <-->|"WebSocket over LAN"| KS
    CONTRACTS -.- WC
    CONTRACTS -.- KS
```

## Sequence: connect and pair

```mermaid
sequenceDiagram
    participant W as Watch
    participant S as Companion server
    participant U as Companion UI
    U->>U: Generate random 6-digit code and show it
    W->>S: WebSocket connect
    W->>S: hello (protocol version, pairing code, client name)
    alt code is valid
        S->>W: hello.accepted (includes current state)
        S->>U: Connection state = connected
    else code is wrong
        S->>W: hello.rejected (invalidPairingCode, or pairingLockedOut)
        S->>S: Count failure, lock out after repeated failures
    end
    loop Heartbeat
        W->>S: ping
        S->>W: pong
    end
```

## Sequence: start session

```mermaid
sequenceDiagram
    participant W as Watch
    participant S as Companion server
    participant D as Session domain
    W->>S: session.start (name, total slides, planned minutes)
    S->>D: StartSession
    D-->>S: Session running, slide 1
    S->>W: command.result (ok)
    S->>W: state.update (server UTC time, elapsed ms, running flag)
    S-->>W: periodic state.update
    Note over W: Watch ticks locally between snapshots
```

## Sequence: next and previous slide

```mermaid
sequenceDiagram
    participant W as Watch
    participant S as Companion server
    participant D as Session domain
    participant K as ISlideKeyboard
    participant P as Presentation window
    W->>S: slide.next
    S->>D: WouldMove(Next)?
    alt counter would change and keys are on
        D-->>S: Yes
        S->>K: Send(Next)
        K->>P: restore if minimized, bring to front, confirm it is in front
        alt confirmed in front
            K->>P: Right arrow
            K-->>S: Delivered
            S->>D: Next (counter moves)
            S->>W: command.result (ok)
        else not confirmed, missing, ambiguous, elevated
            K-->>S: typed failure, no key pressed
            S->>W: command.result (targetWindowUnavailable or keyDeliveryFailed), counter unchanged
        end
        S->>W: state.update
    else already at last slide
        D-->>S: No
        S->>D: Next (nothing changes)
        S->>W: command.result (ok, no key sent)
        S->>W: state.update
    end
```

`slide.previous` follows the same flow with the Left arrow. `slide.set` updates the counter and sends no key. With "send keys" off the counter simply moves. The key goes first and the counter second, so a failed delivery never leaves the counter ahead of the slideshow (ADR-58).

### Presentation window targeting (Phase 5.1)

```mermaid
flowchart LR
    APP["Presenta.Companion.App"] --> VM["Companion.ViewModels<br/>TargetSelectionViewModel"]
    APP --> KBW["Keyboard.Windows<br/>Win32WindowSystem (only Win32 code)"]
    VM --> TGT["Presenta.Targeting<br/>PresentationTargetService, WindowSlideKeyboard, IWindowSystem"]
    KBW --> TGT
    TGT --> DOM["Presenta.Domain<br/>ISlideKeyboard, KeyDeliveryResult, TargetState"]
    SRV["Presenta.Server"] --> DOM
```

Per key, `WindowSlideKeyboard` resolves the saved identity to a live window, restores it if minimized, brings it to the front unless it already is, confirms it is the foreground window (3 attempts of up to 250 ms), checks again right before the press, and only then presses Right or Left. Any failure returns a typed result and presses nothing (ADR-54 to ADR-59).

## Sequence: alert

```mermaid
sequenceDiagram
    participant D as Session domain
    participant S as Companion server
    participant W as Watch
    S->>D: Tick (about once per second)
    D-->>S: Alert raised (kind, severity, numeric parameters)
    S->>W: state.update (activeAlert set)
    W->>W: Control screen enters alert state, haptic feedback
    Note over W: Next and previous stay usable
    W->>S: alert.acknowledge (optional)
    S->>D: AcknowledgeAlert
    D-->>S: Alert cleared
    S->>W: command.result (ok)
    S->>W: state.update (activeAlert null)
```

## Sequence: finish, auto-save, summary

```mermaid
sequenceDiagram
    participant W as Watch
    participant S as Companion server
    participant D as Session domain
    participant H as History store
    W->>S: session.finish
    S->>D: Finish
    D-->>S: Final result
    S->>H: Save record automatically
    H-->>S: Saved
    S->>W: command.result (ok)
    S->>W: session.summary
    S->>W: state.update (session null)
    Note over W: Watch shows the summary and returns home
```

The same auto-save and `session.summary` flow runs when a session ends while paused or is interrupted by disconnection. There is no save command.

## Sequence: history

```mermaid
sequenceDiagram
    participant W as Watch
    participant S as Companion server
    participant H as History store
    W->>S: history.list
    S->>H: Query finished sessions, newest first
    H-->>S: Records
    S->>W: history.list.result
    Note over W: Each list item holds every summary field, so the read-only summary needs no further request
```

## Sequence: disconnection and reconnection

```mermaid
sequenceDiagram
    participant W as Watch
    participant S as Companion server
    participant D as Session domain
    participant H as History store
    Note over W,S: Session is running
    W--xS: Connection lost
    S->>S: Heartbeat timeout, state = disconnected
    S->>D: Start grace period (default 5 minutes)
    alt watch reconnects within grace period
        W->>S: WebSocket connect and hello
        S->>W: hello.accepted (current state)
        S->>D: Cancel grace period
    else grace period expires
        D-->>S: End as interruptedByDisconnection
        S->>H: Save record automatically
    end
```

Only one watch connection exists at a time. A new valid connection replaces the old one.

## Connection topology

```mermaid
flowchart TB
    subgraph PCHost["Windows PC"]
        COMP["Companion (Kestrel, port 8765)"]
        ADB["adb"]
    end

    subgraph Emu["Android emulator (Wear OS)"]
        EW["Watch app"]
    end

    subgraph Phys["Physical watch (same Wi-Fi)"]
        PW["Watch app"]
    end

    EW -->|"A: ws://10.0.2.2:8765"| COMP
    EW -->|"B: ws://localhost:8765"| ADB
    ADB -->|"adb reverse tcp:8765 tcp:8765"| COMP
    PW -->|"C: ws://PC-LAN-IP:8765 (firewall rule, LAN listening on)"| COMP
```

- **A, emulator via `10.0.2.2`:** default. The emulator's alias for the host loopback.
- **B, emulator via `adb reverse`:** fallback. `adb reverse tcp:8765 tcp:8765`, then use `ws://localhost:8765`.
- **C, physical watch via LAN:** the companion must allow the local network (default is loopback-only) and Windows Firewall must permit the port.

## Implemented structure (Phase 4)

```mermaid
flowchart LR
    APP["Presenta.Companion.App (WPF)"] --> VM["Presenta.Companion.ViewModels"]
    APP --> KBW["Presenta.Keyboard.Windows"]
    APP --> TGT["Presenta.Targeting (Phase 5.1)"]
    VM --> TGT
    KBW --> TGT
    TGT --> DOM
    APP --> PER["Presenta.Persistence"]
    APP --> SRV["Presenta.Server"]
    VM --> SRV
    SRV --> DOM["Presenta.Domain"]
    SRV --> PRO["Presenta.Protocol"]
    PER --> DOM
    KBW --> DOM
```

- `Presenta.Server` contains `CompanionService` (all logic, one gate that serializes commands, ticks, heartbeats and disconnects) and `CompanionHost` (Kestrel). The service outlives the listener, so rebinding the address does not lose the session.
- Dependencies point inward: the domain knows nothing about sockets, SQLite, or Windows. `ISlideKeyboard` and `IHistoryStore` are defined in the domain. The app uses `WindowSlideKeyboard` (in `Presenta.Targeting`) as the keyboard; the Win32 calls are in `Presenta.Keyboard.Windows`; history is in `Presenta.Persistence`.
- A single `TimeProvider` timer ticks once per second: it ticks the domain, closes silent connections, ends a session whose watch has not returned within the grace period, and sends a `state.update` every 5 seconds while a watch is connected.
- Message names in the diagrams above are the final protocol v1 names (`hello.accepted`, `state.update`, `command.result`, `session.summary`, `history.list.result`).
- Sequence of a session end: `command.result`, then `session.summary`, then `state.update` with `session: null`. After an interruption nothing is sent; the record is found through `history.list`.

## Watch client (Phase 5)

`RemotePresentaRepository` is the only class that knows about sockets and protocol messages. It implements the same `PresentaRepository` interface as the fake, so no screen changes. All its state changes happen in one coroutine that processes socket callbacks, commands, and a 0.5-second tick one at a time.

### Connection states

```mermaid
stateDiagram-v2
    [*] --> NeedsCode: no stored code
    [*] --> Connecting: stored code
    NeedsCode --> Connecting: connectWithCode
    Connecting --> Handshaking: socket open, hello sent
    Handshaking --> Online: hello.accepted
    Handshaking --> Rejected: hello.rejected
    Rejected --> Connecting: connectWithCode
    Connecting --> BackingOff: failed or timeout
    Handshaking --> BackingOff: timeout
    Online --> BackingOff: closed, failed or silent
    BackingOff --> Connecting: backoff elapsed or retry
    BackingOff --> GivenUp: 30 s without a connection
    GivenUp --> Connecting: retry
```

What the screens see: `Online` is "Connected" and "Paired"; `Connecting`, `Handshaking`, and `BackingOff` are "Reconnecting"; `GivenUp` is "Disconnected" (screen 07); `NeedsCode` and `Rejected` bring up the pairing screen.

### Connect, heartbeat, loss, and reconnection

```mermaid
sequenceDiagram
    participant W as Watch client
    participant S as Companion
    W->>S: hello (stored code, protocol 1)
    S->>W: hello.accepted (current state, or no session)
    loop every 1.5 s
        W->>S: ping
        S->>W: pong
    end
    S--xW: connection lost or server silent
    Note over W: No frame for 3 s: lost, at most 3.5 s after the last frame
    W->>W: state Reconnecting, session stays on screen
    loop backoff 0.5 s, 1 s, 2 s, 4 s
        W->>S: hello (stored code)
    end
    S->>W: hello.accepted (current state restores the session)
```

- The pairing code is the one stored on the watch; a wrong or outdated code ends the loop and shows the pairing screen.
- Commands are sent only when online. Otherwise they fail immediately; nothing is queued.
- Every `state.update` replaces the session and re-synchronizes the local clock that interpolates elapsed time between updates.
- `session.summary` becomes an event for the summary screen; `history.list` waits for `history.list.result`.
