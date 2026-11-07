# Presenta protocol v1

Language-neutral specification of the messages exchanged between the watch (`wear-app`) and the Windows companion (`windows-companion`). The JSON Schemas in [`schemas/`](schemas/) are normative for shapes and ranges; this document explains meaning and behavior. Valid and invalid samples are in [`examples/`](examples/).

## Transport

- WebSocket over the local network, text frames, UTF-8. One JSON message per frame.
- Default port 8765 (configurable). The companion is the server; the watch is the client. Only one watch connection exists at a time.
- Messages carry structured data only (codes and numbers). No message contains text meant for the presenter; each UI maps codes to its own strings.

## Conventions

- Property names are `camelCase`. Enum values are `lowerCamelCase` strings.
- Durations are integers in milliseconds. Remaining time may be negative (overtime); every other duration is non-negative.
- Timestamps are UTC ISO 8601 and must end in `Z` (for example `2026-10-31T15:08:32.000Z`). Offsets such as `+00:00` and values without a zone are invalid.
- Numbers must be sent as JSON numbers. A number sent as a string is invalid.
- Optional properties are omitted when they have no value. Properties documented as "nullable" are always present and may be `null`.
- Readers tolerate unknown extra properties, at the envelope level and in payloads.

## Envelope

```json
{ "version": 1, "type": "slide.next", "id": "m-007", "payload": {} }
```

| Field | Type | Rules |
|---|---|---|
| `version` | integer | Must be `1`. Any other integer yields `unsupportedVersion`. |
| `type` | string | One of the message types below. Unknown types yield `invalidMessage`. |
| `id` | string | 1 to 64 characters. Chosen by the sender. Responses refer to a request through `correlationId`. |
| `payload` | object | Always present, even when empty (`{}`). |

## Message catalog

### Watch to companion

| Type | Payload fields | Notes |
|---|---|---|
| `hello` | `pairingCode` (string, 6 digits), `protocolVersion` (integer, at least 1), `clientName` (string, 1 to 64) | Must be the first message. |
| `session.start` | `plannedSlides` (integer 1 to 999), `plannedDurationMs` (integer 1000 to 43200000), `name` (optional string, 1 to 100) | The watch generates the name. If it is absent the companion uses a neutral English fallback. |
| `session.pause` | none | Running to paused. |
| `session.resume` | none | Paused to running. |
| `session.finish` | none | Running to finished manually, or paused to ended while paused. |
| `slide.next` | none | Moves the counter forward when it can. |
| `slide.previous` | none | Moves the counter back when it can. |
| `slide.set` | `slide` (integer 1 to 999) | Manual correction. Never sends a key to the presentation. |
| `alert.acknowledge` | none | Clears the active alert. Harmless when there is none. |
| `history.list` | `limit` (optional integer 1 to 100) | Newest first. |
| `ping` | none | Heartbeat. |

### Companion to watch

| Type | Payload fields | Notes |
|---|---|---|
| `hello.accepted` | `protocolVersion` (integer), `state` (state object) | Includes the current state, so a reconnecting watch is up to date immediately. |
| `hello.rejected` | `reason` (error code), `retryAfterMs` (optional integer) | `retryAfterMs` is present when the reason is `pairingLockedOut`. |
| `state.update` | state object | Full snapshot. Sent after every state change and periodically while a session runs. |
| `command.result` | `correlationId` (string), `ok` (boolean), `errorCode` (nullable error code) | Reply to every command. `errorCode` is `null` exactly when `ok` is `true`. |
| `session.summary` | history item | Sent once when a session ends (see below). |
| `history.list.result` | `items` (array of history item) | Reply to `history.list`. |
| `error` | `code` (error code), `message` (string), `correlationId` (optional string) | Protocol-level problems. `message` is developer-facing English and is never shown to the presenter. |
| `pong` | none | Reply to `ping`. |

### State object (`state.update`, and `hello.accepted.state`)

