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

# Pick a dotnet whose SDK can target net10 (major version >= 10). Candidates, in order:
# an explicit $DOTNET, the per-user dotnet-install.sh location, then PATH. This skips an
# older system SDK (e.g. /usr/bin/dotnet 6.x) that can't build this project.
sdk_supports_net10() { "$1" --list-sdks 2>/dev/null | grep -qE '^[1-9][0-9]+\.'; }
DOTNET_BIN=""
for cand in "${DOTNET:-}" "$HOME/.dotnet/dotnet" dotnet; do
    [ -n "$cand" ] || continue
    resolved="$(command -v "$cand" 2>/dev/null || true)"
    [ -n "$resolved" ] || continue
    if sdk_supports_net10 "$resolved"; then DOTNET_BIN="$resolved"; break; fi
done
if [ -z "$DOTNET_BIN" ]; then
    echo "error: no .NET SDK that can target net10 was found (need major version >= 10)." >&2
    echo "Install it:  ./dotnet-install.sh --channel 10.0   (then re-run)" >&2
    if command -v dotnet >/dev/null 2>&1; then echo "Detected SDKs:" >&2; dotnet --list-sdks >&2 || true; fi
    exit 1
fi
DOTNET="$DOTNET_BIN"
echo "==> Using dotnet: $DOTNET"

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
