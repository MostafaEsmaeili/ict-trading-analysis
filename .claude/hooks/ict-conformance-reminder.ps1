#!/usr/bin/env pwsh
# ict-conformance-reminder.ps1
# PostToolUse(Edit|Write) advisory hook (plan §13): when a backend trading-logic file
# under src/ (Domain or a module) is changed, remind the assistant to run /ict-conformance
# (check the change against plan §2.5/§2.5.10). Advisory only — never blocks (exit 0).

$ErrorActionPreference = 'SilentlyContinue'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }

$path = $payload.tool_input.file_path
if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }

$norm = ($path -replace '\\', '/')

# Only nudge for backend trading code: src/IctTrader.Domain/** or src/Modules/<M>/** *.cs
if ($norm -match '/src/(IctTrader\.Domain|Modules/[^/]+)/.*\.cs$') {
    $file = [System.IO.Path]::GetFileName($path)
    $msg = "ICT conformance: '$file' touches trading logic. Run /ict-conformance to check it " +
           "against plan §2.5/§2.5.10 (rule fidelity, contested-point defaults, no magic " +
           "numbers/strings, NY-time only, domain-pure) before marking the change done."
    $out = @{
        hookSpecificOutput = @{
            hookEventName     = 'PostToolUse'
            additionalContext = $msg
        }
    } | ConvertTo-Json -Compress
    Write-Output $out
}

exit 0
