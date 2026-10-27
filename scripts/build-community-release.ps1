param([string]$Version = '0.1.14-community.1', [string]$RuntimeVersion = '9.0.22', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$changes = & git -C $root status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Build a release from a clean committed checkout.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts/releases/$Version" }
$releaseRoot = [IO.Path]::GetFullPath($OutputDirectory)
$bundle = Join-Path $releaseRoot 'AnsoraAI-Community-win-x64'
if (Test-Path -LiteralPath $bundle) { throw 'Bundle directory already exists. Choose a new OutputDirectory to preserve previous builds.' }
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
& dotnet publish (Join-Path $root 'OverlayCopilot/OverlayCopilot.csproj') -c Release -r win-x64 --self-contained true "-p:RuntimeFrameworkVersion=$RuntimeVersion" -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true -o $bundle
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
# Keep the portable ZIP specific to Windows x64. Package restores can include
# native speech libraries for other platforms even with an explicit RID.
$bundlePrefix = [IO.Path]::GetFullPath($bundle).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$runtimeDirectory = Join-Path $bundle 'runtimes'
if (Test-Path -LiteralPath $runtimeDirectory) {
    $unneeded = Get-ChildItem -LiteralPath $runtimeDirectory -Directory -Recurse | Where-Object { $_.Name -match '^(linux-|osx-|android-|ios-|win-arm64$|win-x86$)' } | Sort-Object { $_.FullName.Length } -Descending
    foreach ($directory in $unneeded) {
        $target = [IO.Path]::GetFullPath($directory.FullName)
        if (-not $target.StartsWith($bundlePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime pruning target escaped the generated bundle.' }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
foreach ($name in @('run-community.ps1','download-model.ps1','LICENSE','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $bundle
}
Copy-Item -LiteralPath (Join-Path $root 'packaging/community/Start.cmd') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $root 'packaging/community/FIRST-RUN.txt') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $root 'packaging/community/Install-Prerequisites.ps1') -Destination $bundle

# Include package-provided license texts; do not collect fonts, user files,
# credentials or previously published test packages from this machine.
$notices = Join-Path $bundle 'THIRD-PARTY-LICENSES'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
$assets = Get-Content -LiteralPath (Join-Path $root 'OverlayCopilot/obj/project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$summary = @()
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    $packageId = $library.Name.Replace('/', '-')
    foreach ($packageRoot in $packageRoots) {
        $folder = Join-Path $packageRoot $library.Value.path
        if (-not (Test-Path -LiteralPath $folder)) { continue }
        $destination = Join-Path $notices $packageId
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $folder -File) {
            if ($file.Name -match '^(LICENSE|COPYING|ThirdPartyNotices|THIRD-PARTY-NOTICES)(\..*)?$') {
                Copy-Item -LiteralPath $file.FullName -Destination $destination
            }
        }
        $spec = Get-ChildItem -LiteralPath $folder -Filter '*.nuspec' -File | Select-Object -First 1
        if ($spec) {
            [xml]$xml = Get-Content -LiteralPath $spec.FullName -Raw
            $summary += [PSCustomObject]@{ Package = $library.Name; License = [string]$xml.package.metadata.license.'#text'; LicenseUrl = [string]$xml.package.metadata.licenseUrl; ProjectUrl = [string]$xml.package.metadata.projectUrl }
            Copy-Item -LiteralPath $spec.FullName -Destination $destination
        }
        break
    }
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $notices 'packages.json') -Encoding utf8

$config = Get-Content -LiteralPath (Join-Path $bundle 'settings.defaults.json') -Raw | ConvertFrom-Json
if ($config.CommercialMode -or $config.AnswerUrl -ne 'https://api.example.com/v1/chat/completions') { throw 'Release must use credential-free Community defaults.' }
$forbidden = Get-ChildItem -LiteralPath $bundle -Recurse -File | Where-Object { $_.Name -in @('test-connection.json','config.local.php','commercial.mode','.env','.env.production') -or $_.Extension -in @('.dpapi','.pfx','.key') }
if ($forbidden) { throw 'Private configuration found; release not packaged.' }
$zip = Join-Path $releaseRoot "AnsoraAI-Community-$Version-win-x64.zip"
Compress-Archive -LiteralPath $bundle -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Community bundle: $zip"
Write-Output "SHA-256: $hash"
