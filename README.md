# CodexSubsManager

CodexSubsManager is a Windows tray app for managing Codex subscription accounts, switching sign-ins, and tracking usage limits.

**Live website:** https://codex.rakibhq.xyz

**Download:** [Latest Windows release](https://github.com/0xmdrakib/CodexSubsManager/releases/latest)

---

## Overview

CodexSubsManager is built for two core flows:

- **Manual switching:** Save your accounts and choose which one to use in Codex.
- **Automatic switching:** Optionally switch to the next eligible account in your priority order when reported quota runs out.

Sign-ins stay local, usage limits stay visible, and your Codex configuration and local workspace history stay in place.

## Features

- Multiple saved accounts with manual switching and configurable priority
- Optional automatic switching with idle checks and a cooldown
- Separate five-hour and weekly usage bars when reported
- Automatic refresh every 30 seconds and after adding or importing accounts
- Windows-encrypted credentials and password-protected account backups
- System tray controls, keyboard shortcut, and optional Windows startup
- Light and dark appearances
- Switch recovery and a clean return to official Codex
- Standalone EXE, also available in a ZIP containing only that EXE

## Supported platforms

- Windows x64
- Official Codex desktop app and CLI required for account operations
- No separate .NET installation required to run the app

## Switching behavior

### Manual

Select **Switch account** beside a saved account. The manager checks the sign-in and gracefully restarts Codex when needed. Adding an account does not switch your active sign-in.

### Automatic

Enable automatic switching in **Preferences**. The manager follows your priority order, checks fresh quota, and waits for Codex to be idle. Finish active tasks before switching; an in-progress response cannot continue across accounts.

## Usage limits

Each limit reported by Codex gets its own bar and reset time. Five-hour and weekly limits appear together when both are available; weekly-only accounts show a single bar.

## Account storage

Credentials are encrypted for your Windows user and stored separately from Codex configuration. Use password-protected backups to move accounts between PCs. See the [quick guide](./START_HERE.md) for setup, backups, and clean removal.

## Tech stack

- C# and .NET 10
- WPF
- Windows DPAPI and native system tray integration
- Codex app-server integration
- HTML, CSS, and JavaScript

---

## License

This project is licensed under the [MIT License](./LICENSE). Third-party notices are listed in [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md).

CodexSubsManager is an independent companion and is not affiliated with OpenAI.
