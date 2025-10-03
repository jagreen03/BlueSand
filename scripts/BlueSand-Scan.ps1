# BlueSand-Scan.ps1
# Reads config/bluesand.yaml, scans files, builds a consolidated word map (MD + CSV + XLSX).

param(
  [string]$ConfigPath = "config\\bluesand.yaml",
  [string]$OutDir = "docs"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- tiny YAML loader for simple key: value & lists (no external deps) ---
function Load-Yaml {
  param([string]$Path)
  $obj = @{}
  $currentKey = $null
  foreach ($line in Get-Content -Raw -Path $Path -Encoding UTF8 -ErrorAction Stop -Delimiter "`n") {
    $t = $line.Trim()
    if ($t -match '^\s*$' -or $t -match '^\s*#') { continue }
    if ($t -match '^\s*([A-Za-z0-9_]+):\s*(.*)$') {
      $currentKey = $matches[1]
      $rest = $matches[2].Trim()
      if ($rest -match '^\[(.*)\]$') {
        $arr = $matches[1].Split(',') | ForEach-Object { $_.Trim().Trim('"').Trim("'") } | Where-Object { $_ -ne "" }
        $obj[$currentKey] = @($arr)
      } elseif ($rest -ne "") {
        $obj[$currentKey] = $rest
      } else {
        $obj[$currentKey] = @()
      }
    } elseif ($t -match '^\s*-\s*(.*)$' -and $null -ne $currentKey) {
      if ($obj[$currentKey] -isnot [System.Collections.IList]) { $obj[$currentKey] = @() }
      $obj[$currentKey] += $matches[1].Trim().Trim('"').Trim("'")
    }
  }
  return $obj
}

function Normalize-Path([string]$p) {
  return ($ExecutionContext.ExpandString($p))
}

# --- load config ---
$cfg = Load-Yaml -Path $ConfigPath
$includePaths = @($cfg.include_paths | ForEach-Object { Normalize-Path $_ })
$excludeRe = $cfg.exclude_dir_regex
$extensions = @($cfg.extensions)
$terms = @($cfg.anchor_terms)
$plannedRe = $cfg.planned_hints_regex
$codeRe = $cfg.code_hints_regex
$crestT = [double]$cfg.crest_threshold
$slopesT = [double]$cfg.slopes_threshold

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# --- gather files ---
$allFiles = @()
foreach ($root in $includePaths) {
  if (-not (Test-Path $root)) { continue }
  $allFiles += Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
      $ext = "*$($_.Extension.ToLower())"
      ($extensions -contains $ext) -and
      ($_.FullName -notmatch $excludeRe)
    }
}

# --- scan & collect hits ---
$rows = @()
foreach ($f in $allFiles) {
  $text = ""
  try { $text = Get-Content -Raw -Path $f.FullName -Encoding UTF8 } catch { continue }
  foreach ($term in $terms) {
    $hits = ([regex]::Matches($text, [regex]::Escape($term), 'IgnoreCase')).Count
    if ($hits -gt 0) {
      $context = ($text | Select-String -Pattern $term -SimpleMatch -CaseSensitive:$false -List -ErrorAction SilentlyContinue | Select-Object -First 1).Line
      $context = ($context -replace '\s+', ' ').Trim()
      $isPlanned = ($text -match $plannedRe)
      $isCode = ($text -match $codeRe)
      $bucket = if ($isPlanned -and $isCode) { "Overlap" } elseif ($isPlanned) { "Planned" } elseif ($isCode) { "Code" } else { "Unknown" }
      $rows += [pscustomobject]@{
        Term = $term
        Repo = ($f.FullName -split '[\\/]' | Select-Object -Index 2)  # heuristic
        File = $f.FullName
        Ext  = $f.Extension.ToLower()
        Bucket = $bucket
        Frequency = $hits
        Context = $context
      }
    }
  }
}

if ($rows.Count -eq 0) {
  Write-Host "No anchor term matches found."
  exit 0
}

# --- rank by distinctiveness: simple TF scaling per term ---
$termGroups = $rows | Group-Object Term
$ranked = foreach ($g in $termGroups) {
  $total = ($g.Group | Measure-Object Frequency -Sum).Sum
  [pscustomobject]@{ Term = $g.Name; Total = $total }
} | Sort-Object -Property Total -Descending

$max = [double]($ranked | Select-Object -First 1).Total
$withTier = $ranked | ForEach-Object {
  $score = if ($max -gt 0) { $_.Total / $max } else { 0 }
  $tier = if ($score -ge $crestT) { "Crest" } elseif ($score -ge $slopesT) { "Slopes" } else { "Base" }
  [pscustomobject]@{ Term = $_.Term; Total = $_.Total; Score = [math]::Round($score,3); Tier = $tier }
}

# --- write markdown table ---
$md = @()
$md += "# BlueSand Word Map"
$md += ""
$md += "| Term | Tier | Total | Top Example |"
$md += "|---|---:|---:|---|"
foreach ($r in $withTier) {
  $ex = (($rows | Where-Object Term -eq $r.Term | Sort-Object Frequency -Descending | Select-Object -First 1).Context)
  $ex = ($ex -replace '\|','\|')
  $md += "| $($r.Term) | $($r.Tier) | $($r.Total) | $ex |"
}
$md | Out-File -FilePath (Join-Path $OutDir "WORDMAP_TABLE.md") -Encoding UTF8

# --- also dump CSV for Excel import ---
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $OutDir "WORDMAP_RAW.csv")

Write-Host "Wrote docs/WORDMAP_TABLE.md and docs/WORDMAP_RAW.csv"
