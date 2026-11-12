# Ubuntu animation-only companion

Approved scope: Ubuntu 22.04 / 24.04 amd64; no model, OCR, network services,
Agent protocols, Windows builds or automatic updates. Keep all 106 animations,
click/drag, calendar and meal scheduling, low-power mode, tray, autostart,
settings, greeting and farewell.

- [x] Add contract checks for the standalone layout and Make entry points; confirm failure before restructuring.
- [x] Move Linux UI and shared animation/media code to src/, assets to assets/.
- [x] Remove inference/control/platform branches; replace preferences and settings with animation-only equivalents.
- [x] Add Make configure/build/run/test/pack/install/uninstall/clean, shared staging and Debian metadata.
- [x] Rewrite documentation and notices; remove unrelated scripts, tests, assets and generated outputs.
- [x] Verify policy/settings behavior, real GStreamer decoding, executable startup under Xvfb, staged install/uninstall and Debian contents.

Build uses self-contained .NET 10 linux-x64 publish output in build/app; executable
is bigbluefish. Packaging uses that output, installs to /opt/bigbluefish and
/usr/bin/bigbluefish, and writes dist/bigbluefish_<version>_amd64.deb.
DESTDIR supports reversible staged install verification. User settings remain in
$XDG_DATA_HOME/bigbluefish (default ~/.local/share/bigbluefish).

Compatibility verification must distinguish this Ubuntu 22.04 host from any
Ubuntu 24.04 CI execution; do not claim a local 24.04 test without one.

Verification on Ubuntu 22.04: make configure/build/test, 206 core checks,
real GStreamer 1.20 decoding with alpha and seek, Xvfb startup/greeting/farewell,
exit-time input regression, staged install/uninstall, Debian assembly.
Ubuntu 24.04: final Debian package installed in a disposable ubuntu:24.04
container, Xvfb startup/greeting/farewell and uninstall all passed. Full SDK build
and core-test coverage on 24.04 is configured in CI; local SDK/core tests ran on 22.04.
