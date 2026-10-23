$ErrorActionPreference = 'Stop'

$patterns = [ordered]@{
    'private key material' = '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
    'GitHub token' = 'gh[pousr]_[A-Za-z0-9]{30,}'
    'AWS access key' = 'AKIA[0-9A-Z]{16}'
    'Slack token' = 'xox[baprs]-[A-Za-z0-9-]{20,}'
    'private LAN address' = '\b192\.168\.\d{1,3}\.\d{1,3}\b'
    'private DNS suffix' = '\bhome\.arpa\b'
}

$failed = $false
$files = git ls-files
foreach ($file in $files) {
    if ($file -eq 'scripts/check-secrets.ps1') { continue }
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
    $content = Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue
    if ($null -eq $content) { continue }
    foreach ($entry in $patterns.GetEnumerator()) {
        if ($content -match $entry.Value) {
            Write-Error "$($entry.Key) detected in $file" -ErrorAction Continue
            $failed = $true
        }
    }
}

if ($failed) { exit 1 }
Write-Host 'Secret and private-infrastructure scan passed.'
