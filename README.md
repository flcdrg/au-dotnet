# au-dotnet

Chocolatey package automatic updates using .NET.

This is an alternative to the AU PowerShell module for coordinating package updates across multiple packages. It still makes use of the AU module for calling the `update.ps1` scripts.

## Overview

`au-dotnet` is a .NET global tool for automating Chocolatey package updates. It scans a repository of Chocolatey package definitions, executes their `update.ps1` scripts, and manages the package update lifecycle. This tool is particularly useful for maintaining multiple Chocolatey packages in a single repository.

## Features

- **Automated Package Updates**: Scans directories for `update.ps1` scripts and executes them to check for package updates
- **PowerShell Integration**: Leverages PowerShell SDK to execute update scripts seamlessly
- **GitHub Actions Support**: Built-in integration with GitHub Actions Core for CI/CD workflows
- **Package Management**: Automatically handles package building, testing, and publishing to Chocolatey
- **Summary Reports**: Generates detailed summary reports of update operations

## Installation

Install as a .NET global tool:

```bash
dotnet tool install -g choco-au
```

## Usage

### Using .NET 10 SDK

With .NET 10 SDK, you can execute the tool using the new `dnx` command:

```bash
dnx choco-au
```

### Traditional Method

Alternatively, use the traditional tool invocation:

```bash
choco-au
```

## Configuration

The tool can be configured using environment variables:

- `api_key`: Chocolatey API key for publishing packages
- `PACKAGES_REPO`: Path to the directory containing Chocolatey package definitions (default: `c:\dev\git\au-packages`)
- `VT_APIKEY`: VirusTotal CLI API Key

## How It Works

1. The tool scans the specified repository path for subdirectories containing `update.ps1` files
2. For each package directory:
   - Executes the `update.ps1` script to check for new versions
   - Skips directories that already contain `.nupkg` files
   - Builds and optionally publishes updated packages
3. Generates a summary report of all operations

## Requirements

- .NET 10.0 SDK or later
- PowerShell 7.5+ (embedded via SDK)
- Chocolatey package definitions with `update.ps1` scripts

## GitHub Actions Integration

The tool includes built-in support for GitHub Actions workflows, making it easy to automate package updates in CI/CD pipelines.

## License

Apache-2.0
