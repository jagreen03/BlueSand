# Tell Git to use your repo-local hooks folder
git config core.hooksPath .githooks

# Make the hook executable (works on Windows too)
git update-index --chmod=+x .githooks/pre-commit

# Add and commit
git add .githooks/pre-commit scripts/BlueSand-QuickCheck.ps1 .gitattributes
git commit -m "chore(hooks): add fast BlueSand pre-commit quick check"
