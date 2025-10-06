# usage:
#   pwsh -NoProfile -File scripts/commit.ps1 -Message "chore(BlueSand): update scan" -All -Push
#   pwsh -NoProfile -File scripts/commit.ps1 -Message "feat: add X" -Files config/bluesand.yaml,scripts/BlueSand-Scan.ps1 -RunScan

param(
  [Parameter(Mandatory)][string]$Message,
  [string[]]$Files,
  [switch]$All,
  [switch]$Push,
  [switch]$RunScan
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Optional: enforce Conventional Commits lightly
if ($Message -notmatch '^(feat|fix|chore|docs|refactor|perf|test|build|ci)(\(.+\))?: .+') {
  Write-Warning "Commit message doesn't look like Conventional Commits. Proceeding anyway."
}

# Verify git repo
$inside = (git rev-parse --is-inside-work-tree 2>$null).Trim()
if ($inside -ne 'true') { throw "Not inside a git repository." }

# Optionally run a scan and stage generated docs
if ($RunScan) {
  Write-Host "Running BlueSand scan..." -ForegroundColor Cyan
  pwsh -NoProfile -File scripts/BlueSand-Scan.ps1 -ConfigPath config/bluesand.yaml -OutDir docs
  git add docs/WORDMAP_TABLE.md docs/WORDMAP_RAW.csv docs/SUMMARY.md 2>$null
}

# Stage changes
if ($All) {
  git add -A
} elseif ($Files) {
  foreach ($f in $Files) { git add $f }
} else {
  Write-Host "Nothing staged. Use -All or -Files." -ForegroundColor Yellow
  git status -s
  exit 1
}

# Bail if nothing to commit
if (-not (git diff --cached --quiet; $LASTEXITCODE -eq 1)) {
  Write-Host "No staged changes. Nothing to commit." -ForegroundColor Yellow
  exit 0
}

# Commit & optional push
git commit -m $Message
if ($Push) { git push }
