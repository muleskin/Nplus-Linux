#!/usr/bin/env bash
# Per-user installer for n+ (no root required). Installs into ~/.local.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"

APPDIR="$PREFIX/lib/nplus"
BINLINK="$PREFIX/bin/nplus"
DESKTOP_DIR="$PREFIX/share/applications"
ICON_DIR="$PREFIX/share/icons/hicolor/256x256/apps"

echo "==> Installing n+ to $PREFIX"
mkdir -p "$APPDIR" "$PREFIX/bin" "$DESKTOP_DIR" "$ICON_DIR"

# App files (binary + native .so libs it loads at runtime).
cp -f "$HERE/bin/"* "$APPDIR/"
chmod +x "$APPDIR/nplus"

# Launcher symlink on PATH.
ln -sf "$APPDIR/nplus" "$BINLINK"

# Desktop entry — point Exec at the installed binary.
sed "s|^Exec=.*|Exec=$APPDIR/nplus %F|" "$HERE/share/applications/nplus.desktop" > "$DESKTOP_DIR/nplus.desktop"
cp -f "$HERE/share/icons/hicolor/256x256/apps/nplus.png" "$ICON_DIR/nplus.png"

# Refresh caches if the tools are present (non-fatal).
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DESKTOP_DIR" || true
command -v gtk-update-icon-cache  >/dev/null 2>&1 && gtk-update-icon-cache -f -t "$PREFIX/share/icons/hicolor" || true

echo "Done. Launch with 'nplus' (ensure $PREFIX/bin is on your PATH) or from your app menu."
