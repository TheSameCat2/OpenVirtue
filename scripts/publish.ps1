# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 The OpenVirtue Authors
#
# Builds the player's OpenVirtue: one self-contained executable per platform, no .NET install needed.
# usage: scripts/publish.ps1 [-Rid win-x64,linux-x64]
# Output: artifacts/OpenVirtue-<rid>/OpenVirtue[.exe]. Players put their game files in a "game" folder beside it.
param([string[]]$Rid = @('win-x64', 'linux-x64'))
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
foreach ($r in $Rid) {
    $out = "artifacts/OpenVirtue-$r"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    dotnet publish src/OpenVirtue.App -c Release -r $r --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none -p:GenerateDocumentationFile=false -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $r" }
    Get-ChildItem $out -Filter *.xml | Remove-Item
    New-Item -ItemType Directory -Force (Join-Path $out 'game') | Out-Null
    "Copy your Saints of Virtue files (TITLE.WRS, START.WRS, APATHY.WRS, HEART.WRS,`nLEGALISM.WRS, NEWAGE.WRS) into this folder, then start OpenVirtue." |
        Set-Content (Join-Path $out 'game/PUT-GAME-FILES-HERE.txt')
    Write-Host "published $out"
}
