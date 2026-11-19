# Extron MGP 641: hardware test plan

For the next developer or agent. The plugin has only been exercised by unit tests (`dotnet test tests/epi-extron-mgp.Tests.csproj`) and a CI build. **Nothing has run against a real MGP 641 or inside Essentials.** This plan validates the parts that were written from the manual alone.

Source of truth for the protocol: *MGP 641 User Guide, 68-2396-01 Rev C*, "Remote Configuration and Control" (kept local, `docs/*.pdf` is gitignored). In the manual's tables spaces are typesetting only; a real space is shown as `•`. `ESC` is 0x1B, `}` is CR, `]` is CR/LF.

## 1. What is being tested

| Area | Implementation |
| --- | --- |
| Transports | SSH 22023, Telnet/TCP 23, RS-232 (9600/8/N/1) via `CommFactory` |
| Session setup | `ExtronMgpDevice.InitializeSession`: echo off (SSH only), verbose mode 3, state and info queries |
| Layouts | Recall window preset `1*N.`; feedback from `ESC L1PRST CR` and unsolicited replies |
| Window mute and freeze | `N*1B` / `N*0B`, `N*1F` / `N*0F`; queries `NB`, `NF` |
| Device info | Hostname `ESC CN CR`, IP `ESC CI CR`, MAC `ESC CH CR`, firmware `*Q` |
| Routing | `IRoutingInputs` ports `hdmiIn1`-`hdmiIn4`, `hdmiInBackground` (tie lines are made in Essentials config) |
| Mobile Control | `ExtronMgpMessenger` at `/device/{key}` |
| SIMPL bridge | `ExtronMgpJoinMap` |
| Mock | `extronMgp641Mock`, for UI work without the unit |

Out of scope by design: input switching (the MGP has none; HDMI input N feeds window N), input presets, outputs as routing ports.

## 2. Prerequisites

- MGP 641 on known firmware (record it), on the bench network, and a Crestron 4-Series processor running Essentials 3.0.0-rc.20 or newer with the `.cplz` from `output/` (or the CI artifact).
- Unit login: `admin`, password = unit serial number on a new unit, `extron` after a factory reset.
- A PC with Extron DataViewer or an SSH client (port 22023) to compare against the plugin's traffic.
- Telnet is **disabled by default**. Enable it from an SSH session: `ESC Z 23 PMAP CR`, verify with `ESC Z PMAP CR` (expect `00023`).
- For the routing tests: a DM-MD or NVX with at least two live sources and an output tied to the MGP.
- Turn on stream debugging for the device (Essentials console, confirm the syntax for your Essentials version) and keep device debug at `Debug` or `Verbose`. Unrecognized lines are logged as `Unhandled line: ...` with `<ESC>`, `<CR>`, `<LF>` shown, and every sent command is logged at verbose.

Raw SIS can be sent with `devjson`; use JSON escapes for control characters:

```
devjson:1 {"deviceKey":"mgp-01","methodName":"SendText","params":["\u001bL1PRST\r"]}
```

## 3. Config to test with

```json
{
  "key": "mgp-01",
  "name": "MGP 641",
  "type": "extronMgp641",
  "group": "processor",
  "properties": {
    "control": {
      "method": "ssh",
      "tcpSshProperties": {
        "address": "<unit ip>",
        "port": 22023,
        "username": "admin",
        "password": "<serial>",
        "autoReconnect": true,
        "autoReconnectIntervalMs": 10000
      }
    },
    "pollTimeMs": 30000,
    "layouts": {
      "full": { "preset": 1 },
      "quad": { "preset": 5, "name": "Quad" }
    }
  }
}
```

Leave one layout's `name` empty on purpose to test name lookup. For Telnet use `"method": "tcpIp"`, port 23 (not the literal `telnet` method, which `CommFactory` ignores) and set `properties.password` if the unit has one. For RS-232 use `"method": "com"` with `controlPortDevKey`, `controlPortNumber` and `comParams` (see the README).

Before testing, save a few window presets on the unit with distinguishable layouts (front panel or VCS), and name at least two.

## 4. Known uncertainties to verify first

These were inferred from ambiguous manual tables. Each is matched leniently in `src/Shared/ExtronMgpProtocol.cs`. For every one, capture the real bytes and fix the regex and the unit test together.

