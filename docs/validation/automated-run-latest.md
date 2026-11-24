# Automated run, latest

Written by `scripts/verify-all.ps1` on 2026-11-24 12:36:14 -05:00. Overwritten by every run; do not edit by hand.

Options: ContinueOnError=False, CrossStack=True, Desktop=False. .NET SDK 10.0.400.

| Step | Result | Duration | Counts |
|---|---|---|---|
| Guide strings exist in the resources | PASS | 3 s | checked 540 |
| Companion build (warnings as errors) | PASS | 11 s | warnings 0 |
| Companion tests | PASS | 43 s | passed 1138, failed 0, skipped 12 |
| Watch unit tests | PASS | 85 s | passed 270, failed 0, skipped 0 |
| Watch lint | PASS | 29 s | errors 0 |
| Watch debug build | PASS | 25 s |  |
| Cross-stack tests (real client, real server) | PASS | 109 s | passed 13, failed 0, skipped 0 |
| Desktop tests | SKIPPED | 0 s | pass -Desktop to run (opens windows) |

## Measured by the cross-stack tests (loopback, developer host)

```
TIMING command-latency samples=100 minMs=1.1 medianMs=1.3 p95Ms=1.6 maxMs=6.4
TIMING watch-log-lines=111 (the debug log of the client saw the same commands)
TIMING loss-detection afterHostKilledMs=15 productionBoundMs=3500
TIMING watch-log connection-lost phase=Online sinceLastFrameMs=15 reconnectingAfterMs=0
```

These are the real client against the real server on this computer. They do not include the real key injection, the slideshow repaint, or a real network.
