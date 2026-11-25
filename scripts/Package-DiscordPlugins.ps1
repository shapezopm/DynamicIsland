[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../publish')
)

$ErrorActionPreference = 'Stop'
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$plugins = Join-Path $output 'Assets/Plugins'
$betterDiscord = Join-Path $plugins 'BetterDiscord'
$vencord = Join-Path $plugins 'Vencord'
$bridge = Join-Path $PSScriptRoot '../src/Discord/DynamicIslandDiscordBridge.plugin.js'

New-Item -ItemType Directory -Path $betterDiscord, $vencord -Force | Out-Null
Copy-Item -LiteralPath $bridge -Destination $betterDiscord -Force
& (Join-Path $PSScriptRoot 'vencord-setup/Build-DynamicIslandVencord.ps1') -OutputDirectory $vencord

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $output 'Plugins.zip'
$pending = "$archive.$([guid]::NewGuid().ToString('N')).tmp"
try {
    [IO.Compression.ZipFile]::CreateFromDirectory($plugins, $pending,
        [IO.Compression.CompressionLevel]::Optimal, $true)
    Move-Item -LiteralPath $pending -Destination $archive -Force
}
finally {
    if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending }
}
Get-Item -LiteralPath $archive
