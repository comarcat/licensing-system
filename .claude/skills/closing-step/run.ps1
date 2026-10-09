#!/usr/bin/env pwsh
# .claude/skills/closing-step/run.ps1
# Usage: /closing-step "<commit-message>"

param(
    [Parameter(Mandatory=$true)]
    [string]$CommitMessage
)

Write-Host "--- 1. Updating Documents (PMI and LOGs) ---"
# Placeholder: user to run manual documentation updates or define steps here if automated
Write-Host "Ensure PMI, Error Log, and Change Log are updated."

Write-Host "--- 2. Updating Skill runs (Graphify and Archify) ---"
# Placeholder: run graphify and archify
# graphify .
# archify .

Write-Host "--- 3. Pushing Changes to GitHub ---"
git add .
git commit -m "$CommitMessage`n`nCo-Authored-By: Claude Code <noreply@anthropic.com>"
git push

Write-Host "--- 4. Compacting Session ---"
# Command to trigger compaction if available, or just clear context
/clear
Write-Host "Session compacted."
