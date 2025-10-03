# BlueSand

**What it does:** Finds *unicorn* terms across code & docs, classifies them as **Planned / Code / Overlap**, and outputs ranked word maps:
- **Crest** (most distinctive), **Slopes** (solid), **Base** (background).

**Why:** Turn sprawling repos + notes into a teachable map (naming, docs, onboarding).

## Quick start
1. Configure `config/bluesand.yaml` (paths, terms, excludes).
2. Run `scripts/BlueSand-Scan.ps1` to generate:
   - `docs/WORDMAP_TABLE.md`
   - `docs/WORDMAP_TABLE.xlsx`
3. (Optional) Add references to `docs/REFERENCES.md`.

## Vocabulary
- **Unicorn term**: rare/novel or domain-significant word/phrase (e.g., *BlueSand*, *Irisification*).
- **Crest / Slopes / Base**: rank tiers by distinctiveness.
- **Planned / Code / Overlap**: where the term lives (plans-only, code-only, both).

## Status
MVP scripts + config. Expect iteration.
