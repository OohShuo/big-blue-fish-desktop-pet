#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
for tool in make python3 dpkg-deb gst-inspect-1.0; do
    command -v "$tool" >/dev/null || { echo "Missing tool: $tool" >&2; exit 1; }
done
[[ $(uname -m) == x86_64 ]] || { echo 'Only amd64 is supported.' >&2; exit 1; }
# Do not install software or change the host configuration.
source /etc/os-release
[[ $ID == ubuntu && ($VERSION_ID == 22.04 || $VERSION_ID == 24.04) ]] || { echo 'Supported systems: Ubuntu 22.04 and 24.04.' >&2; exit 1; }
dotnet=${DOTNET:-dotnet}
command -v "$dotnet" >/dev/null || { echo 'Install .NET 10 SDK or set DOTNET=/path/to/dotnet.' >&2; exit 1; }
[[ $("$dotnet" --version) == 10.* ]] || { echo '.NET 10 SDK is required.' >&2; exit 1; }
python3 - <<'CHECK_LIBRARIES'
import ctypes
import sys
for library in ('libX11.so.6', 'libXext.so.6', 'libXcomposite.so.1', 'libXi.so.6', 'libXcursor.so.1',
                'libXrandr.so.2', 'libXrender.so.1', 'libICE.so.6', 'libSM.so.6',
                'libfontconfig.so.1', 'libfreetype.so.6', 'libdbus-1.so.3',
                'libgstreamer-1.0.so.0', 'libgstapp-1.0.so.0'):
    try:
        ctypes.CDLL(library)
    except OSError as error:
        sys.exit(f'Missing runtime library: {error}')
CHECK_LIBRARIES
for element in vp9alphadecodebin matroskademux vp9parse videoconvert videoscale appsink; do
    gst-inspect-1.0 "$element" >/dev/null || { echo "Missing GStreamer element: $element" >&2; exit 1; }
done
# Resolve a path for use from make. Make-significant characters are rejected explicitly.
dotnet=$(command -v "$dotnet")
[[ $dotnet != *[' #$'\\]* ]] || { echo 'Use an SDK path without whitespace or Make metacharacters.' >&2; exit 1; }
printf 'DOTNET := %s\nCONFIGURATION := %s\n' "$dotnet" "${CONFIGURATION:-Release}" > config.mk
printf 'Configured Ubuntu %s amd64; .NET %s\n' "$VERSION_ID" "$("$dotnet" --version)"
