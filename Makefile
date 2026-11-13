.DEFAULT_GOAL := build
SHELL := /bin/bash
-include config.mk
DOTNET ?= $(if $(wildcard $(CURDIR)/.dotnet/dotnet),$(CURDIR)/.dotnet/dotnet,dotnet)
CONFIGURATION ?= Release
INTERACTION_TEST_ARGS ?=
DESTDIR ?=
export DOTNET CONFIGURATION

.PHONY: all configure build run test pack install uninstall clean distclean test-ui test-interactions test-agent help
all: build
configure:
	@bash packaging/configure.sh
build:
	@rm -rf build/app
	@"$(DOTNET)" publish src/BigBlueFish.csproj -c "$(CONFIGURATION)" -r linux-x64 --self-contained true -p:RestoreLockedMode=true -o build/app --nologo
run: build
	@build/app/bigbluefish
test:
	@python3 tests/test_project.py
	@"$(DOTNET)" run --project tests/CoreTests.csproj -c "$(CONFIGURATION)" -- "$(CURDIR)/assets/fish"
test-ui: build
	@python3 tests/ui_smoke.py
test-interactions:
	@mkdir -p build
	@set -o pipefail; xvfb-run -a "$(DOTNET)" run --project tests/ui/InteractionTests.csproj -c "$(CONFIGURATION)" -- $(INTERACTION_TEST_ARGS) | tee build/interactions.log
	@python3 -c 'from pathlib import Path; assert "PASS: complete clips and deferred interaction transitions" in Path("build/interactions.log").read_text(), "Interaction suite did not complete; close any running pet before testing"'
test-agent:
	@test -x build/app/bigbluefish || { echo 'Run make build first.' >&2; exit 1; }
	@python3 tests/agent_runtime.py
pack: build
	@bash packaging/pack.sh
install:
	@test -x build/app/bigbluefish || { echo 'Run make build before installing.' >&2; exit 1; }
	@bash packaging/install.sh install "$(DESTDIR)"
uninstall:
	@bash packaging/install.sh uninstall "$(DESTDIR)"
clean:
	rm -rf build dist src/bin src/obj tests/bin tests/obj tests/ui/bin tests/ui/obj
distclean: clean
	rm -f config.mk
help:
	@echo 'make configure  Check dependencies and write config.mk'
	@echo 'make [build]    Build self-contained build/app/bigbluefish'
	@echo 'make run        Build and launch the companion'
	@echo 'make test       Verify assets, ACP sessions, workspace, settings, MCP and video decoding'
	@echo 'make test-ui    Verify real windows under Xvfb'
	@echo 'make test-interactions  Verify complete clips and queued interactions'
	@echo 'make pack       Build dist/bigbluefish_VERSION_amd64.deb'
	@echo 'make install    Install built app (sudo for system; DESTDIR for staging)'
	@echo 'make uninstall  Remove app, preserving user settings'
	@echo 'make clean      Remove build and package outputs'
	@echo 'make distclean  Also remove local configuration'
	@echo 'make test-agent  Verify published MCP bridge mode without API calls'
