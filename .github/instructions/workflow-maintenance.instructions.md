---
description: "Use when creating or modifying GitHub Actions workflows in this repository, including build, publish, CodeQL, and Dependabot automation."
name: "Workflow Maintenance Rules"
applyTo: .github/workflows/*.yml, .github/workflows/*.yaml
---
# Workflow Maintenance Guidelines

- Keep permissions least-privilege at workflow and job scope; do not grant broader `contents`/`actions`/`packages` access than required.
- Preserve trigger intent:
  - Build validation workflows should run on `push` and `pull_request` for `main`.
  - Publish workflow should stay tied to release events.
  - CodeQL should keep scheduled scanning enabled.
- For .NET jobs, prefer `actions/setup-dotnet` with `global-json-file: global.json` for SDK consistency.
- Keep deterministic CI environment settings where relevant (`DOTNET_*`, `NUGET_PACKAGES`, `Configuration`).
- Maintain Windows runners for jobs that depend on Windows tooling (`choco`, PowerShell module paths, `vt-cli`).
- Preserve repository checkout behavior that is required for versioning or history-sensitive tooling (`fetch-depth: 0` where needed).
- Keep cache keys tied to project files (`**/AutoUpdate/*.csproj`) to avoid stale or cross-project cache pollution.
- Use current major versions of trusted actions already adopted in this repo unless there is a compatibility reason not to.
- When adding shell steps, keep commands non-interactive and CI-safe; ensure failures are surfaced by default.
- Do not place secrets in logs or workflow YAML literals; use `${{ secrets.* }}` and avoid echoing sensitive values.
- For CodeQL manual C# builds, preserve restore/build steps before analyze when `build-mode: manual` is used.
- For Dependabot auto-merge, keep automation constrained to Dependabot-authored pull requests and allowed update types.
