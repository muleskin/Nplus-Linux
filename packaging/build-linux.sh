#!/usr/bin/env bash
# Build a self-contained Linux x64 release of n+ and assemble a distributable tarball.
# Usage: packaging/build-linux.sh [Runtime]
#   Runtime defaults to linux-x64 (also try linux-arm64).
set -euo pipefail

RID="${1:-linux-x64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/src/NPlus/NPlus.csproj"
OUT="$ROOT/dist/$RID"
STAGE="$ROOT/dist/nplus-$RID"

# Find dotnet: honor $DOTNET, else PATH, else the per-user dotnet-install.sh location.
DOTNET="${DOTNET:-dotnet}"
if ! command -v "$DOTNET" >/dev/null 2>&1; then
    if [ -x "$HOME/.dotnet/dotnet" ]; then
        DOTNET="$HOME/.dotnet/dotnet"
    else
        echo "error: 'dotnet' not found on PATH and ~/.dotnet/dotnet is missing." >&2
        echo "Install the SDK first:  ./dotnet-install.sh --channel 10.0" >&2
        exit 127
    fi
fi

echo "==> Publishing $RID (self-contained, single file)…"
rm -rf "$OUT" "$STAGE"
"$DOTNET" publish "$PROJ" -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -o "$OUT"

echo "==> Staging tarball layout…"
mkdir -p "$STAGE/bin" "$STAGE/share/applications" "$STAGE/share/icons/hicolor/256x256/apps"
# The single-file exe plus the loose native libs (Skia/HarfBuzz/Oniguruma) it loads at runtime.
cp "$OUT"/nplus "$STAGE/bin/"
cp "$OUT"/*.so "$STAGE/bin/" 2>/dev/null || true
chmod +x "$STAGE/bin/nplus"

cp "$ROOT/packaging/nplus.desktop" "$STAGE/share/applications/nplus.desktop"
cp "$ROOT/src/NPlus/Assets/nplus.png" "$STAGE/share/icons/hicolor/256x256/apps/nplus.png"
cp "$ROOT/packaging/install.sh" "$STAGE/install.sh"
chmod +x "$STAGE/install.sh"
cp "$ROOT/README.md" "$STAGE/README.md" 2>/dev/null || true

TARBALL="$ROOT/dist/nplus-$RID.tar.gz"
echo "==> Creating $TARBALL"
tar -C "$ROOT/dist" -czf "$TARBALL" "nplus-$RID"

echo "Done."
echo "  Binary : $OUT/nplus"
echo "  Tarball: $TARBALL"
echo "Install per-user with: (cd dist/nplus-$RID && ./install.sh)"
