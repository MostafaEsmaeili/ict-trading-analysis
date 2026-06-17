---
name: update-memory
description: Keep the project memory current. Use at the end of a work session / before stopping, or whenever the architecture, status, conventions, domain model, or automation layer change — update CLAUDE.md (Status + any changed section) and docs/PLAN.md so the next session resumes accurately. Memory must reflect reality after each period of work.
allowed-tools: Read Edit Grep Glob Bash(git status *) Bash(git log *) Bash(git diff *)
---
# Update project memory (do this after each period of work)
Memory = **CLAUDE.md** (live conventions + status, read first by every session) + **docs/PLAN.md** (the
blueprint). After any meaningful work, make them true again.

1. **See what changed:** `git status`, `git log --oneline -10`, and the current diff.
2. **Update `CLAUDE.md`:**
   - **## Status** — what now exists (modules built, WP progress, what works) and the next concrete step.
   - Any convention/architecture/command/config that changed (new module, new `Ict:*` key, new skill/agent).
   - The **Automation layer** lists when an agent/skill is added or renamed.
3. **Update `docs/PLAN.md`** (and the canonical plan at `~/.claude/plans/…`) if architecture/scope/§-content
   changed, then re-snapshot the plan into `docs/PLAN.md`.
4. **Be accurate and concise** — no aspirational claims; memory must match the repo exactly. Stale memory
   makes the next session do the wrong work.
5. Commit the memory update with the change (or as a `chore` commit) per the `git-workflow` skill.

This is mandatory at the end of a work session — the `Stop` hook will remind you while code changes are
pending under `src/`, `tests/`, or `web/`.
