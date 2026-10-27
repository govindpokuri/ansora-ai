$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$downloadDirectory = Join-Path $env:TEMP ('AnsoraCommunityPrerequisite-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
$installerPath = Join-Path $downloadDirectory 'vc_redist.x64.exe'
Invoke-WebRequest -Uri 'https://aka.ms/vc14/vc_redist.x64.exe' -OutFile $installerPath -UseBasicParsing
$signature = Get-AuthenticodeSignature -LiteralPath $installerPath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw 'Microsoft signature verification failed; setup was not launched.'
}
# The user reviews Microsoft's interactive setup, license and UAC prompts.
$setup = Start-Process -FilePath $installerPath -Wait -PassThru
Write-Output ('Microsoft prerequisite setup exit code: ' + $setup.ExitCode)
