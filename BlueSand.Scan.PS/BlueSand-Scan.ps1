# BlueSand-Scan.ps1
# Reads config/bluesand.yaml, scans files, builds a consolidated word map (MD + CSV).

param(
  [string]$ConfigPath = "config\\bluesand.yaml",
  [string]$OutDir = "docs"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- tiny YAML loader for simple key: value & lists (no external deps) ---
function Load-Yaml {
  param([string]$Path)
  $yamlObject  = @{}
  $currentKey  = $null
  $lineNumber  = 0

  foreach ($line in Get-Content -Path $Path -Encoding UTF8)
  {
    $lineNumber++
    $trimmed = $line.Trim()
    if ($trimmed -match '^\s*$' -or $trimmed -match '^\s*#')
    {
        continue
    }

    if ($trimmed -match '^\s*([A-Za-z0-9_]+):\s*(.*)$')
    {
      $currentKey = $matches[1]
      $rest = $matches[2].Trim()
      $rest = Strip-YamlInlineComment $rest
      if ($rest -match '^\[(.*)\]$') {
        $items = $matches[1].Split(',') |
                 ForEach-Object { $_.Trim().Trim('"').Trim("'") } |
                 Where-Object { $_ -ne "" }
        $yamlObject[$currentKey] = @($items)
      } elseif ($rest -ne "") {
        if (($rest.StartsWith('"') -and -not $rest.EndsWith('"')) -or
            ($rest.StartsWith("'") -and -not $rest.EndsWith("'"))) {
          throw "YAML parse error at line $($lineNumber): unmatched quote near: $rest"
        }
        $yamlObject[$currentKey] = $rest.Trim('"').Trim("'")
      } else {
        $yamlObject[$currentKey] = @()   # expect list items to follow
      }

    }
    elseif ($trimmed -match '^\s*-\s*(.*)$' -and $null -ne $currentKey)
    {
      $value = $matches[1].Trim()
      $value = Strip-YamlInlineComment $value
      if (($value.StartsWith('"') -and -not $value.EndsWith('"')) -or
          ($value.StartsWith("'") -and -not $value.EndsWith("'"))) {
        throw "YAML parse error at line $($lineNumber): unmatched quote near: $value"
      }
      if ($yamlObject[$currentKey] -isnot [System.Collections.IList]) { $yamlObject[$currentKey] = @() }
      $yamlObject[$currentKey] += $value.Trim('"').Trim("'")
    } else {
      throw "YAML parse error at line $($lineNumber): unexpected line: $trimmed"
    }
  }
  return $yamlObject
}

# --- helpers ---------------------------------------------------------------
function Strip-YamlInlineComment {
  param([string]$s)
  if ([string]::IsNullOrWhiteSpace($s)) { return $s }

  $inS = $false; $inD = $false
  for ($i = 0; $i -lt $s.Length; $i++) {
    $ch = $s[$i]
    if ($ch -eq '"'  -and -not $inS) { $inD = -not $inD; continue }
    if ($ch -eq "'"  -and -not $inD) { $inS = -not $inS; continue }
    if ($ch -eq '#'  -and -not $inS -and -not $inD) {
      return $s.Substring(0, $i).Trim()
    }
  }
  return $s.Trim()
}


<# --- Expand %USERPROFILE%-style vars too.
 # $ExecutionContext.ExpandString() won’t expand %USERPROFILE%. Add
 # Environment.ExpandEnvironmentVariables so both $env:… and %…% work.
 #>
function Normalize-Path([string]$Template) {
  $expanded = [Environment]::ExpandEnvironmentVariables($Template)
  try   { return $ExecutionContext.ExpandString($expanded) }
  catch { return $expanded }
}


# --- robust "double from text" ---
function Get-DoubleFromText {
  [CmdletBinding()]
  param([Parameter(Mandatory)][string]$Text)

  $regex = '-?(\d+(\.\d+)?)'
  if ($Text -match $regex) {
    $numericString = $matches[1]
    try {
      return [double]::Parse(
        $numericString,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture
      )
    } catch { return $null }
  }
  return $null
}

# --- fallback string expansion for very old hosts ---
function Expand-StringAlternative {
  [CmdletBinding()]
  param([Parameter(Mandatory,ValueFromPipeline)][string]$InputString)

  process {
    $template  = [string]"@"
    $template += "`"`n$InputString`n"
    $template += "`"@"
    return Invoke-Command -ScriptBlock ([scriptblock]::Create($template)) -NoNewScope
  }
}

# --- this will expand string with new or catch if old and use local method for 
function Normalize-Path([string]$Template) {
  try { return ($ExecutionContext.ExpandString($Template)) }
  catch { return Expand-StringAlternative -InputString $Template }
}

# --- Improve the “Repo” column (safer fallback) ---
function Get-RepoName {
  param([string]$FullPath, [string[]]$Roots)

  foreach ($root in $Roots) {
    if ($FullPath.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
      $relative = $FullPath.Substring($root.Length).TrimStart('\','/')
      if ($relative) { return ($relative -split '[\\/]')[0] }
    }
  }
  # fallback if no root matched (C:\X\Y\Z -> Y)
  $parts = $FullPath -split '[\\/]'
  if ($parts.Count -ge 3) { return $parts[2] }
  return $parts[-1]
}

# --- helper: extract a single-line context around a match index ---
function Get-LineContext {
  param([string]$Text, [int]$Index, [int]$MaxLen = 240)

  if ($null -eq $Text -or $Index -lt 0) { return "" }
  $start = $Text.LastIndexOf("`n", [Math]::Min($Index, $Text.Length - 1))
  $end   = $Text.IndexOf("`n", $Index)
  if ($start -lt 0) { $start = 0 } else { $start++ }
  if ($end   -lt 0) { $end = [Math]::Min($Text.Length, $start + $MaxLen) }
  $slice = $Text.Substring($start, [Math]::Min($end - $start, $MaxLen))
  return ($slice -replace '\s+', ' ').Trim()
}

function Get-ConfigInteger {
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true)]
        [string]$ConfigValue,

        [int]$DefaultValue = 0
    )

    $integer = $DefaultValue

    # Remove any comments and leading/trailing whitespace
    $cleanValue = ($ConfigValue -split '#')[0].Trim()

    if ([string]::IsNullOrWhiteSpace($cleanValue)) {
        return $integer
    }

    # Attempt to parse the cleaned string as an integer
    $success = [int32]::TryParse($cleanValue, [ref]$integer)

    if ($success) {
        return $integer
    }
    else {
        # If parsing fails, return the default value and optionally provide a warning
        Write-Warning "Could not parse '$cleanValue' as an integer. Returning default value '$DefaultValue'."
        return $DefaultValue
    }
}