| Field | Type | Meaning |
|---|---|---|
| `serverTimeUtc` | timestamp | Companion clock when the snapshot was produced. |
| `session` | session snapshot or `null` | `null` means there is no active session. |

### Session snapshot

| Field | Type | Meaning |
|---|---|---|
| `state` | enum | `configured`, `running`, `paused`, `finished`. |
| `name` | string | Session name. |
| `currentSlide` | integer | 1-based counter. |
| `totalSlides` | integer | Planned slides. |
| `plannedDurationMs` | integer | Planned duration. |
| `elapsedMs` | integer | Elapsed time. Paused time is excluded. |
| `remainingMs` | integer | `plannedDurationMs - elapsedMs`. Negative in overtime. |
| `running` | boolean | `true` only in state `running`. |
| `progressRatio` | number 0 to 1 | `currentSlide / totalSlides`. |
| `paceStatus` | enum | `onTrack`, `behind`, `tooFast`, `notEnoughData`. |
| `slidesRemaining` | integer | `totalSlides - currentSlide`. |
| `averageSlidesPerMinute` | number | `currentSlide` per elapsed minute; `0` below 5 seconds of elapsed time. |
| `finalState` | nullable enum | Set only when `state` is `finished`. |
| `activeAlert` | nullable alert | The alert currently shown, if any. |
| `recommendation` | nullable recommendation | Current recommendation, if any. |

### Alert

| Field | Type | Meaning |
|---|---|---|
| `kind` | enum | `timeRunningOut`, `timeCritical`, `overtime`, `behindSchedule`, `tooFast`. |
| `severity` | enum | `info`, `warning`, `critical`. |
| `raisedAtElapsedMs` | integer | Elapsed time when raised. |
| `remainingMs` | integer | Remaining time when raised. |
| `slidesRemaining` | integer | Slides left when raised. |
| `slideDeviation` | integer | Actual minus expected slide, rounded. Negative means behind. |

### Recommendation

| Field | Type | Meaning |
|---|---|---|
| `code` | enum | `onTrack`, `speedUpSlightly`, `speedUpNow`, `slowDown`, `wrapUp`, `overtime`. |
| `slidesRemaining` | integer | Slides left. |
| `timeRemainingMs` | integer | Remaining time. May be negative. |
| `freeText` | nullable string | Reserved for a future external provider. Always `null` in V1. |

### History item (also the payload of `session.summary`)

| Field | Type | Meaning |
|---|---|---|
| `name` | string | Session name. |
| `finishedAtUtc` | timestamp | When the session ended. |
| `plannedDurationMs` | integer | Planned duration. |
| `actualDurationMs` | integer | Elapsed time at the end. |
| `plannedSlides` | integer | Configured slides. |
| `registeredSlides` | integer | Slide counter at the end. |
| `averageSlidesPerMinute` | number | Average pace. |
| `finalState` | enum | `finishedManually`, `endedWhilePaused`, `interruptedByDisconnection`. |
| `endedWithinPlannedTime` | boolean | `actualDurationMs <= plannedDurationMs`. |

## No active session

There is no separate message for "no session". The state object carries it explicitly: `session` is `null`.

```json
{ "version": 1, "type": "state.update", "id": "s-011",
  "payload": { "serverTimeUtc": "2026-10-31T15:30:00.000Z", "session": null } }
```

The companion sends this form in `hello.accepted` when a watch connects and nothing is running, and in `state.update` after a session ends (right after `session.summary`). The `finished` value of `state` is part of the schema for completeness; the companion does not keep a finished session in its published state.

## Error codes

Each code is documented here. The list must match the `errorCode` definition in `schemas/common.schema.json` and the C# `ErrorCode` enum exactly (enforced by tests).

