# BNL BepInEx Launch Helper
# Preloader patcher handles Assembly-CSharp. This script just writes
# config files and launches the game directly.
$gameRoot = "H:\Programas\Steam\steamapps\common\BlockNLoad"
$gameExe = Join-Path $gameRoot "Win64\BlockNLoad.exe"
"299360" | Out-File -FilePath (Join-Path $gameRoot "steam_appid.txt") -Encoding ascii -NoNewline
"public v310.blocknload.pauldh.nl 28100" | Out-File -FilePath (Join-Path $gameRoot "servers.txt") -Encoding ascii
Write-Host "Config OK. Launching..."
Start-Process -FilePath $gameExe -WorkingDirectory $gameRoot
Write-Host "Done."
