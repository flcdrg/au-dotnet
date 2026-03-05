---
description: "Run a pre-release checklist for this repo before tagging or publishing a release."
name: "Release Checklist"
argument-hint: "Release context (optional): version/tag/target branch"
agent: "agent"
---
Run a focused pre-release checklist for this repository (`flcdrg/au-dotnet`).

Use any user-provided release context from the chat input (for example: target tag, branch, or release type) when evaluating results.

Checklist scope:
- Validate repo state and changed files relevant to release safety.
- Validate build pipeline expectations for this repo:
  - `dotnet restore`
  - `dotnet build --no-restore`
  - `dotnet test`
  - `dotnet pack`
- Validate global tool packaging expectations (`AutoUpdate/nupkg` output and installability assumptions).
- Review workflow readiness in `.github/workflows/` for build, publish, and CodeQL consistency.
- Check release-critical configuration and conventions:
  - `global.json` SDK pinning
  - `AutoUpdate/AutoUpdate.csproj` package metadata
  - `README.md` usage/configuration alignment
  - Required environment variables (`PACKAGES_REPO`, `api_key`, `VT_APIKEY`)

Output format:
1. `Release Readiness: PASS` or `Release Readiness: FAIL`
2. `Blocking Issues` section with numbered items and file references.
3. `Warnings` section for non-blocking risks.
4. `Checks Run` section listing commands/actions performed and key outcomes.
5. `Recommended Next Steps` as a numbered list.

Rules:
- Prioritize concrete findings over general advice.
- Include file references for all issues.
- If something cannot be validated in the current environment, state it explicitly under `Warnings`.
- Run `dotnet restore`, `dotnet build --no-restore`, `dotnet test`, and `dotnet pack` by default for this checklist.
- If the user explicitly asks for a read-only/dry analysis, do not run commands and call out that limitation in `Warnings`.
- Do not modify files unless the user explicitly asks for fixes after the checklist.
