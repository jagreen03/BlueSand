# (A) sanity check config
Get-Content .\config\bluesand.yaml | more

# (B) run the scanner in "dry run" mode (if you added that flag), or just run it:
.\scripts\BlueSand-Scan.ps1 `
  -Roots @(
    "C:\gitx","C:\test","C:\GITY",
    "$env:USERPROFILE\Desktop","$env:USERPROFILE\Documents","$env:USERPROFILE\Downloads",
    "$env:USERPROFILE\OneDrive\Desktop\personal",
    "C:\TEST\Superthinking-Blueprints\SuperThinking-Blueprints"
  ) `
  -Ext @("*.md","*.html","*.cs","*.py","*.js","*.ts","*.sql","*.txt","*.ps1","*.bat") `
  -ExcludeDir @(".git",".obsidian",".vscode",".vs","node_modules","plugins","venv",".pytest_cache",".ruff_cache") `
  -Terms @("BlueSand","Essence64","Factor3Vec","hotFactor","coldFactor","constFactor","AI-Jain","AI-Joe","Iris","Irisification","RainbowFactor") `
  -OutTable ".\docs\WORDMAP_TABLE.md" `
  -OutCsv   ".\docs\WORDMAP_TABLE.csv"