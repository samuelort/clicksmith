#!/bin/bash
# Run after import-1.2.4-source.sh. Creates a git repo and pushes it to GitHub.
set -euo pipefail
cd "$(dirname "$0")"
command -v git >/dev/null || { echo "git is not installed"; exit 1; }
[ -f Sources/ClicksmithCore/IPC.swift ] || { echo "First run: ./import-1.2.4-source.sh ~/Downloads/clicksmith"; exit 1; }
grep -q 'version = "1.2.4"' Sources/ClicksmithCore/IPC.swift || { echo "Source is not 1.2.4"; exit 1; }
if [ ! -d .git ]; then git init -b main; fi
git add .
git commit -m "Clicksmith 1.2.4 beta" || true
cat <<'TXT'

Local repository is ready.

On github.com:
  1. Click + > New repository.
  2. Name it: clicksmith
  3. Choose Public or Private.
  4. DO NOT add a README, .gitignore or license.
  5. Click Create repository.
  6. Copy the repository HTTPS URL, then run these two commands:

     git remote add origin YOUR_GITHUB_REPOSITORY_URL
     git push -u origin main

Example URL format:
  https://github.com/YOUR_USERNAME/clicksmith.git
TXT
