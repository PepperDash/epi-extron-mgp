![PepperDash Essentials Plugin Logo](/images/essentials-plugin-blue.png)

![PepperDash Essentials](https://img.shields.io/badge/PepperDash%20Essentials-≥%20v3.0.0--rc.20-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Crestron](https://img.shields.io/badge/Crestron-4--Series-lightgrey)
![License](https://img.shields.io/badge/license-MIT-green)


# Extron MGP 641 Essentials Plugin (c) 2026

Controls an Extron MGP 641 four-window processor from PepperDash Essentials 3.0 using SIS (Simple Instruction Set).

## Features

* Layout selection: recalls an MGP window preset (1-128)
* Per-window (1-4) input selection: recalls an MGP input preset (1-128) to the window, for use with an upstream matrix
* Per-window mute and freeze
* Device info (IDeviceInfoProvider): hostname, IP address, MAC address and firmware. The MGP has no SIS command for its serial number.
* Mobile Control messenger for the React app, plus an EISC bridge join map

## Communication

| Method | `control.method` | Port | Notes |
| --- | --- | --- | --- |
| SSH | `ssh` | 22023 | Default Ethernet method. Login `admin`; password is the unit serial number on a new unit (`extron` after a factory reset). Put both in `tcpSshProperties`. |
| Telnet / TCP | `tcpIp` | 23 | Disabled on the unit by default. Enable it with `ESC Z 23 PMAP CR`. If the unit has a password, set the plugin `password` property. |
| RS-232 | `com` | n/a | 9600, 8, N, 1, no flow control. |

On connect the plugin sets verbose mode 3 (unsolicited and tagged responses) and, over SSH, turns echo off.

## Behavior notes

* The four HDMI inputs are fixed to windows 1-4. Selecting an "input" for a window recalls an input preset, which is how an upstream matrix's sources are sized in the window.
* The unit cannot report which input preset is active in a window. The plugin reports the last preset it recalled and shows `null` for a window after a reconnect.
* A window preset stores image size and position. Set `reapplyInputsAfterLayout` to re-recall each window's input preset after a layout change.
* Names missing from config are read from the unit, for configured presets only.
* Response parsing is lenient; unrecognized lines are logged at debug level, so confirm the formats on first hardware test.

## Mobile Control messenger

Path: `/device/{deviceKey}`. The status action is `/mgpStatus`, not `/fullStatus`, because the core device-info and communication-monitor messengers use `/fullStatus` at the same path.

| Action | Content | Effect |
| --- | --- | --- |
| `/mgpStatus` | none | Posts full state |
| `/recallLayout` | `"quad"` or `{"layout":"quad"}` | Recalls the layout configured under that id |
| `/selectWindowInput` | `{"window":1,"input":"laptop"}` | Recalls the input preset to the window |
| `/windowMute` | `{"window":1,"state":true}` | Mute; omit `state` to toggle |
| `/windowFreeze` | `{"window":1,"state":true}` | Freeze; omit `state` to toggle |

State posted to the app (on request and on every change):

```json
{
  "layouts": [{ "id": "quad", "name": "Quad", "preset": 5 }],
  "currentLayout": "quad",
  "currentLayoutPreset": 5,
  "inputs": [{ "id": "laptop", "name": "Laptop", "preset": 1 }],
  "windows": [{ "window": 1, "input": "laptop", "inputPreset": 1, "muted": false, "frozen": false }]
}
```

## Mock device

Set a device's `type` to `extronMgp641Mock` to bench-test rooms, Mobile Control UIs and EISC bridges with no hardware. It uses the same state model, messenger and joins as the real device and needs no `control` object. It starts with the first layout recalled and input N in window N.

```json
{ "key": "mgp-01", "name": "MGP 641", "type": "extronMgp641Mock",
  "properties": { "layouts": { "quad": { "preset": 5, "name": "Quad" } }, "inputs": { "laptop": { "preset": 1, "name": "Laptop" } } } }
```

Inject changes from the console with `devjson`, for example `{"deviceKey":"mgp-01","methodName":"SimulateLayout","params":[5]}`. Methods: `SimulateLine` (feeds a raw SIS response through the real parser), `SimulateLayout`, `SimulateWindowInput`, `SimulateWindowMute`, `SimulateWindowFreeze` and `SetIsOnline`. Commands are dropped while offline.

## Tests

`dotnet test tests/epi-extron-mgp.Tests.csproj` builds the plugin and runs the xUnit tests. The BCL-only `src/Shared/` code (SIS commands, response parsing, state model) is compiled into the test project and executed; the plugin assembly is inspected by metadata for factory checks. New logic that should be tested belongs in `src/Shared/`.

## Build

`dotnet build src/epi-extron-mgp.4Series.csproj` writes the `.cplz` and `.nupkg` to `output/`. Regenerate the documentation below with `dotnet msbuild -t:UpdateReadmeDocs`.

## License

Provided under the MIT license; see [LICENSE.md](LICENSE.md).

## Plugin Documentation

The sections below are generated from the source code; regenerate with `dotnet msbuild -t:UpdateReadmeDocs`.

<!-- START Minimum Essentials Framework Versions -->
### Minimum Essentials Framework Versions

- 3.0.0
<!-- END Minimum Essentials Framework Versions -->

<!-- START Config Example -->
### Config Example

```json
{
    "key": "device-1",
    "name": "Example Device",
    "type": "extronMgp641",
    "group": "pluginDevices",
    "properties": {
        "control": {
            "method": "ssh",
            "tcpSshProperties": {
                "address": "172.22.0.101",
                "port": 22023,
                "username": "admin",
                "password": "unitSerialNumber",
                "autoReconnect": true,
                "autoReconnectIntervalMs": 10000
            }
        },
        "password": "unitSerialNumber",
        "pollTimeMs": 30000,
        "warningTimeoutMs": 180000,
        "errorTimeoutMs": 300000,
        "layouts": {
            "full": {
                "preset": 1,
                "name": "Full Screen"
            },
            "quad": {
                "preset": 5,
                "name": "Quad"
            }
        },
        "inputs": {
            "laptop": {
                "preset": 1,
                "name": "Laptop"
            },
            "camera": {
                "preset": 2,
                "name": "Camera"
            }
        },
        "reapplyInputsAfterLayout": false
    }
}
```
<!-- END Config Example -->

<!-- START Supported Types -->
### Supported Types

- extronMgp641
- extronMgp
- extronMgp641Mock
- extronMgpMock
<!-- END Supported Types -->

<!-- START Join Maps -->
### Join Maps

#### Digitals

| Join | Type (RW) | Description |
| --- | --- | --- |
| 1 | R | Is Online |
| 2 | R/W | Connect (Held)/Disconnect (Release) & corresponding feedback |
| 11 | R/W | Window 1-4 mute on (pulse) & feedback |
| 21 | R/W | Window 1-4 mute off (pulse) & feedback |
| 31 | R/W | Window 1-4 freeze on (pulse) & feedback |
| 41 | R/W | Window 1-4 freeze off (pulse) & feedback |

#### Analogs

| Join | Type (RW) | Description |
| --- | --- | --- |
| 1 | R | Socket status |
| 11 | R/W | Layout (MGP window preset 1-128) select & feedback |
| 21 | R/W | Window 1-4 input (MGP input preset 1-128) select & feedback |

#### Serials

| Join | Type (RW) | Description |
| --- | --- | --- |
| 1 | R | Device name |
| 2 | R | IP address |
<!-- END Join Maps -->

<!-- START Base Classes -->
### Base Classes

- EssentialsBridgeableDevice
- MessengerBase
- DeviceStateMessageBase
<!-- END Base Classes -->

<!-- START Interfaces Implemented -->
### Interfaces

- IExtronMgp
- IDeviceInfoProvider
- ICommunicationMonitor
<!-- END Interfaces Implemented -->

<!-- START Public Methods -->
### Public Methods

- public void RecallLayout(string layoutId)
- public void RecallLayoutPreset(int preset)
- public void SelectWindowInput(int window, string inputId)
- public void SelectWindowInputPreset(int window, int preset)
- public void SetWindowMute(int window, bool mute)
- public void SetWindowFreeze(int window, bool freeze)
- public void SendText(string text)
- public void Poll()
- public void UpdateDeviceInfo()
- public void FireAll()
- public void Link(IExtronMgp device, BasicTriList trilist, ExtronMgpJoinMap joinMap)
- public void SendFullStatus(string id = null)
- public void RecallLayout(string layoutId)
- public void RecallLayoutPreset(int preset)
- public void SelectWindowInput(int window, string inputId)
- public void SelectWindowInputPreset(int window, int preset)
- public void SetWindowMute(int window, bool mute)
- public void SetWindowFreeze(int window, bool freeze)
- public void SetIsOnline(bool online)
- public void SimulateLine(string line)
- public void SimulateLayout(int preset)
- public void SimulateWindowInput(int window, int preset)
- public void SimulateWindowMute(int window, bool muted)
- public void SimulateWindowFreeze(int window, bool frozen)
- public void UpdateDeviceInfo()
- public bool TryGetLayoutPreset(string id, out int preset)
- public bool TryGetInputPreset(string id, out int preset)
- public int GetInputPreset(int window)
- public bool GetMute(int window)
- public bool GetFreeze(int window)
- public void SetCurrentLayout(int preset)
- public void SetInputPreset(int window, int preset)
- public void SetMute(int window, bool muted)
- public void SetFreeze(int window, bool frozen)
- public void SetLayoutName(int preset, string name)
- public void SetInputName(int preset, string name)
- public void SetInfo(string hostName = null, string ipAddress = null, string macAddress = null, string firmwareVersion = null)
- public void ExpectUnitName()
- public ExtronMgpLineKind ProcessLine(string rawLine)
<!-- END Public Methods -->

<!-- START Bool Feedbacks -->
### Bool Feedbacks

- ConnectFeedback
- OnlineFeedback
- OnlineFeedback
<!-- END Bool Feedbacks -->

<!-- START Int Feedbacks -->
### Int Feedbacks

- StatusFeedback
- LayoutPreset
<!-- END Int Feedbacks -->

<!-- START String Feedbacks -->
### String Feedbacks

- IpAddressFeedback
- IpAddressFeedback
<!-- END String Feedbacks -->
