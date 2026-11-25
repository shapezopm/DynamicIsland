[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '../../publish/VencordSetup')
)

$ErrorActionPreference = 'Stop'
$vencordRevision = 'd086992fb674299a2fa05ac6432edc3a777389f3'
$installerUrl = 'https://github.com/Vencord/Installer/releases/download/v1.4.2/VencordInstaller.exe'
$installerHash = '1CB4115A99A8B7A0B0371DDDB2AC7DDE38B9ACC23F53A69540319B4B7CA6C2DA'
$work = Join-Path ([IO.Path]::GetTempPath()) ('dynamicisland-vencord-package-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'source'
$payload = Join-Path $work 'payload'
$dist = Join-Path $payload 'dist'
$bundleName = 'DynamicIslandVencord-Build.zip'
$launcherName = 'Install-DynamicIslandVencord.cmd'
$pluginName = 'dynamicIslandBridge.desktop'

function Invoke-Checked([string] $Program, [string[]] $CommandArgs) {
    & $Program @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "$Program exited with code $LASTEXITCODE" }
}

New-Item -ItemType Directory -Path $source, $dist -Force | Out-Null
Push-Location -LiteralPath $source
try {
    Invoke-Checked git @('init', '--quiet')
    Invoke-Checked git @('remote', 'add', 'origin', 'https://github.com/Vendicated/Vencord.git')
    Invoke-Checked git @('fetch', '--quiet', '--depth', '1', 'origin', $vencordRevision)
    Invoke-Checked git @('checkout', '--quiet', '-B', 'main', 'FETCH_HEAD')

    $pluginDirectory = Join-Path $PSScriptRoot "../../src/Discord/Vencord/$pluginName"
    New-Item -ItemType Directory -Path 'src/userplugins' | Out-Null
    Copy-Item -LiteralPath $pluginDirectory -Destination 'src/userplugins' -Recurse
    Invoke-Checked pnpm.cmd @('install', '--frozen-lockfile')
    Invoke-Checked pnpm.cmd @('build')

    if (-not (Select-String -LiteralPath 'dist/renderer.js' -Pattern 'DynamicIslandDiscordBridge' -SimpleMatch -Quiet)) {
        throw 'The renderer build does not contain the DynamicIsland plugin.'
    }
    Get-ChildItem -LiteralPath 'dist' -File |
        Where-Object Name -Match '^(patcher|preload|renderer)\.' |
        Copy-Item -Destination $dist
    Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $payload 'Vencord-LICENSE.txt')

    Invoke-Checked git @('add', '-f', "src/userplugins/$pluginName")
    $tree = Invoke-Checked git @('write-tree')
    Invoke-Checked git @('archive', '--format=zip', '--prefix=Vencord/',
        "--output=$(Join-Path $payload 'Vencord-Source.zip')", $tree)

    $installer = Join-Path $payload 'VencordInstaller.exe'
    Invoke-Checked curl.exe @('--fail', '--location', '--silent', '--show-error', '--retry', '2',
        '--output', $installer, $installerUrl)
    if ((Get-FileHash -LiteralPath $installer).Hash -ne $installerHash) {
        throw 'The official Vencord installer checksum does not match.'
    }

    $version = (Get-Content -LiteralPath 'package.json' -Raw | ConvertFrom-Json).version
    @(
        "Vencord version: $version"
        "Vencord source: https://github.com/Vendicated/Vencord/tree/$vencordRevision"
        'The included Vencord-Source.zip also contains the DynamicIsland plugin used in this build.'
        'Build: pnpm install --frozen-lockfile; pnpm build'
        'Official installer source: https://github.com/Vencord/Installer/tree/v1.4.2'
        "Official installer SHA-256: $installerHash"
    ) | Set-Content -LiteralPath (Join-Path $payload 'BUILD.txt') -Encoding ascii

    $archive = Join-Path $work $bundleName
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive)
    $archiveHash = (Get-FileHash -LiteralPath $archive).Hash
    $template = Get-Content -LiteralPath (Join-Path $PSScriptRoot $launcherName) -Raw
    $launcher = Join-Path $work $launcherName
    [IO.File]::WriteAllText($launcher, $template.Replace('@BUNDLE_SHA256@', $archiveHash).Replace("`r`n", "`n").Replace("`n", "`r`n"))
}
finally {
    Pop-Location
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item -LiteralPath $archive, $launcher, (Join-Path $PSScriptRoot 'README.md') -Destination $OutputDirectory
Get-ChildItem -LiteralPath $OutputDirectory -File | Select-Object Name, Length
Write-Host "Built Vencord $version ($vencordRevision). Build files: $work"
