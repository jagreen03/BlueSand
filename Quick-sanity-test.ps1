Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path 'config/bluesand.yaml')) { throw "config/bluesand.yaml missing" }

# Ensure exclude_dir_regex compiles if present
$cfg = Get-Content -Raw 'config/bluesand.yaml' -Encoding UTF8
$line = ($cfg -split "`r?`n" | Where-Object { $_ -match '^\s*exclude_dir_regex\s*:' }) | Select-Object -First 1
if ($line) {
  $pattern = ($line -split ':',2)[1].Trim().Trim('"').Trim("'")
  if ($pattern) { $null = [regex]::new($pattern,[System.Text.RegularExpressions.RegexOptions]::IgnoreCase) }
}

Write-Host "Quick sanity OK."
