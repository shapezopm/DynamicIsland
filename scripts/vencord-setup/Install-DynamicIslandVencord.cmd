@echo off
setlocal DisableDelayedExpansion
title DynamicIsland - Vencord setup

set "DI_ZIP=%~dp0DynamicIslandVencord-Build.zip"
set "DI_ZIP_HASH=@BUNDLE_SHA256@"
set "DI_INSTALLER_HASH=1CB4115A99A8B7A0B0371DDDB2AC7DDE38B9ACC23F53A69540319B4B7CA6C2DA"
set "VENCORD_USER_DATA_DIR=%LOCALAPPDATA%\DynamicIsland\VencordSetup\%DI_ZIP_HASH:~0,16%"
set "VENCORD_DEV_INSTALL=1"
set "PSModulePath="

echo Checking the DynamicIsland Vencord bundle...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "if (-not (Test-Path -LiteralPath $env:DI_ZIP -PathType Leaf)) { throw 'Put DynamicIslandVencord-Build.zip next to this script.' };" ^
  "if ((Get-FileHash -LiteralPath $env:DI_ZIP).Hash -ne $env:DI_ZIP_HASH) { throw 'Archive checksum mismatch. Use the ZIP and CMD from the same release.' };" ^
  "Add-Type -AssemblyName System.IO.Compression.FileSystem;" ^
  "$target = $env:VENCORD_USER_DATA_DIR; $installerName = 'VencordInstaller.exe';" ^
  "$parent = [IO.Path]::GetDirectoryName($target); [IO.Directory]::CreateDirectory($parent) | Out-Null;" ^
  "$stage = Join-Path $parent ([guid]::NewGuid().ToString('N') + '.tmp');" ^
  "$archive = [IO.Compression.ZipFile]::OpenRead($env:DI_ZIP);" ^
  "try {" ^
  "  $files = @($archive.Entries | Where-Object { $_.FullName -cmatch '^(dist/[A-Za-z0-9][A-Za-z0-9._-]*|VencordInstaller\.exe)$' });" ^
  "  foreach ($name in @('dist/patcher.js', 'dist/preload.js', 'dist/renderer.js', 'dist/renderer.css', $installerName)) { if ($name -cnotin $files.FullName) { throw ('Incomplete bundle: ' + $name) } };" ^
  "  if (-not (Test-Path -LiteralPath $target)) {" ^
  "    [IO.Directory]::CreateDirectory((Join-Path $stage 'dist')) | Out-Null;" ^
  "    foreach ($entry in $files) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $stage $entry.FullName)) };" ^
  "    if ((Get-FileHash -LiteralPath (Join-Path $stage $installerName)).Hash -ne $env:DI_INSTALLER_HASH) { throw 'Vencord installer checksum mismatch.' };" ^
  "    [IO.Directory]::Move($stage, $target);" ^
  "  };" ^
  "  foreach ($entry in $files) {" ^
  "    $file = Join-Path $target $entry.FullName; $stream = $entry.Open();" ^
  "    try { if ((Get-FileHash -LiteralPath $file).Hash -ne (Get-FileHash -InputStream $stream).Hash) { throw ('Installed bundle does not match: ' + $file) } } finally { $stream.Dispose() };" ^
  "  };" ^
  "  if ((Get-FileHash -LiteralPath (Join-Path $target $installerName)).Hash -ne $env:DI_INSTALLER_HASH) { throw 'Vencord installer checksum mismatch.' };" ^
  "} finally { $archive.Dispose(); if ([IO.Directory]::Exists($stage)) { [IO.Directory]::Delete($stage, $true) } };"
if errorlevel 1 goto failed
if /i "%~1"=="--prepare-only" exit /b 0

echo Opening the official Vencord Installer.
echo Select your Discord and choose Install or Reinstall / Repair.
start "" /wait "%VENCORD_USER_DATA_DIR%\VencordInstaller.exe"
if errorlevel 1 goto failed
exit /b 0

:failed
echo.
echo Setup could not continue. See the error above.
if /i "%~1"=="--prepare-only" exit /b 1
pause
exit /b 1
