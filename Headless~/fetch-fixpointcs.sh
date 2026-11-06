#!/usr/bin/env bash
# Clones the pinned FixPointCS commit into Headless~/external/FixPointCS.
# Not needed when FixPointCSDir is set (e.g. to the game's own copy).
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)/external/FixPointCS"
COMMIT=a852f05b428a942f8dc274ee516a893ae224e0d4
if [ ! -d "$DIR/.git" ]; then
  git clone https://github.com/XMunkki/FixPointCS.git "$DIR"
fi
git -C "$DIR" fetch --quiet origin
git -C "$DIR" checkout --quiet "$COMMIT"
