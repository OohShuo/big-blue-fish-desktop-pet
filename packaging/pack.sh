#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/BigBlueFish.csproj)
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid project version.' >&2; exit 1; }
mkdir -p build dist
work=$(mktemp -d "$PWD/build/deb.XXXXXXXX")
trap 'rm -rf -- "$work"' EXIT
bash packaging/install.sh install "$work/root"
mkdir -p "$work/root/DEBIAN"
size=$(du -sk "$work/root" | cut -f1)
cat > "$work/root/DEBIAN/control" <<CONTROL
Package: bigbluefish
Version: $version
Section: games
Priority: optional
Architecture: amd64
Maintainer: shuo <shuo@localhost>
Installed-Size: $size
Depends: libc6 (>= 2.35), libstdc++6, libgcc-s1, libssl3 | libssl3t64, libicu70 | libicu74, libfontconfig1, libfreetype6, libx11-6, libxext6, libxcomposite1, libxi6, libxcursor1, libxrandr2, libxrender1, libice6, libsm6, libdbus-1-3, libgstreamer1.0-0 (>= 1.20), libgstreamer-plugins-base1.0-0, gstreamer1.0-plugins-base, gstreamer1.0-plugins-good, gstreamer1.0-plugins-bad, fonts-noto-cjk
Conflicts: bigbluefish-safe, bigbluefish-safe-nomodel
Replaces: bigbluefish-safe, bigbluefish-safe-nomodel
Description: Big Blue Fish animated desktop companion
 Desktop animations with ACP chat for Claude Code, Codex CLI and OpenCode.
 Built for Ubuntu 22.04 and 24.04 amd64.
CONTROL
for script in postinst postrm; do
    install -m755 packaging/update-caches.sh "$work/root/DEBIAN/$script"
done
name="bigbluefish_${version}_amd64.deb"
dpkg-deb --root-owner-group -Zxz --build "$work/root" "dist/$name"
(cd dist && sha256sum "$name" > "$name.sha256")
echo "Package: dist/$name"