| # | Question | Where it is parsed | Manual says |
| --- | --- | --- | --- |
| U1 | Reply to layout recall `1*N.` | `LayoutRecalledRegex` | `1RprX2%`; guessed `1Rpr005` or `Rpr1*005` |
| U2 | Reply to `ESC L1PRST CR` in verbose 3 | `CurrentLayoutRegex` | `Prst L 1*X2%`; guessed `PrstL1*005` |
| U3 | Reply to layout name query `ESC 1*NPNAM CR` | `PresetNameRegex` | bare name; tagged `Pnam1*N,Name` assumed |
| U4 | Reply to mute and freeze set, and to queries `NB`, `NF` | `MuteRegex`, `FreezeRegex` | set: `VmtX!*1`, `FrzX!*1`; guessed query replies are tagged in verbose 3 |
| U5 | Replies to `ESC CI CR`, `ESC CH CR`, `ESC CN CR`, `*Q` | `Ipv4Regex`, `MacRegex`, `UnitNameRegex`, `FirmwareRegex` | IP and unit name possibly bare; MAC `Iph•…`; firmware `Ver01*n.nn` or `Bld…` |
| U6 | Unit name arrives untagged | `ExtronMgpModel.ExpectUnitName` takes the next otherwise-unrecognized line | none |
| U7 | `ESC 0ECHO CR` accepted over SSH | `EchoOff` | SIS-over-SSH only |
| U8 | Verbose mode 3 over each transport | `VerboseMode3` | RS-232 defaults to verbose 1, Telnet and SSH to 0 |
| U9 | Banner, password prompt and login line sequence per transport | `IsBanner`, `IsPasswordPrompt`, `IsLoginAccepted` | banner on connect, `Password` prompt, then `Login Administrator` |
| U10 | Does the unit push changes made at the front panel or in VCS in verbose 3 | `ProcessLine` | verbose mode description |
| U11 | `0*1B` (all windows) response, if ever used | `SetMute(0, ...)` | window 0 = all windows |

## 5. Test cases

Run each over SSH first, then repeat the connection and feedback sections (5.1, 5.2, 5.5) over Telnet and RS-232. Record observed raw traffic for every case.

### 5.1 Connection and session

| ID | Step | Expected |
| --- | --- | --- |
| C1 | Load config, start Essentials | Device builds, `Connect` goes true, no exceptions. Communication monitor reaches OK within one poll interval |
| C2 | Watch the first lines after connect | Banner `(c) Copyright …, Extron Electronics, MGP 641, Vn.nn, 60-1574-0x` seen, then `Initializing MGP session` logged once |
| C3 | Telnet with unit password set and `properties.password` configured | `Password` prompt answered, `Login Administrator` seen, init runs after login, not before |
| C4 | Telnet or SSH with a **wrong** password | Warning logged, no init loop, device stays offline |
| C5 | SSH: confirm echo is off | Sent commands are not echoed back; no `Unhandled line` flood of the plugin's own commands |
| C6 | Confirm verbose 3 took effect | Query replies carry tags; changes made on the front panel arrive unsolicited (U8, U10) |
| C7 | RS-232 only: start the plugin with the unit already running | Init runs from activation without a banner (the banner only follows a power cycle) |

### 5.2 Layout selection with feedback

| ID | Step | Expected |
| --- | --- | --- |
| L1 | At startup, read current layout (Mobile Control `/mgpStatus` or analog join 11) | Matches the layout recalled on the unit. Verifies U2 |
| L2 | Recall `full` then `quad` via messenger `/recallLayout` | Display changes; `currentLayout` and `currentLayoutPreset` update; reply parsed (U1), nothing left unhandled |
| L3 | Recall a layout with an invalid id | Warning `Unknown layout id`, nothing sent |
| L4 | Analog join 11: send 5, then 300 | 5 recalls preset 5; 300 is rejected as out of range with no command sent |
| L5 | Recall a layout on the **front panel** or in VCS | Plugin feedback follows within a second (U10). If it does not, the next poll (`ESC L1PRST CR`) must correct it |
| L6 | Layout with no configured `name` | Name is read from the unit (U3) and shows in `/mgpStatus`; without a unit name it falls back to `Layout N` |
| L7 | Recall a preset that is empty or invalid on the unit | An `E` error is logged as a warning and state is not left stuck. Known limitation: state was set optimistically; confirm the next poll corrects it |
| L8 | Check animated vs cut transitions | Feedback is stable during the 0.5 s animation; no flapping |

### 5.3 Window mute and freeze

| ID | Step | Expected |
| --- | --- | --- |
| W1 | Mute then unmute each window 1-4 via `/windowMute` | Window blanks and returns; `muted` updates; replies parsed (U4) |
| W2 | `/windowMute` without `state` | Toggles |
| W3 | Freeze and unfreeze each window | Same, with `frozen` |
| W4 | Change mute and freeze at the front panel | Feedback follows (U10) |
| W5 | Bridge: pulse digital joins mute on 11-14, off 21-24, freeze on 31-34, off 41-44 | Feedback on the on join, complement on the off join |

### 5.4 Device info

