---
name: mine-ict-transcripts
description: How to (re)run the ICT transcript analysis workflows — the broad 65-transcript taxonomy sweep, the focused 2022-Mentorship entry-model mine, and the ICT-2022 web cross-check — to extract or refresh the codifiable rules catalog when the domain rules need updating or a concept may have been missed.
---
# Mine the ICT transcripts
Each course folder also has a combined `_<Playlist> - FULL PLAYLIST.txt`; the per-episode `.txt` files are
best for parallel fan-out. Saved workflows cache their agents, so resuming is cheap.

- **Focused entry-model mine (2022 Mentorship -> THE setup, plan §2.5):**
  `Workflow({ scriptPath: "C:\Users\Mostafa\.claude\projects\C--Repos-Personal-ICT-transcribe-2022-ICT-Mentorship\77104297-4990-41a9-b598-0603dfc9c8e4\workflows\scripts\mentorship-entry-model-mine-wf_7f702dda-09a.js", resumeFromRunId: "wf_7f702dda-09a" })`
- **Broad 65-transcript taxonomy sweep:**
  `Workflow({ scriptPath: "C:\Users\Mostafa\.claude\projects\C--Repos-Personal-ICT-transcribe\77104297-4990-41a9-b598-0603dfc9c8e4\workflows\scripts\ict-transcript-sweep-wf_b6717b76-098.js", resumeFromRunId: "wf_b6717b76-098" })`
- **ICT-2022 web cross-check (validates §2.5 vs the web + trade-realism, plan §2.5.10):**
  `Workflow({ scriptPath: "C:\Users\Mostafa\.claude\projects\c--Repos-Personal-ICT-transcribe\77104297-4990-41a9-b598-0603dfc9c8e4\workflows\scripts\ict-2022-web-polish-wf_d6b3fe71-0d0.js", resumeFromRunId: "wf_d6b3fe71-0d0" })`

After a run: paste the consolidated rules into plan §2.5 + the `ict-methodology` skill, and extend the
detector list (§4.2/§2.5.6) + `ConfluenceOptions` weights (§2.5.3) with any newly surfaced concept.
Remember: transcripts are PRIMARY; web findings are secondary (provenance-flag them — §2.5.10).
