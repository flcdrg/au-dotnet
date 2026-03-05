# Project Guidelines

## Code Style
- Target C# 12/.NET 10 patterns used in this repo (top-level statements in `AutoUpdate/Program.cs`, primary constructor style in `AutoUpdate/Worker.cs`).
- Keep nullable reference types enabled and preserve existing concise style unless a change clearly improves readability.
- Do not edit build outputs in `AutoUpdate/bin/` or `AutoUpdate/obj/`; make source changes under `AutoUpdate/`.

## Architecture
- This is a single-project .NET global tool (`AutoUpdate/AutoUpdate.csproj`) that automates Chocolatey package updates.
- Entry point is `AutoUpdate/Program.cs`, which configures DI and runs one hosted service.
- Core behavior lives in `AutoUpdate/Worker.cs`:
  - Scans `PACKAGES_REPO` (or fallback `c:\dev\git\au-packages`) for directories containing `update.ps1`.
  - Executes each `update.ps1` via PowerShell SDK.
  - Pushes generated `.nupkg` files with `choco`, tags with `git`, and optionally submits files to VirusTotal (`VT_APIKEY`).
  - Writes GitHub Actions logs and job summary output.

## Build and Test
- SDK is pinned in `global.json` (`10.0.101`). Use that SDK for local and CI-consistent work.
- Standard local commands from repo root:
  - `dotnet restore`
  - `dotnet build --no-restore`
  - `dotnet test`
  - `dotnet pack`
- Tool usage:
  - `dotnet tool install -g flcdrg.au`
  - Run with `audotnet` (or `dnx flcdrg.au` on .NET 10 SDK).
- CI reference workflows:
  - Build/test/pack/install check: `.github/workflows/au-dotnet.yaml`
  - Package publishing: `.github/workflows/publish.yml`

## Conventions
- Environment variables used by runtime behavior:
  - `PACKAGES_REPO` for package source directory.
  - `api_key` for Chocolatey push.
  - `VT_APIKEY` for VirusTotal scan.
- External tooling expectations:
  - Windows-first execution path (`choco.exe`, `git.exe`, `vt.exe` usage in worker process calls).
  - PowerShell scripts are first-class inputs (`update.ps1` per package directory).
- Debug caveat:
  - `AutoUpdate/Worker.cs` currently contains a `#if DEBUG` filter that only processes `azure-functions-core-tools`. Account for this when validating local debug runs.
