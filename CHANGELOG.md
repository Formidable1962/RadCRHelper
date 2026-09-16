# Changelog

Plain-language notes, newest first. One line per change people would notice.
Version numbers: `0.x` during the pilot, `1.0.0` at first deployment to the rooms.
Tag each release in git as `vX.Y.Z`.

## [Unreleased]

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
