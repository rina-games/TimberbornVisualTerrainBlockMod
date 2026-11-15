#!/usr/bin/env bash
# 把仓库里的 Blueprints / Localizations / manifest 同步到游戏的 mods 目录。
# 用法： tools/deploy.sh
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${MOD_DEST:-$HOME/Documents/Timberborn/Mods/VisualTerrainBlock}"

mkdir -p "$DEST"
# 建筑蓝图必须在 mod 根目录的 Buildings/ 下（放在 Blueprints/ 里会 Blueprint not found）
rsync -a --delete "$REPO/Buildings/" "$DEST/Buildings/"
rsync -a --delete "$REPO/Blueprints/" "$DEST/Blueprints/"
rsync -a --delete "$REPO/Localizations/" "$DEST/Localizations/"
cp "$REPO/manifest.json" "$DEST/manifest.json"
if [ -f "$REPO/thumbnail.png" ]; then
  cp "$REPO/thumbnail.png" "$DEST/thumbnail.png"
fi

python3 "$REPO/tools/check-localization.py"

echo "deployed -> $DEST"
find "$DEST" -type f | sort
