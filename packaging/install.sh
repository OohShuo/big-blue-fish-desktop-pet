#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
operation=${1:?install or uninstall}
stage=${2:-}
[[ -z $stage || $stage == /* ]] || { echo 'DESTDIR must be an absolute path.' >&2; exit 1; }
case "$operation" in
    install)
        install -d "$stage/opt/bigbluefish" "$stage/usr/bin" "$stage/usr/share/applications" "$stage/usr/share/icons/hicolor/256x256/apps" "$stage/usr/share/doc/bigbluefish/licenses"
        cp -a build/app/. "$stage/opt/bigbluefish/"
        printf '#!/bin/sh\nexec /opt/bigbluefish/bigbluefish "$@"\n' > "$stage/usr/bin/bigbluefish"
        chmod 755 "$stage/usr/bin/bigbluefish"
        install -m644 packaging/bigbluefish.desktop "$stage/usr/share/applications/bigbluefish.desktop"
        install -m644 assets/bigbluefish.png "$stage/usr/share/icons/hicolor/256x256/apps/bigbluefish.png"
        install -m644 LICENSE "$stage/usr/share/doc/bigbluefish/copyright"
        install -m644 THIRD-PARTY.md "$stage/usr/share/doc/bigbluefish/THIRD-PARTY.md"
        install -m644 assets/fish/LICENSE.txt "$stage/usr/share/doc/bigbluefish/licenses/Character-MIT.txt"
        install -m644 licenses/* "$stage/usr/share/doc/bigbluefish/licenses/"
        ;;
    uninstall)
        rm -rf -- "$stage/opt/bigbluefish" "$stage/usr/share/doc/bigbluefish"
        rm -f -- "$stage/usr/bin/bigbluefish" "$stage/usr/share/applications/bigbluefish.desktop" "$stage/usr/share/icons/hicolor/256x256/apps/bigbluefish.png"
        ;;
    *) echo "Unknown operation: $operation" >&2; exit 1 ;;
esac
if [[ -z $stage ]]; then
    if command -v update-desktop-database >/dev/null; then update-desktop-database -q /usr/share/applications || true; fi
    if command -v gtk-update-icon-cache >/dev/null; then gtk-update-icon-cache -q -f -t /usr/share/icons/hicolor || true; fi
fi