| ID | Step | Expected |
| --- | --- | --- |
| D1 | After connect, read device info (Essentials console, Mobile Control `/device/mgp-01` device-info messenger, or serial joins 1-2) | Hostname, IP, MAC and firmware all populated and correct against the unit's front panel / web page. Verifies U5 and U6 |
| D2 | Hostname with only a bare reply | `HostName` is the unit name, not a stray line. Check logs for lines wrongly captured (U6) |
| D3 | Change the unit name from the web UI or SIS, then trigger `UpdateDeviceInfo` | New name picked up |
| D4 | Config address is a hostname, not an IP | `IpAddress` becomes the reported IP after connect |
| D5 | Serial number | Stays empty by design (no SIS query) |

### 5.5 Offline, reconnect and recovery

| ID | Step | Expected |
| --- | --- | --- |
| R1 | Pull the network cable, wait for the monitor to warn and then error | Online feedback goes false |
| R2 | Reconnect | Reconnects, runs init again once, state and info refresh, online true |
| R3 | Power-cycle the unit | Same as R2; verbose mode is re-sent (the unit forgets it) |
| R4 | RS-232: power-cycle, then reconnect cable | Poll re-initializes once the monitor reports online again; recovery can take up to one poll interval, so consider a shorter `pollTimeMs` |
| R5 | Open more sessions on the unit until `E26` (max connections) | Plugin logs the error and recovers when a slot frees |

### 5.6 Routing (Essentials)

| ID | Step | Expected |
| --- | --- | --- |
| T1 | Add tie lines from upstream outputs to `mgp-01` ports `hdmiIn1`-`hdmiIn4` | Essentials accepts them; the routing graph lists the ports (Audio and Video, HDMI) |
| T2 | Route different sources to windows 1 and 2 through the room or the console routing commands | Each window shows its source with the MGP in `quad` or another multi-window layout |
| T3 | Route to `hdmiInBackground` | Source appears as the unscaled background |
| T4 | Confirm the plugin sends **nothing** to the MGP on a route change | Only upstream switching occurs, because input presets were removed |

### 5.7 Mobile Control messenger

Use the Mobile Control debug tools or a WebSocket client.

| ID | Step | Expected |
| --- | --- | --- |
| M1 | Send `/device/mgp-01/mgpStatus` | One `/device/mgp-01` message with `layouts`, `currentLayout`, `currentLayoutPreset`, `windows[4]` |
| M2 | Confirm no clash with the core messengers | `/fullStatus` still returns the core device-info and monitor payloads; `/mgpStatus` is the plugin's |
| M3 | Change state from the unit | A state message is pushed without polling, and not more than once per actual change |
| M4 | Send malformed content (missing `window`, a string where an object is expected) | Ignored without an exception |
| M5 | Several clients connected | Each receives pushes; a request answers only the requesting client |

### 5.8 Mock device

| ID | Step | Expected |
| --- | --- | --- |
| K1 | Change the config `type` to `extronMgp641Mock` (drop `control`) | Builds; starts on the first layout; reports fixed device info |
| K2 | Run the messenger cases M1-M4 against the mock | Same payload shapes as the real device |
| K3 | `devjson` `SimulateLayout [5]`, `SimulateWindowMute [1,true]`, `SetIsOnline [false]`, `SimulateLine ["Prst L 1*001"]` | State, feedback and online status react; commands are dropped while offline |

## 6. What to do with the results

1. Record raw traffic for every uncertainty in section 4 in the table below.
2. Fix the matching regex in `src/Shared/ExtronMgpProtocol.cs` and add the observed line to the tests in `tests/ExtronMgpProtocolTests.cs`. Keep the lenient alternatives that still make sense.
3. Update this plan's section 4 with confirmed formats (or delete the row), and update the README if a behavior note changes.
4. Any command that returns an `E##` error: look the code up in the manual's Error Responses list and decide whether it is a bug in the command or a state to handle.
5. Commit with conventional commits (`fix:`, `test:`), open a PR to `main` (protected, one approval required).

### Results log

| Date | Tester | Firmware | Transport | Case IDs run | Result | Notes and raw traffic |
| --- | --- | --- | --- | --- | --- | --- |
| | | | | | | |

### Uncertainty outcomes

| # | Confirmed format or fix | Test added |
| --- | --- | --- |
| U1 | | |
| U2 | | |
| U3 | | |
| U4 | | |
| U5 | | |
| U6 | | |
| U7 | | |
| U8 | | |
| U9 | | |
| U10 | | |
| U11 | | |

## 7. Known limitations to keep in mind

- Optimistic state: layout, mute and freeze are set when the command is sent and corrected by the unit's reply or the next poll. A rejected command (`E##`) is logged but not re-queried, to avoid a query loop.
- The poll only asks for the last recalled layout. Mute and freeze are refreshed on connect and by unsolicited verbose-mode replies.
- No serial number. Unit serial is also the first-boot SSH password and is deliberately not read or exposed.
- The unit-name reply fallback (U6) can capture an unrelated line if one arrives between the name query and its reply.