# --- 1 of 3 helper for .bluesandignore
function Convert-GlobToRegex([string]$glob) {
  $g = $glob -replace '\\','/'          # normalize
  $g = [regex]::Escape($g)
  $g = $g -replace '(?<!\\)\*\\\*','.*' # ** → .*
  $g = $g -replace '(?<!\.)\\\*','[^/]*'# *  → [^/]*
  $g = $g -replace '\\\?','[^/]'        # ?  → [^/]
  return '^' + $g + '$'
}

# --- 2 of 3 helper for .bluesandignore
function New-PathSpecSet([string[]]$lines) {
  $rules = @()
  foreach ($raw in $lines) {
    $line = $raw.Trim()
    if ($line -eq '' -or $line.StartsWith('#')) { continue }
    $neg = $line.StartsWith('!')
    $pat = if ($neg) { $line.Substring(1).Trim() } else { $line }
    $rx  = [regex]::new((Convert-GlobToRegex $pat), 'IgnoreCase')
    $rules += [pscustomobject]@{ Rx = $rx; Neg = $neg }
  }
  return ,$rules
}

# --- 3 of 3 helper for .bluesandignore
function Test-PathSpecExcluded([object[]]$rules, [string]$fullPath) {
  $p = $fullPath.Replace('\','/')
  $state = $null
  foreach ($r in $rules) {
    if ($r.Rx.IsMatch($p)) { $state = -not $r.Neg }
  }
  return ($state -eq $true)
}



<# 7) Timing (see where it’s “slow at the start/end”)
 # Add a stopwatch so SUMMARY shows elapsed totals:
 #>
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# --- load config ---
$cfg = Load-Yaml -Path $ConfigPath
if (-not $cfg -or -not $cfg.include_paths) {
  throw "Config parse failed or 'include_paths' missing. Check config\bluesand.yaml."
}

# compile directory regex (or null)
$excludeDirRegex = if ([string]::IsNullOrWhiteSpace($cfg.exclude_dir_regex)) {
  $null
} else {
  [regex]::new($cfg.exclude_dir_regex,
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

# compile file regex (or null)  <-- this was the missing piece
$excludeFileRegex = if ([string]::IsNullOrWhiteSpace($cfg.exclude_file_regex)) {
  $null
} else {
  [regex]::new($cfg.exclude_file_regex,
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

if ($cfg.include_paths.Count -eq 0) {
  throw "'include_paths' is empty. Check quoting/indentation in config\bluesand.yaml."
}

$includePaths = @($cfg.include_paths | ForEach-Object { Normalize-Path $_ })

$extensions = @($cfg.extensions)
$anchorTerms = @($cfg.anchor_terms)
$plannedRegex = [regex]::new($cfg.planned_hints_regex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$codeRegex    = [regex]::new($cfg.code_hints_regex,    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$crestThreshold  = Get-DoubleFromText -Text $cfg.crest_threshold
$slopesThreshold = Get-DoubleFromText -Text $cfg.slopes_threshold
$excludeDirRegex = [regex]::new($cfg.exclude_dir_regex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$maxFileMB     = Get-ConfigInteger($cfg.max_file_mb);
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# --- compile anchor regexes once ---
$regexOptions = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
$compiledTermRegex = @{}
foreach ($termText in $anchorTerms) {
  if ([string]::IsNullOrWhiteSpace($termText)) { continue }
  $compiledTermRegex[$termText] = [regex]::new([regex]::Escape($termText), $regexOptions)
}

# Build once:
$patternLines = @()
$patternLines += $cfg.exclusion_patterns
foreach ($theFile2 in $cfg.exclusion_files)
{
    if (Test-Path -LiteralPath $theFile2) {
        $patternLines += Get-Content -LiteralPath $theFile2 -Encoding UTF8 |
                        Where-Object { $_ -and -not $_.StartsWith('#') }
                        Select-Object { $_.Trim() }
    }
}

$psRules = New-PathSpecSet $patternLines
# before the loop
$resolvedOutDir = ((Resolve-Path $OutDir).Path.TrimEnd('\','/')) + [IO.Path]::DirectorySeparatorChar
# (optional) also skip C# output if present
$resolvedOutCli = ((Resolve-Path ".\out_cli").Path.TrimEnd('\','/')) + [IO.Path]::DirectorySeparatorChar

if ($resolvedOutCli) { $resolvedOutCli = $resolvedOutCli.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar }

# --- gather files ---
$allFiles = @()
$resolvedOutDir = (Resolve-Path $OutDir).Path # ADDED LINE 204
foreach ($rootPath in $includePaths) {
  if (-not (Test-Path $rootPath)) { continue }

  $root = $rootPath;
  $allFiles += Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object {
    $full = $_.FullName;

    # skip generated outputs
    $startsWithOutDir = $full.ToLower().StartsWith($resolvedOutDir.ToLower());
    $startsWithOutCli = $full.ToLower().StartsWith($resolvedOutCli.ToLower());
    if ($startsWithOutDir -eq $true -or $startsWithOutCli -eq $true) { continue }

    # pathspec (from .bluesandignore + patterns)
    if ($psRules -and (Test-PathSpecExcluded -rules $psRules -fullPath $full)) { continue }

    # regex & extension filters
    $extOK  = ($extensions -contains "*$($_.Extension.ToLower())")
    $dirOK  = ($excludeDirRegex  -eq $null) -or (-not $excludeDirRegex.IsMatch($full))
    $fileOK = ($excludeFileRegex -eq $null) -or (-not $excludeFileRegex.IsMatch($full))
    $extOK -and $dirOK -and $fileOK
  }
}

<#
$resolvedOutDir = (Resolve-Path $OutDir).Path
$allFiles += Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object {
    $full = $_.FullName
    -not ($full.StartsWith($resolvedOutDir, [System.StringComparison]::OrdinalIgnoreCase)) -and
    ($extensions -contains ("*" + $_.Extension.ToLower())) -and
    ($full -notmatch $excludeRe) -and
    ([string]::IsNullOrWhiteSpace($excludeFileRe) -or ($full -notmatch $excludeFileRe))
  }
#>

<# 5) Optional: guard the “Top 10 largest files” sort
 # That sort is O(n log n) and can be a time sink on huge trees. Make it conditional:
 #>
if ($allFiles.Count -le 50000) {
  $largest = $allFiles | Sort-Object Length -Descending | Select-Object -First 10 FullName, Length
  Write-Host "`nTop 10 largest files (pre-filter):"
  $largest | Format-Table -AutoSize | Out-String | Write-Host
}

$includePaths = $includePaths | Sort-Object -Unique
# after building $allFiles:
$allFiles = $allFiles | Sort-Object FullName -Unique

# --- scan & collect hits (typed list is faster than += arrays) ---
$rows = New-Object System.Collections.Generic.List[object]
$fileIndex = 0
$totalFiles = $allFiles.Count

<# (Optional) Log the biggest slowpokes
 # If you want to see what would be skipped/kept, add this before scanning:
 #>
$largest = $allFiles | Sort-Object Length -Descending | Select-Object -First 10 FullName, Length
Write-Host "`nTop 10 largest files (pre-filter):"
$largest | Format-Table -AutoSize | Out-String | Write-Host

foreach ($theFile in $allFiles) {
  $fileIndex++
  if (($fileIndex % 250) -eq 0) {
    Write-Progress -Activity "Scanning files" -Status "$fileIndex / $totalFiles" -PercentComplete ([int](100*$fileIndex/$totalFiles))
  }

  $fileText = ""

  # we want to Skip oversized files at scan time
  if ($maxFileMB -gt 0 -and $theFile.Length -gt ($maxFileMB * 1MB)) { continue }

  try { $fileText = Get-Content -Raw -Path $theFile.FullName -Encoding UTF8 } catch { continue }

  $isPlanned = $false;
  if($fileText -ne $null -and $plannedRegex.IsMatch($fileText))
  {
    $isPlanned = $true;
  }
  $isCode = $false;
  if($fileText -ne $null -and $codeRegex.IsMatch($fileText))
  {
    $isCode = $true;
  }
  $bucket    = if ($isPlanned -and $isCode) { "Overlap" } elseif ($isPlanned) { "Planned" } elseif ($isCode) { "Code" } else { "Unknown" }

  foreach ($termKey in $compiledTermRegex.Keys) {
    $termRe = $compiledTermRegex[$termKey]
    if($fileText -eq $null)
    {
        continue;
    }
    $matches = $termRe.Matches($fileText)
    if ($matches.Count -le 0) { continue }

    $firstIndex = $matches[0].Index
    $contextOneLine = Get-LineContext -Text $fileText -Index $firstIndex -MaxLen 240

    $rows.Add([pscustomobject]@{
      Term      = $termKey
      Repo      = (Get-RepoName -FullPath $theFile.FullName -Roots $includePaths)
      File      = $theFile.FullName
      Ext       = $theFile.Extension.ToLower()
      Bucket    = $bucket
      Frequency = $matches.Count
      Context   = $contextOneLine
    }) | Out-Null
  }
}

if ($rows.Count -eq 0) {
  Write-Host "No anchor term matches found."
  exit 0
}

# --- rank by distinctiveness: simple TF scaling per term ---
$termGroups = $rows | Group-Object Term
$ranked = @()
if ($termGroups) {
  $ranked = $termGroups | ForEach-Object {
    $totalForTerm = ($_.Group | Measure-Object Frequency -Sum).Sum
    [pscustomobject]@{ Term = $_.Name; Total = $totalForTerm }
  } | Sort-Object -Property Total -Descending
}

$maxTotal = [double]($ranked | Select-Object -First 1).Total
$withTier = $ranked | ForEach-Object {
  $theValueR = $_;
  $score = if ($maxTotal -gt 0) { $theValueR.Total / $maxTotal } else { 0 }
  $tier  = if ($score -ge $crestThreshold) { "Crest" } elseif ($score -ge $slopesThreshold) { "Slopes" } else { "Base" }
  [pscustomobject]@{ Term = $theValueR.Term; Total = $theValueR.Total; Score = [math]::Round($score,3); Tier = $tier }
}

# --- write markdown table ---
$mdLines = @()
$mdLines += "# BlueSand Word Map"
$mdLines += ""
$mdLines += "| Term | Tier | Total | Top Example |"
$mdLines += "|---|---:|---:|---|"
foreach ($termRow in $withTier) {
  $example = (($rows | Where-Object Term -eq $termRow.Term | Sort-Object Frequency -Descending | Select-Object -First 1).Context)
  $example = ($example -replace '\|','\|')
  $mdLines += "| $($termRow.Term) | $($termRow.Tier) | $($termRow.Total) | $example |"
}
$mdLines | Out-File -FilePath (Join-Path $OutDir "WORDMAP_TABLE.md") -Encoding UTF8

# --- also dump CSV for Excel import ---
$rows | Export-Csv -Path (Join-Path $OutDir "WORDMAP_RAW.csv") -NoTypeInformation -Encoding UTF8

# --- SUMMARY (buckets, tiers, top repos/terms) ---
$bucketSummary = $rows | Group-Object Bucket | Sort-Object Count -Descending |
  ForEach-Object { [pscustomobject]@{ Bucket = $_.Name; Items = $_.Count } }

$tierSummary = $withTier | Group-Object Tier | Sort-Object Count -Descending |
  ForEach-Object { [pscustomobject]@{ Tier = $_.Name; Terms = $_.Count } }

$topRepos = $rows | Group-Object Repo | Sort-Object Count -Descending |
  Select-Object -First 10 |
  ForEach-Object { [pscustomobject]@{ Repo = $_.Name; Items = $_.Count } }

$topTerms = $withTier | Sort-Object Total -Descending | Select-Object -First 15 Term,Total,Tier,Score

Write-Host ""
Write-Host "== BlueSand Summary =="
Write-Host "Buckets:" -ForegroundColor Cyan
$bucketSummary | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Tiers:" -ForegroundColor Cyan
$tierSummary | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Top Repos:" -ForegroundColor Cyan
$topRepos | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Top Terms:" -ForegroundColor Cyan
$topTerms | Format-Table -AutoSize | Out-String | Write-Host

# Write docs/SUMMARY.md
$summaryLines = @()
$summaryLines += "# BlueSand Summary"
$summaryLines += ""
$summaryLines += "## Bucket Distribution"
$summaryLines += ""
$summaryLines += "| Bucket | Items |"
$summaryLines += "|---|---:|"
foreach ($bucketRow in $bucketSummary) { $summaryLines += "| $($bucketRow.Bucket) | $($bucketRow.Items) |" }
$summaryLines += ""
$summaryLines += "## Tier Distribution (per term)"
$summaryLines += ""
$summaryLines += "| Tier | Terms |"
$summaryLines += "|---|---:|"
foreach ($tierRow in $tierSummary) { $summaryLines += "| $($tierRow.Tier) | $($tierRow.Terms) |" }
$summaryLines += ""
$summaryLines += "## Top Repos (by occurrences)"
$summaryLines += ""
$summaryLines += "| Repo | Items |"
$summaryLines += "|---|---:|"
foreach ($repoRow in $topRepos) { $summaryLines += "| $($repoRow.Repo) | $($repoRow.Items) |" }
$summaryLines += ""
$summaryLines += "## Top Terms"
$summaryLines += ""
$summaryLines += "| Term | Tier | Total | Score |"
$summaryLines += "|---|---|---:|---:|"
foreach ($termTop in $topTerms) { $summaryLines += "| $($termTop.Term) | $($termTop.Tier) | $($termTop.Total) | $([math]::Round($termTop.Score,3)) |" }

$summaryPath = Join-Path $OutDir "SUMMARY.md"
$summaryLines | Out-File -FilePath $summaryPath -Encoding UTF8

Write-Host "Wrote $(Join-Path $OutDir 'WORDMAP_TABLE.md'), $(Join-Path $OutDir 'WORDMAP_RAW.csv'), $summaryPath"
