#!/usr/bin/env pwsh
# memory-update-reminder.ps1
# Stop hook (plan §13): when a work session ends with pending changes under src/, tests/, or web/,
# remind (once) to run /update-memory so CLAUDE.md + docs/PLAN.md stay current for the next session.
# Blocks the stop ONCE (loop-guarded by stop_hook_active); the next stop is allowed.

$ErrorActionPreference = 'SilentlyContinue'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }

# Already continued once because of this hook -> allow the stop, never loop.
if ($payload.stop_hook_active -eq $true) { exit 0 }

# Any uncommitted/untracked changes under the code directories this session?
$changed = (& git status --porcelain -- src tests web 2>$null)
if ([string]::IsNullOrWhiteSpace($changed)) { exit 0 }

$reason = "Memory hygiene: code under src/, tests/, or web/ changed this session but project memory " +
          "may be stale. Before finishing, run /update-memory to update CLAUDE.md (## Status + any " +
          "changed convention/command/config) and docs/PLAN.md so the next session resumes accurately. " +
          "If memory is already current, you may stop."

@{ decision = 'block'; reason = $reason } | ConvertTo-Json -Compress
exit 0
