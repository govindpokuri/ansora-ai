$ErrorActionPreference = 'Stop'
$modelDirectory = Join-Path $PSScriptRoot 'models'
$modelPath = Join-Path $modelDirectory 'ggml-base.en.bin'
$expectedHash = 'a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002'
New-Item -ItemType Directory -Force -Path $modelDirectory | Out-Null
if ((Test-Path -LiteralPath $modelPath) -and (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -eq $expectedHash) {
    Write-Output 'Whisper English model is ready.'
    return
}
$temporaryPath = $modelPath + '.download'
Invoke-WebRequest -Uri 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin' -OutFile $temporaryPath
if ((Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Model checksum mismatch. Download was not installed.'
}
Move-Item -LiteralPath $temporaryPath -Destination $modelPath -Force
Write-Output 'Whisper English model is ready.'
