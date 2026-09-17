# Changelog

Plain-language notes, newest first. One line per change people would notice.
Version numbers: `0.x` during the pilot, `1.0.0` at first deployment to the rooms.
Tag each release in git as `vX.Y.Z`.

## [Unreleased]

## [0.2.0] - 2026-09-17

- New **Quick links** section: buttons from `links` in the settings file.
  Buttons: **Resident Conference** (Zoom), **Today's calendar** and **Email** (Outlook on the web). Clicking again within 5 seconds does nothing,
  so nervous double-clicks don't open several copies.
- Calendar and Email open Outlook on the web as an **Edge app window** (no tabs or address bar),
  falling back to Chrome, then the installed Outlook app, then a plain-language message.
  Fixes the "How do you want to open this?" prompt.
- Quick links are grouped under headings: **Zoom meetings** and **Office 365** (`group` per link).
- Office 365 shows as two-across tiles: PowerPoint | Your OneDrive, Your Calendar | Your Mail (`groupColumns`).
- New **PowerPoint** button: opens the installed PowerPoint. If it is missing, asks
  "Can't find PowerPoint on this workstation! Open PowerPoint online instead?" (`localApp` per link).
- New **OneDrive** button (OneDrive on the web) as an Edge app window, then Chrome, then the default browser.
- Resident Conference opens in Edge (then Chrome).
- Admin exit: `Ctrl+Alt+Shift+X` closes the app, or double-click the panel title and answer Yes.
- Rooms can override the sign-in screen mode (`rooms.<PC>.defaultModeAtLogin`).

## [0.1.0] - 2026-09-16

First scaffold. Not yet built or tested on a room PC.

- Right-docked panel that collapses to a small tab; opens and closes with Ctrl+Alt+H.
- Mirror / Extend buttons with the current mode lit, plain-language labels, and a
  15-second "Keep this screen setting?" safety prompt.
- Screens are detected live; with one screen the buttons grey out with an explanation.
- Screens reset to Mirror at sign-in (room default).
- In Extend, the desk monitor stays the main screen (set per room in the settings file).
- Sign out and Restart at the bottom, each with a 30-second countdown. Both are forced.
  There is no Shut down option.
- Room info window: computer, screens, mode, version, settings and log file locations.
- Hidden from Zoom/Teams screen sharing (test).
