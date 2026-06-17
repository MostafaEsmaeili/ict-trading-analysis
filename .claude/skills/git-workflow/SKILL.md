---
name: git-workflow
description: The team's Git/GitHub contribution workflow — open a GitHub issue first, branch as feature/#<issue>-<title>, write imperative commit titles "#<issue> Add X" (< 72 chars) with an 80-column-wrapped body explaining WHY (not what), and open a PR that states the issue and the fix. Use for every code change that will be committed, pushed, or PR'd.
allowed-tools: Read Grep Glob Bash(git *) Bash(gh *)
---
# Git / GitHub contribution workflow (follow for EVERY change)

## 1. Issue first (it owns the number)
Every change begins from a GitHub issue — its number `N` is used in the branch, commits, and PR.
Create one if it doesn't exist: `gh issue create --title "<imperative summary>" --body "<the problem +
the intended outcome>"`. Capture the returned number.

## 2. Branch
Branch off the default branch: `git switch -c <type>/#N-<kebab-title>` where `type` is one of
`feature | fix | refactor | chore` (default `feature`).
Example: `feature/#42-trade-style-timeframe`.

## 3. Commits — title + body, imperative, WHY not WHAT
- **Title** (HARD <= 72 chars): `#N <Verb> <subject>`. The verb is an imperative-MOOD command —
  Add, Refactor, Fix, Remove, Update, Rename, Move, Extract, Introduce... NEVER past tense ("Added")
  or gerund ("Adding"). Example: `#42 Add Trade domain`.
- **Body** (after a blank line): prose **hard-wrapped at 80 columns** that explains **WHY** the change was
  made — the motivation, the context, the problem it solves. The diff already shows WHAT changed, so do
  not narrate the code. Reference the issue where useful.
- **Trailer** (last line): `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- One logical change per commit. Use a heredoc for the multi-line message:

```bash
git commit -m "#42 Add Trade domain" -m "$(cat <<'EOF'
The scanner needs a place to express position lifecycle and risk rules that is
independent of persistence and transport, so paper-trade behaviour can be unit
tested deterministically and the live-trading guardrail stays structural.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

## 4. Pull request — what was the issue, how we fixed it
`gh pr create` with title `#N <Verb> <subject>`. Body has two sections:
- **Issue** — what was wrong or what was needed (link `Closes #N`).
- **Fix** — how we addressed it (approach + notable decisions) and how to verify.
- Last line: `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.

## Guardrails
Commit/push only when the user asks; if on the default branch, branch first. Never commit secrets, never
force-push shared branches, never skip hooks (`--no-verify`) unless explicitly asked. Run the
`defensive-guardrail-check` + `/ict-conformance` before opening a PR that touches trading logic.
