# Ubuntu companion with ACP Agent

Current scope: Ubuntu 22.04 / 24.04 amd64; no local model, OCR, weather, news,
Windows builds or automatic updates. Keep all 106 animations, click/drag and
gesture interactions, calendar and meal scheduling, low-power mode, tray,
autostart, settings, greeting and farewell.

- [x] Keep the standalone `src/` layout, Make entry points and Debian packaging.
- [x] Add the upstream ACP Agent runtime and internal control tools under `src/Agent` and `src/Control`.
- [x] Restrict Harnesses to Claude Code, Codex CLI and OpenCode.
- [x] Reuse the main executable as the stdio MCP bridge (`--mcp-bridge --pipe ...`), so the package remains a single app directory.
- [x] Add double-click floating input, `say` / `say_next` typing, external action queue, Puppet mode and daily-care wakeups.
- [x] Add settings for Harness selection, workspace, model/permission options, persona and Agent response switches.
- [x] Remove the previous Claude Code CLI + memory + systemd reminder prototype.
- [x] Add X11 foreground/fullscreen capture for Agent tools and the `libxcomposite1` dependency.
- [x] Replace core tests with ACP session, workspace, wakeup, settings, MCP protocol and bridge checks.
- [x] Verify build, core tests, Xvfb UI, interaction tests and MCP bridge mode.

Build uses self-contained .NET 10 linux-x64 publish output in build/app; executable
is bigbluefish. Packaging uses that output, installs to /opt/bigbluefish and
/usr/bin/bigbluefish, and writes dist/bigbluefish_<version>_amd64.deb.
DESTDIR supports reversible staged install verification. User settings remain in
$XDG_DATA_HOME/bigbluefish (default ~/.local/share/bigbluefish).

Agent data is stored under the same data directory in `agent/`: workspace,
session references, wakeups and npm adapters. Agent transcripts and model requests
belong to the selected CLI; screenshots and tool results may be sent to its
configured cloud model. The pet does not store API keys.

Compatibility verification must distinguish this Ubuntu 22.04 host from any
Ubuntu 24.04 CI execution; do not claim a local 24.04 test without one.

Verification on Ubuntu 22.04: `make build`, `make test`, `make test-ui`,
`make test-agent`, `make test-interactions`, staged install/uninstall and Debian
assembly. Ubuntu 24.04 build/test/package coverage runs in CI.
