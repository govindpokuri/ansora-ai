$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path $PSScriptRoot -Parent
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$root = Join-Path $workspacePath ('artifacts\distribution\AnsoraAI-cPanel-' + $stamp)
$public = Join-Path $root 'public_html'
$private = Join-Path $root 'ansora-private'
New-Item -ItemType Directory -Path $public,$private -Force | Out-Null
foreach ($name in @('index.html','privacy.html','terms.html','refunds.html','robots.txt','sitemap.xml','api.php','.htaccess','.user.ini','assets','updates')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('public\' + $name)) -Destination $public -Recurse -Force
}
$apiFile = Join-Path $public 'api.php'
$apiText = Get-Content -LiteralPath $apiFile -Raw
$apiText = $apiText.Replace("dirname(__DIR__).'/cuemitra-private'", "dirname(__DIR__).'/ansora-private'")
[IO.File]::WriteAllText($apiFile,$apiText,[Text.UTF8Encoding]::new($false))
foreach ($name in @('bootstrap.php','config.example.php','src','database')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $private -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.env.example') -Destination $private
New-Item -ItemType Directory -Path (Join-Path $private 'bin') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\maintenance.php') -Destination (Join-Path $private 'bin')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'DEPLOYMENT.md') -Destination $root
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UPDATES.md') -Destination $root
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RAZORPAY-SETUP.md') -Destination $root
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ZEPTO-SMTP-SETUP.md') -Destination $root
if (Test-Path -LiteralPath (Join-Path $private 'config.local.php')) { throw 'Private machine configuration must not be packaged.' }
$zip = $root + '.zip'
Compress-Archive -Path (Join-Path $root '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Output ('cPanel deployment package: ' + $zip)
Write-Output 'Contains no production credentials. Read DEPLOYMENT.md before uploading.'
