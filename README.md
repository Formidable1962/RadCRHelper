# Rad CR Helper

A small helper panel for the University of Iowa Radiology conference-room PCs.
It docks to the right edge of the main screen and puts the few things people need
behind big, plainly labelled buttons.

> **Status:** 0.1.0 scaffold, written 2026-09-16 without a Windows build machine.
> First job: open it in Visual Studio, build, and fix anything the compiler finds.
> Project notes and decisions live in Notion: **Conference Room User Console**.

## What it does (v0.1)

| Area | Behaviour |
|---|---|
| Panel | Right edge of the main screen, always on top. Collapses to a small tab. Never collapses on its own. `Ctrl+Alt+H` opens/closes it. |
| TV and screens | **Mirror** — both screens show the same thing. **Extend** — separate screens (presenter view). Current mode is lit. Greyed out with an explanation when only one screen is connected. |
| Safety net | After every switch: "Keep this screen setting?" — goes back automatically after 15 s. |
| Sign-in default | Screens reset to **Mirror** when the app starts at sign-in. |
| Sign out / Restart | Bottom of the panel. 30-second countdown with **Cancel** and **Sign out now / Restart now**. Forced. **No Shut down**, by design. |
| Quick links | Buttons from `links` in settings: Resident Conference, Today's calendar, Email. One open per button every 5 s. |
| Room info | Computer, room, screens (names to use in settings), mode, version, settings and log file paths. **Copy** for tickets. |

## Build and run

Requirements: **Visual Studio 2022** (".NET desktop development" workload) or the **.NET 8 SDK**.

1. Open `RadCRHelper.sln`.
2. Build and run (F5). NuGet restores **WPF-UI** automatically.

Command line:

```powershell
dotnet run
```

## Release a version for the rooms

1. Bump `<Version>` in `RadCRHelper.csproj` and add a line to `CHANGELOG.md`.
2. Commit, then tag: `git tag v0.2.0` and `git push --tags`.
3. Publish (Visual Studio: right-click project > **Publish** > `win-x64`), or:

   ```powershell
   dotnet publish -p:PublishProfile=win-x64
   ```

4. Output in `bin\publish\win-x64\`: `RadCRHelper.exe` (self-contained, no .NET install needed)
   and `RadCRHelper.json`. Copy both to the Radiology network share.
   *How room PCs pick up the new version is still to be designed.*

Room info shows the exact build, e.g. `0.2.0+a1b2c3d` (version + git commit).

## Settings — `RadCRHelper.json`

Lives next to the `.exe`. Comments are allowed. Restart the app after editing.

- `panel` — widths, start open or collapsed, theme, accent colour, hide from screen sharing.
- `display.defaultModeAtLogin` — `Mirror`, `Extend` or `None`.
- `display.revertSeconds`, `session.countdownSeconds` — countdown lengths.
- `hotkeys` — list of `{ "keys": "...", "action": "..." }`. Key names are WPF `Key` names:
  letters `A`–`Z`, digits `D0`–`D9`, `F1`–`F24`. Modifiers: `Ctrl`, `Alt`, `Shift`, `Win`.
- `links` — Quick links buttons: `label`, `subtitle`, `url` (http/https/zoommtg/zoomus/msteams), optional `id` and `icon`.
- `rooms` — keyed by **computer name** (see Room info):
  - `roomName` — shown under the title.
  - `presenterDisplay` — the desk monitor's name (or part of it) from Room info.
    Stays the main screen in Extend so the taskbar and this panel stay on the desk.
  - `hideDisplayControls` — `true` for rooms that will never have a second screen.
  - `defaultModeAtLogin` — optional override of the sign-in screen mode for that PC.

`configVersion` changes only when the file's shape changes; an older app warns if it meets a newer file.

## Actions

Every button runs a named action. Hotkeys and a future USB macro deck use the same names.

| Action | Does |
|---|---|
| `panel.toggle` / `panel.expand` / `panel.collapse` | Open or close the panel |
| `display.mirror` / `display.extend` | Switch screens (with the Keep prompt) |
| `session.signout` / `session.restart` | Countdown, then forced sign out / restart |
| `link.<id>` | Open that Quick link, e.g. `link.resident-conference` |
| `roominfo.show` | Room info window |
| `app.exit` | Close the app (admin; default `Ctrl+Alt+Shift+X`, no button) |
| `app.exit.prompt` | "Close Rad CR Helper?" then exit. Also: double-click the panel title |

**Macro deck idea:** have the deck send `F13`–`F24` (no normal program uses them) and map them here:

```json
{ "keys": "F13", "action": "panel.toggle" },
{ "keys": "F14", "action": "display.mirror" }
```

## Code map

```
App.xaml(.cs)            startup, action wiring, screen-switch and sign-out flows
Log.cs                   %LOCALAPPDATA%\RadCRHelper\RadCRHelper.log
RadCRHelper.json         settings (copied next to the .exe)
Config/AppConfig.cs      settings model and loader
Services/DisplayService  read screens, Mirror/Extend, keep desk monitor as main screen
Services/NativeDisplay   Windows display API declarations
Services/SessionService  forced sign out / restart
Services/HotkeyService   system-wide shortcuts
Services/WindowInterop   hide from Alt+Tab and from screen capture
Views/PanelWindow        the docked panel
Views/CountdownDialog    every "happens in N seconds" prompt
Views/RoomInfoWindow     troubleshooting info
```

No MVVM framework on purpose: small app, code-behind, one place (`App`) that decides what actions do.

## Test checklist (first room PC)

- [ ] Builds and starts; panel docks right; tab collapses/expands; `Ctrl+Alt+H` works.
- [ ] Mirror ↔ Extend switches; Keep prompt appears; doing nothing goes back after 15 s.
- [ ] Unplug the TV: buttons grey out with the message; plug back in: they return.
- [ ] Extend keeps the desk monitor as the main screen once `presenterDisplay` is set.
- [ ] Sign out and Restart countdowns: Cancel works, timeout goes ahead, apps are force-closed.
- [ ] Zoom and Teams whole-screen share: is the panel hidden from remote viewers?
- [ ] Switching Mirror/Extend on a room where the main screen's scaling changes: does the panel stay docked on the right edge?
- [ ] Unnamed screens show as `Unnamed HDMI screen N` in Room info; that label also works as `presenterDisplay`.
- [ ] Runs from the network share (policy may block `.exe` on network paths).
