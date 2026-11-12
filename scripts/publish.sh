#!/bin/sh
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 The OpenVirtue Authors
#
# Builds the player's OpenVirtue: one self-contained executable per platform, no .NET install needed.
# usage: scripts/publish.sh [runtime-id ...]   (default: linux-x64 win-x64)
# Output: artifacts/OpenVirtue-<rid>/OpenVirtue[.exe]. Players put their game files in a "game" folder beside it.
set -eu
cd "$(dirname "$0")/.."
[ "$#" -gt 0 ] || set -- linux-x64 win-x64
for rid in "$@"; do
  out="artifacts/OpenVirtue-$rid"
  rm -rf "$out"
  dotnet publish src/OpenVirtue.App -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -p:GenerateDocumentationFile=false -o "$out"
  find "$out" -name '*.xml' -delete
  mkdir -p "$out/game"
  printf '%s\n' "Copy your Saints of Virtue files (TITLE.WRS, START.WRS, APATHY.WRS, HEART.WRS," \
    "LEGALISM.WRS, NEWAGE.WRS) into this folder, then start OpenVirtue." > "$out/game/PUT-GAME-FILES-HERE.txt"
  echo "published $out"
done