| Code | Meaning |
|---|---|
| `invalidPairingCode` | The pairing code in `hello` does not match. |
| `pairingLockedOut` | Too many wrong pairing codes; further attempts are refused until the lockout ends. |
| `notPaired` | A command was sent before a successful `hello`. |
| `unsupportedVersion` | The envelope `version` or the client `protocolVersion` is not supported. |
| `invalidMessage` | Not valid JSON, violates the envelope or payload rules, has an unknown type, or carries an invalid session configuration. |
| `invalidState` | The command is not allowed in the current session state (for example `session.pause` when not running). |
| `slideOutOfRange` | `slide.set` asked for a slide outside 1 to `totalSlides`. |
| `noActiveSession` | The command needs an active session and there is none. |
| `targetWindowUnavailable` | "Send keys to the presentation" is on, but there is no usable presentation window: none chosen on the computer, it cannot be found, several windows match, or it belongs to an elevated program. Answers `session.start`, `slide.next` and `slide.previous`. Added to v1 without changing any existing message. |
| `keyDeliveryFailed` | The presentation window exists, but the key could not be delivered to it (the window could not be brought to the front, or the key was not accepted). Answers `slide.next` and `slide.previous`. The counter is unchanged and the session continues. Added to v1 without changing any existing message. |
| `internalError` | Unexpected failure on the companion. |

Mapping from domain failures: `invalidState` maps to `invalidState`, `slideOutOfRange` to `slideOutOfRange`, and an invalid session configuration to `invalidMessage`. `noActiveSession` is raised by the companion when it holds no session object.

## Sequence notes

- **Connect:** `hello` then `hello.accepted` (with the current state) or `hello.rejected`. A wrong code yields `invalidPairingCode`; repeated failures yield `pairingLockedOut`. Any other message before an accepted `hello` is answered with `error` / `notPaired`.
- **Commands:** every command is answered by `command.result`. When the command changes state, a `state.update` follows. A command that fails never closes the connection or the session.
- **Slide commands and keys:** the companion sends a key to the presentation only when the slide counter would actually change. `slide.next` at the last slide and `slide.previous` at slide 1 succeed without a key. `slide.set` never sends a key.
- **Key first, counter second:** when "send keys" is on, the companion delivers the key to the confirmed presentation window first and moves the counter only if delivery succeeded. If delivery fails, `command.result` carries `targetWindowUnavailable` or `keyDeliveryFailed`, the counter keeps its previous value, and the session continues. `session.start` is answered with `targetWindowUnavailable` when keys are on and no usable presentation window exists; with keys off no window is needed.
- **Finish:** on `session.finish`, and equally when a session ends while paused or by disconnection, the companion saves the record automatically, sends `session.summary`, then sends `state.update` with `session: null`. There is no save command.
- **History:** `history.list` is answered by `history.list.result`.
- **Heartbeat (recommended values):** the watch sends `ping` about every 2 seconds. Either side treats the connection as lost after about 5 seconds without any message. Final values are set in Phases 4 and 8.
- **Disconnection:** a running session keeps running when the watch disconnects. A reconnecting watch sends `hello` and immediately receives the current state in `hello.accepted`. A new valid connection replaces the previous one.

## Time handling

The companion owns the authoritative timer. The watch interpolates between snapshots: while `running` is `true`, displayed elapsed time is `elapsedMs` plus the local monotonic time since the snapshot was received, and displayed remaining time is the planned duration minus that value. `serverTimeUtc` identifies when the snapshot was produced and must not be used to infer elapsed time on a watch whose clock may differ.

The companion must call the domain `Tick()` regularly (recommended once per second) while a session runs, because time-based alerts are only evaluated when the session is touched. It sends a `state.update` whenever `Tick()` raises an alert.

## Versioning and compatibility

- The `version` field is the major protocol version. This document describes version 1.
- Within version 1, changes must be backward compatible: new optional properties and new message types may be added; required properties, enum values that existing readers rely on, and ranges must not be removed or narrowed.
- Readers ignore unknown properties. A reader that receives an unknown message type answers with `error` / `invalidMessage` and keeps the connection open.
- A change that breaks this policy requires `version: 2`. A companion that receives another version replies `unsupportedVersion`.
- The client announces the protocol version it speaks in `hello.protocolVersion`; the companion answers with the version it uses in `hello.accepted.protocolVersion`.
