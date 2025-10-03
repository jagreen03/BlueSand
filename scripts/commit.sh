#!/usr/bin/env bash
# Usage: scripts/commit.sh -m "feat: message" -all -push -runscan
pwsh -NoProfile -File "$(dirname "$0")/commit.ps1" -Message "$@"
