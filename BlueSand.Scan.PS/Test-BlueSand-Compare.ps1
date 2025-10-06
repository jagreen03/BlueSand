param(
  [string]$ConfigPath = ".\config\bluesand.yaml",
  [string]$CliOutDir  = ".\out_cli",
  [string]$PsOutDir   = ".\out_ps",
  [string]$CliProject = ".\src\BlueSand.Cli",
  [switch]$Rebuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-CleanDir([string]$Path) {
  if (Test-Path $Path) { Remove-Item -Recurse -Force $Path }
  New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Normalize-Csv([string]$In, [string]$Out) {
  if (-not (Test-Path $In)) { throw "Missing CSV: $In" }
  $rows = Import-Csv -Path $In
  # stable sort matches column names from both tools
  $rows |
    Sort-Object Term,Repo,File,Ext,Bucket,Frequency,Context |
    Export-Csv -Path $Out -NoTypeInformation -Encoding UTF8
}

# 0) prep
Write-Host "== BlueSand C# vs PowerShell comparison ==" -ForegroundColor Cyan
Write-Host "Config: $ConfigPath"

if ($Rebuild) {
  Write-Host "Building C# CLI (Release)..." -ForegroundColor Yellow
  dotnet build $CliProject -c Release | Out-Null
}

New-CleanDir $CliOutDir
New-CleanDir $PsOutDir

# 1) run C# CLI
Write-Host "`n[1/3] C# run..." -ForegroundColor Cyan
$cliTime = Measure-Command {
  dotnet run --project $CliProject -c Release -- --config $ConfigPath --outdir $CliOutDir --no-progress
}

# 2) run PowerShell
Write-Host "`n[2/3] PowerShell run..." -ForegroundColor Cyan
$psTime = Measure-Command {

  Write-Host "1. ConfigPath: `"$ConfigPath`""
  Write-Host "2. OutDir `"$PsOutDir`""
  Write-Host "3. pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\BlueSand-Scan.ps1 -ConfigPath `"$ConfigPath`" -OutDir `"$PsOutDir`" | Out-Null"
  pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\BlueSand-Scan.ps1 -ConfigPath $ConfigPath -OutDir $PsOutDir | Out-Null
}

# 3) normalize and compare CSVs
Write-Host "`n[3/3] Normalizing + diff..." -ForegroundColor Cyan
$cliCsv = Join-Path $CliOutDir "WORDMAP_RAW.csv"
$psCsv  = Join-Path $PsOutDir  "WORDMAP_RAW.csv"
$cliCsvNorm = Join-Path $CliOutDir "WORDMAP_RAW.normalized.csv"
$psCsvNorm  = Join-Path $PsOutDir  "WORDMAP_RAW.normalized.csv"

Normalize-Csv $cliCsv $cliCsvNorm
Normalize-Csv $psCsv  $psCsvNorm

# simple counts
$cliCount = (Import-Csv $cliCsvNorm).Count
$psCount  = (Import-Csv $psCsvNorm).Count

# file hash for quick equality check
$cliHash = (Get-FileHash $cliCsvNorm -Algorithm SHA256).Hash
$psHash  = (Get-FileHash $psCsvNorm  -Algorithm SHA256).Hash

Write-Host "`n== Timing ==" -ForegroundColor Yellow
"{0,-10} {1}" -f "C#:", $cliTime.ToString()
"{0,-10} {1}" -f "PowerShell:", $psTime.ToString()

Write-Host "`n== Row counts ==" -ForegroundColor Yellow
"{0,-10} {1}" -f "C#:", $cliCount
"{0,-10} {1}" -f "PowerShell:", $psCount

if ($cliHash -eq $psHash) {
  Write-Host "`nRESULT: ✅ CSVs match exactly (normalized)." -ForegroundColor Green
} else {
  Write-Host "`nRESULT: ⚠️ CSVs differ. Showing small diff sample..." -ForegroundColor DarkYellow

  # Compare as strings for quick signal
  $a = Get-Content $cliCsvNorm
  $b = Get-Content $psCsvNorm
  $diff = Compare-Object -ReferenceObject $a -DifferenceObject $b -IncludeEqual:$false -SyncWindow 3

  # print up to 30 differences with side markers
  $printed = 0
  foreach ($d in $diff) {
    if ($printed -ge 30) { break }
    $side = if ($d.SideIndicator -eq "<=") { "C#" } else { "PS" }
    Write-Host ("[{0}] {1}" -f $side, $d.InputObject)
    $printed++
  }

  Write-Host "`nHints:" -ForegroundColor Yellow
  Write-Host "- Differences often come from: path normalization, repo heuristics, regex options, or file-size/skips."
  Write-Host "- Check: ExcludeDir/ExcludeFile regex, MaxFileMb, and extension normalization in both implementations."
}

Write-Host "`nArtifacts:"
Write-Host "  C#: $cliCsvNorm"
Write-Host "  PS: $psCsvNorm"
