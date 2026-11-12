# Creates random local secrets in deploy/secrets/ (git-ignored). Existing files are kept.
# Production secrets are not made here: they come from the operations repository (design 6.5).
$ErrorActionPreference = 'Stop'
$dir = Join-Path $PSScriptRoot 'secrets'
New-Item -ItemType Directory -Force $dir | Out-Null

function New-Secret([string] $name, [int] $bytes) {
    $path = Join-Path $dir $name
    if (-not (Test-Path $path) -or (Get-Item $path).Length -eq 0) {
        $value = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes($bytes))
        [IO.File]::WriteAllText($path, $value)
        Write-Output "created $name"
    }
}

foreach ($name in 'postgres_password', 'db_migrator_password', 'db_app_password', 'db_worker_password', 'db_readonly_password', 'ip_hash_key') {
    New-Secret $name 24
}
New-Secret 'data_protection_key' 32
$smtp = Join-Path $dir 'smtp_password'
if (-not (Test-Path $smtp)) { [IO.File]::WriteAllText($smtp, '') }
Write-Output "secrets in $dir"
