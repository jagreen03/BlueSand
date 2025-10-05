# Fast lint for BlueSand: validates config/bluesand.yaml; no slow scans here.

param([string]$ConfigPath = "config\\bluesand.yaml")

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Load-Yaml {
  param([string]$Path)
  $yamlObject  = @{}
  $currentKey  = $null
  $lineNumber  = 0
  foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
    $lineNumber++
    $t = $line.Trim()
    if ($t -match '^\s*$' -or $t -match '^\s*#') { continue }
    if ($t -match '^\s*([A-Za-z0-9_]+):\s*(.*)$') {
      $currentKey = $matches[1]
      $rest = $matches[2].Trim()
      if ($rest -match '^\[(.*)\]$') {
        $items = $matches[1].Split(',') | ForEach-Object { $_.Trim().Trim('"').Trim("'") } | Where-Object { $_ -ne "" }
        $yamlObject[$currentKey] = @($items)
      } elseif ($rest -ne "") {
        if (($rest.StartsWith('"') -and -not $rest.EndsWith('"')) -or ($rest.StartsWith("'") -and -not $rest.EndsWith("'"))) {
          throw "YAML parse error at line $($lineNumber): unmatched quote near: $rest"
        }
        $yamlObject[$currentKey] = $rest.Trim('"').Trim("'")
      } else {
        $yamlObject[$currentKey] = @()
      }
    } elseif ($t -match '^\s*-\s*(.*)$' -and $null -ne $currentKey) {
      $val = $matches[1].Trim()
      if (($val.StartsWith('"') -and -not $val.EndsWith('"')) -or ($val.StartsWith("'") -and -not $val.EndsWith("'"))) {
        throw "YAML parse error at line $($lineNumber): unmatched quote near: $val"
      }
      if ($yamlObject[$currentKey] -isnot [System.Collections.IList]) { $yamlObject[$currentKey] = @() }
      $yamlObject[$currentKey] += $val.Trim('"').Trim("'")
    } else {
      throw "YAML parse error at line $($lineNumber): unexpected line: $t"
    }
  }
  return $yamlObject
}

function Require-NonEmpty([string]$name, $value) {
  if ($null -eq $value) { throw "Missing required config: $name" }
  if ($value -is [string] -and [string]::IsNullOrWhiteSpace($value)) { throw "Empty config value: $name" }
  if ($value -is [System.Collections.IEnumerable] -and -not ($value | ForEach-Object { $_ } | Measure-Object).Count) {
    throw "Empty list: $name"
  }
}

function Parse-Double([string]$text, [string]$name) {
  $regex = '-?(\d+(\.\d+)?)'
  if ($text -notmatch $regex) { throw "Config '$name' must be a number (got: '$text')" }
  return [double]::Parse($matches[1], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture)
}

Write-Host "[BlueSand] Validating $ConfigPath…" -ForegroundColor Cyan
if (-not (Test-Path $ConfigPath)) { throw "Config file not found: $ConfigPath" }

$cfg = Load-Yaml -Path $ConfigPath

Require-NonEmpty 'include_paths'       $cfg.include_paths
Require-NonEmpty 'extensions'          $cfg.extensions
Require-NonEmpty 'anchor_terms'        $cfg.anchor_terms
Require-NonEmpty 'exclude_dir_regex'   $cfg.exclude_dir_regex
Require-NonEmpty 'planned_hints_regex' $cfg.planned_hints_regex
Require-NonEmpty 'code_hints_regex'    $cfg.code_hints_regex

# Regex sanity
try { [void][regex]::new($cfg.exclude_dir_regex, [Text.RegularExpressions.RegexOptions]::IgnoreCase) }
catch { throw "exclude_dir_regex is not a valid regex: $($_.Exception.Message)" }

try { [void][regex]::new($cfg.planned_hints_regex, [Text.RegularExpressions.RegexOptions]::IgnoreCase) }
catch { throw "planned_hints_regex is not a valid regex: $($_.Exception.Message)" }

try { [void][regex]::new($cfg.code_hints_regex, [Text.RegularExpressions.RegexOptions]::IgnoreCase) }
catch { throw "code_hints_regex is not a valid regex: $($_.Exception.Message)" }

# Threshold numbers
[void](Parse-Double $cfg.crest_threshold  'crest_threshold')
[void](Parse-Double $cfg.slopes_threshold 'slopes_threshold')

# Include paths exist (warn but don’t fail)
$missing = @()
foreach ($p in @($cfg.include_paths)) {
  $expanded = $ExecutionContext.ExpandString($p)
  if (-not (Test-Path $expanded)) { $missing += $expanded }
}
if ($missing.Count -gt 0) {
  Write-Warning "Some include_paths do not exist:`n  - $(($missing -join "`n  - "))"
}

Write-Host "[BlueSand] Quick check passed." -ForegroundColor Green
