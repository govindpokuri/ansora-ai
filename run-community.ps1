$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$portableBinary = Join-Path $PSScriptRoot 'AnsoraAI.exe'
$portable = Test-Path -LiteralPath $portableBinary
$settingsFile = if ($portable) { Join-Path $PSScriptRoot 'settings.defaults.json' } else { Join-Path $PSScriptRoot 'OverlayCopilot/settings.defaults.json' }
$settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
$url = Read-Host 'Full HTTPS chat-completions URL (Enter keeps current)'
if ($url.Trim()) { $settings.AnswerUrl = $url.Trim() }
$endpoint = $null
if (-not [Uri]::TryCreate($settings.AnswerUrl, [UriKind]::Absolute, [ref]$endpoint) -or
    ($endpoint.Scheme -ne 'https' -and -not ($endpoint.Scheme -eq 'http' -and $endpoint.IsLoopback)) -or
    $endpoint.UserInfo) { throw 'Use HTTPS (or localhost HTTP) with no credentials in the URL.' }
if ($endpoint.Host -eq 'api.example.com') { throw 'Enter your actual AI endpoint before running.' }
$model = Read-Host 'Model/deployment name (Enter keeps current)'
if ($model.Trim()) { $settings.AnswerModel = $model.Trim() }
if ($settings.AnswerModel -eq 'YOUR-MODEL') { throw 'Enter your actual model/deployment name.' }
$auth = Read-Host 'Authentication: Bearer or api-key (Enter keeps current)'
if ($auth.Trim()) { $settings.AnswerAuth = $auth.Trim() }
if ($settings.AnswerAuth -notin @('Bearer', 'api-key')) { throw 'Choose Bearer or api-key.' }
$settings.CommercialMode = $false
$settings.CommercialBaseUrl = ''
$settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settingsFile -Encoding utf8
if (-not $portable) {
    & dotnet build OverlayCopilot/OverlayCopilot.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
if ($portable) {
    # No SDK is needed in the self-contained portable release.
    & (Join-Path $PSScriptRoot 'download-model.ps1')
}
$secureKey = Read-Host 'Your AI API key (hidden; Enter reuses the saved key)' -AsSecureString
$oldKey = [Environment]::GetEnvironmentVariable('OVERLAYCOPILOT_AI_KEY', 'Process')
$pointer = [IntPtr]::Zero
try {
    if ($secureKey.Length -gt 0) {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
        $processKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        [Environment]::SetEnvironmentVariable('OVERLAYCOPILOT_AI_KEY', $processKey, 'Process')
        $processKey = $null
    }
    $binary = if ($portable) { $portableBinary } else { Join-Path $PSScriptRoot 'OverlayCopilot/bin/Release/net9.0-windows/AnsoraAI.exe' }
    # Saved UI settings override defaults, so update only the Community profile.
    $profileDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'AnsoraAICommunity'
    $profileFile = Join-Path $profileDir 'settings.json'
    New-Item -ItemType Directory -Path $profileDir -Force | Out-Null
    if (Test-Path -LiteralPath $profileFile) {
        $saved = Get-Content -LiteralPath $profileFile -Raw | ConvertFrom-Json
        foreach ($field in @('AnswerUrl','AnswerModel','AnswerAuth','CommercialMode','CommercialBaseUrl')) {
            $saved | Add-Member -NotePropertyName $field -NotePropertyValue $settings.$field -Force
        }
        $saved | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $profileFile -Encoding utf8
    }
    Start-Process -FilePath $binary -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
} finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    [Environment]::SetEnvironmentVariable('OVERLAYCOPILOT_AI_KEY', $oldKey, 'Process')
    $secureKey.Dispose()
}
