Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "pre-commit: quick checks..." -ForegroundColor Cyan

# 1) Block copied/temp files
$staged = git diff --cached --name-only --diff-filter=AM
$bad = $staged | Where-Object { $_ -match '(?i)(^|[\\/]).*([ -]Copy|\~|\.tmp|\.bak)($|[\\/])' }
if ($bad) {
    Write-Host "✖ Refusing to commit copied/temp files:" -ForegroundColor Red
    $bad | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

# 2) Block large files > 5MB (allow the CSV)
$tooBig = @()
foreach ($path in $staged) {
    if (-not (Test-Path $path)) { continue }
    if ($path -ieq 'docs/WORDMAP_RAW.csv') { continue }
    if ((Get-Item $path).Length -gt 5MB) { $tooBig += $path }
}
if ($tooBig.Count) {
    Write-Host "✖ These staged files are >5MB:" -ForegroundColor Red
    $tooBig | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

# 3) Quick config presence + minimal sanity
$config = 'config/bluesand.yaml'
if (-not (Test-Path $config)) {
    Write-Host "✖ Missing $config" -ForegroundColor Red
    exit 1
}
$raw = Get-Content -Raw $config -Encoding UTF8
if ($raw -notmatch '(?im)^\s*include_paths\s*:' -or $raw -notmatch '(?im)^\s*anchor_terms\s*:') {
    Write-Host "✖ $config must define include_paths and anchor_terms" -ForegroundColor Red
    exit 1
}

# Optional near-instant smoke
if (Test-Path ".\Quick-sanity-test.ps1") {
    pwsh -NoProfile -ExecutionPolicy Bypass -File .\Quick-sanity-test.ps1
}

Write-Host "✔ pre-commit checks passed." -ForegroundColor Green
exit 0
