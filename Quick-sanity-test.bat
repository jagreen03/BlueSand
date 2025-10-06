# Should PASS
pwsh -NoProfile -File scripts/BlueSand-QuickCheck.ps1

# Intentionally break the YAML (e.g., unbalanced quote) and stage it, then:
git commit -m "test: should fail pre-commit"
# You should see a precise YAML error with a line number; commit is blocked.
