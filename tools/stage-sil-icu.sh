#!/usr/bin/env bash
# Stage SIL ICU 70 for linux-x64 from the pinned packages in tools/icu-payload.json, without root.
#   bash tools/stage-sil-icu.sh [<destination>]
# Prints the folder on its last line; export it as MOTIF_SIL_ICU_STAGE and build.ps1 copies the
# libraries beside the product and the test hosts. CI's Ubuntu job and a developer run the same steps.
set -euo pipefail
repo=$(cd "$(dirname "$0")/.." && pwd)
payload=$repo/tools/icu-payload.json
root=${1:-$HOME/.local/share/motif/sil-icu/linux-x64}
mkdir -p "$root/debs" "$root/extracted"

python3 - "$payload" > "$root/sources.tsv" <<'PY'
import json, sys
runtime = json.load(open(sys.argv[1]))["rids"]["linux-x64"]
print(runtime["sources"][0]["repository"])
for s in runtime["sources"]:
    print("\t".join([s["path"], s["sha256"], str(s.get("ships", False)).lower(), str(s.get("size", ""))]))
PY

repository=$(head -1 "$root/sources.tsv")
while IFS=$'\t' read -r path checksum ships size; do
  deb="$root/debs/$(basename "$path")"
  [ -f "$deb" ] || curl --fail --location --retry 3 --silent "$repository/$path" --output "$deb"
  printf '%s  %s\n' "$checksum" "$deb" | sha256sum --check --status
  if [ -n "$size" ] && [ "$(stat -c '%s' "$deb")" != "$size" ]; then echo "Unexpected size for $deb" >&2; exit 1; fi
  if [ "$ships" = true ]; then dpkg-deb --extract "$deb" "$root/extracted"; fi
done < <(tail -n +2 "$root/sources.tsv")

# patchelf sets each library's search path to its own folder; fetch the upstream static build if absent.
patchelf=$(command -v patchelf || true)
if [ -z "$patchelf" ]; then
  url=$(curl -fsSL https://api.github.com/repos/NixOS/patchelf/releases/latest |
    grep -o '"browser_download_url": *"[^"]*x86_64\.tar\.gz"' | grep -o 'https[^"]*')
  mkdir -p "$root/patchelf" && curl -fsSL "$url" | tar xz -C "$root/patchelf"
  patchelf=$root/patchelf/bin/patchelf
fi

find "$root/extracted" \( -type f -o -type l \) -name 'libicu*.so*' -print0 |
  while IFS= read -r -d '' library; do cp --archive "$library" "$root/"; done
find "$root" -maxdepth 1 -type f -name 'libicu*.so.*' -print0 |
  while IFS= read -r -d '' library; do "$patchelf" --set-rpath '$ORIGIN' "$library"; done
echo "$root"
