#!/usr/bin/env bash
# Optional: install a minimal X display server + the X11/Skia client libraries that
# n+ (Avalonia) needs to open a window. Intended for "lite"/minimal Ubuntu/Debian
# installs that ship without a graphical desktop.
#
# Safe to skip if you already run a desktop environment and only need the runtime
# libraries — see the LIBS-only note below.
#
# Usage:
#   ./packaging/install-gui-deps.sh            # X server + window manager + libraries
#   ./packaging/install-gui-deps.sh --libs     # only the runtime libraries (no X server)
set -euo pipefail

if ! command -v apt-get >/dev/null 2>&1; then
    echo "This script is for Debian/Ubuntu (apt). For other distros install the" >&2
    echo "equivalent packages: an X server plus libX11, libICE, libSM, libfontconfig," >&2
    echo "libXext, libXrender, libXi, libXrandr, libXcursor and libGL." >&2
    exit 1
fi

SUDO=""
if [ "$(id -u)" -ne 0 ]; then
    if command -v sudo >/dev/null 2>&1; then
        SUDO="sudo"
    else
        echo "Run as root or install sudo." >&2
        exit 1
    fi
fi

# Runtime libraries Avalonia loads at startup (the cause of "no window" / libICE errors).
LIBS=(
    libx11-6 libice6 libsm6 libfontconfig1
    libxext6 libxrender1 libxi6 libxrandr2 libxcursor1 libgl1
)

# A minimal display server + lightweight window manager so a GUI can actually show.
XSERVER=( xserver-xorg xinit openbox )

PACKAGES=( "${LIBS[@]}" )
if [ "${1:-}" != "--libs" ]; then
    PACKAGES+=( "${XSERVER[@]}" )
fi

echo "==> apt update"
$SUDO apt-get update

echo "==> Installing: ${PACKAGES[*]}"
$SUDO apt-get install -y "${PACKAGES[@]}"

echo
echo "Done."
if [ "${1:-}" != "--libs" ]; then
    echo "Start a bare X session with 'startx', then launch 'nplus' from it,"
    echo "or log in to your desktop environment and run 'nplus'."
else
    echo "Runtime libraries installed. Launch 'nplus' from your graphical desktop."
fi
