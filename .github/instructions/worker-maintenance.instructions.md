---
description: "Use when modifying AutoUpdate/Worker.cs, hosted service orchestration, PowerShell execution, package push/tag flow, or process/logging behavior."
name: "Worker Maintenance Rules"
applyTo: "AutoUpdate/Worker.cs"
---
# Worker Maintenance Guidelines

- Preserve end-to-end behavior: discover package directories, run `update.ps1`, push `.nupkg`, tag with git, update GitHub Actions summary.
- Keep cancellation checks in the directory loop and after PowerShell invocation so long runs stop promptly.
- Maintain logging semantics:
  - Use `LogErrorMessage` for errors that should contribute to non-zero exit.
  - Use warnings for recoverable per-package failures where processing should continue.
- Do not remove `ApplyExitCodeFromLoggedErrors()` calls before shutdown paths.
- Keep process execution non-interactive in `RunProcess` (`UseShellExecute=false`, redirected streams, timeout-aware wait).
- Treat external tools as Windows-first (`choco.exe`, `git.exe`, `vt.exe`); avoid Linux-specific command assumptions in this file.
- If changing AUPackage property access (`Name`, `RemoteVersion`, `Files`, `Streams`), guard for null/missing properties to avoid runtime exceptions.
- Preserve VirusTotal limits and behavior:
  - Skip files larger than 650 MB.
  - Require `VT_APIKEY` before submission.
- Keep repo root fallback behavior for `PACKAGES_REPO` unless intentionally changing configuration defaults.
- DEBUG caveat:
  - `#if DEBUG` currently filters to `azure-functions-core-tools`; account for this in local validation and avoid accidental release-impacting behavior changes.
