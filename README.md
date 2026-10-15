# CodexSubsManager

CodexSubsManager is a standalone Windows tray companion for managing multiple ChatGPT subscription accounts in official Codex.

**Download:** [Latest Windows release](https://github.com/0xmdrakib/CodexSubsManager/releases/latest)

**Live website:** https://codexsubsmanager.vercel.app

---

## Overview

CodexSubsManager is built for two core flows:

- **Manual switching:** Keep your saved Codex accounts together and select the account you want to use.
- **Automatic switching:** Optionally move to the next eligible account in your priority order when fresh reported quota is exhausted.

The app focuses on keeping account management clear and local. Usage bars show the limits actually reported by Codex, credentials are protected for your Windows user, and existing Codex configuration and local workspace history stay in place.

The desktop executable is named `CodexAccounts.exe`, and its window is titled **Codex Accounts**.

## Features

- Multiple saved ChatGPT subscription accounts with manual switching
- Configurable account priority and optional automatic selection
- Separate remaining-quota bars and reset times for every reported usage window
- Five-hour and weekly limits together when both are reported; weekly-only display when applicable
- Usage refresh on startup, after adding or importing accounts, and every 30 seconds by default
- Manual refresh and a configurable refresh interval
- Idle checks, fresh-usage checks, and a cooldown before automatic switching
- Windows DPAPI protection for saved credentials
- Password-protected portable account backups
- System tray controls and an optional Ctrl + Alt + A shortcut
- Optional quiet startup with Windows
- Light and dark appearances with locally bundled icons
- Graceful Codex restart when switching a running desktop session
- Switch rollback and interrupted-switch recovery
- Clean return to official Codex, with optional vault removal
- A lightweight project website with downloads linked to GitHub Releases

## Supported platforms

- Windows x64
- An installed official Codex desktop client and CLI are required for account operations
- The standalone EXE includes its .NET runtime; end users do not need a development SDK

## Switching behavior

### Manual

Select **Switch account** beside a saved account. The manager checks the target sign-in and blocks a switch when Codex is busy. If the desktop client is running, it closes gracefully and reopens with the selected sign-in.

Adding an account does not change the active Codex sign-in. Local project files and chat history stay in the same Codex home. Account-specific permissions, cloud conversations, and external connections depend on the selected account.

### Automatic

Enable automatic selection in **Preferences**. Accounts are considered in your chosen priority order using fresh usage and backend permission. Unknown, stale, disabled, and exhausted accounts are skipped. The manager waits for Codex to be idle and pauses if activity cannot be verified.

Finish active Codex work before switching. A model response already in progress is not continued across an account change. Close other Codex terminal or editor sessions that share the sign-in before switching.

## Usage limits

Limits are read dynamically rather than inferred from a subscription label. Each reported window gets its own remaining-quota bar and reset time; additional usage pools are labeled separately.

Checks never overlap. At the default setting, the next check starts 30 seconds after the previous operation finishes. Cached values are marked when a request fails and are retried automatically. Accounts marked **Manual only** still receive usage updates. A paused manager does not poll automatically.

## Account storage and backups

Saved credentials and transaction backups use Windows DPAPI for the current Windows user. The vault is stored under `%LOCALAPPDATA%\CodexAccounts`, separately from the executable and source code. Account data is not included in the executable or this repository.

Use **Preferences > Export backup** to create a password-protected portable account bundle. Import it on another PC with the same export password, or sign in again there. Copying raw DPAPI vault files is not a portable backup.

To stop using the manager, choose **Return to official Codex**. Keep the current account or restore the saved original sign-in, and optionally remove the manager's vault. This disables the manager's startup integration and automation without deleting Codex configuration or chat history.

## Getting started

1. Download `CodexAccounts.exe` from [GitHub Releases](https://github.com/0xmdrakib/CodexSubsManager/releases/latest).
2. Run it from a permanent folder on your Windows PC.
3. Choose **Import existing** for your current Codex sign-in, or **Add account** for another official browser sign-in.
4. Use **Switch account**, or configure automatic selection in **Preferences**.

Close or minimize the popup to keep it in the system tray. Choose **Quit manager** from the tray menu to stop it for the current Windows session. See [`START_HERE.md`](./START_HERE.md) for detailed instructions.

## Tech stack

- C# and .NET 10
- WPF desktop interface
- Windows DPAPI and native system tray integration
- Official Codex app-server integration
- Dependency-free HTML, CSS, and JavaScript for the website
- Self-hosted Inter font and optimized local artwork
- Vercel static hosting configuration

## Development

Development requires Windows, PowerShell, and the .NET 10 SDK with `dotnet` on PATH. The app has no third-party NuGet package dependencies.

```powershell
# Build and run the regression suite.
.\build.ps1 -Test

# Create a self-contained EXE and portable ZIP without replacing a running copy.
.\build.ps1 -Test -Publish -SkipDirect
```

Generated builds go into `artifacts/`. Quit the manager before replacing its running EXE. The existing standalone release passed 55 core regression checks, light/dark rendering checks, standalone deployment checks, and live usage-refresh verification. Live cross-account switching was not included in those automated checks.

## Website

The static landing page is in [`website/`](./website/). Its download buttons point to the repository's latest GitHub Release, so there is no version number to update in the page.

Import this repository into Vercel using the repository root. The included [`vercel.json`](./vercel.json) selects `website/` as the output directory and skips dependency installation and building. No environment variables are required. See [`website/README.md`](./website/README.md) for asset credits and local preview instructions.

---

## License

This project is licensed under the [MIT License](./LICENSE). Third-party code, fonts, icons, and bundled Microsoft components retain their respective notices in [`THIRD_PARTY_NOTICES.md`](./THIRD_PARTY_NOTICES.md) and [`licenses/`](./licenses/). CodexSubsManager is an independent companion, not an official OpenAI product.
