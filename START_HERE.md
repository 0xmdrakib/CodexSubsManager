# Start here

Codex Accounts 1.4 is a standalone Windows account manager for official Codex.

## Set up your accounts

1. Open **CodexAccounts.exe**, or extract the optional ZIP and run it from there. You can run just the EXE from any folder. No SDK or administrator access is required; official Codex must be installed for account operations.
2. Select **Import existing** to save the account already signed in to official Codex.
3. Select **Add account**, enter a name, and complete the official browser sign-in. Repeat for your additional accounts.
4. Select **Switch account** beside the account you want to use. Adding an account alone does not change the active Codex sign-in.
5. Select **Open Codex** to launch the official desktop app.

If Codex is open, switching closes it gracefully and reopens it with the selected sign-in. Local workspace files, configuration, and chat history remain in the same location. Close other Codex terminal or editor sessions that share the sign-in before switching.

## Everyday controls

- Search by account name, email, or subscription plan.
- Usage refreshes every 30 seconds by default, on startup, and immediately after adding or importing an account. Use **Refresh usage** for an additional check. Checks never overlap; the interval starts again after an operation finishes. Change it under **Preferences > Advanced settings** if needed.
- Each reported limit has its own bar and reset time: five-hour and weekly when both are present, or weekly alone when that is the only limit. Cached usage is marked if a check fails and is retried automatically. Hover over a reset time for its exact date.
- Open **Account options** using the three-dot vector button to rename an account, adjust its priority, or exclude it from automatic selection.
- Open **Preferences** for automatic switching, quota threshold, cooldown, refresh interval, appearance, and optional executable paths.
- Close or minimize the popup to keep it in the system tray. Click the supplied tray icon once to show or hide it. Press **Ctrl + Alt + A** to open it when the shortcut is available.
- Choose **Quit manager** in the tray menu to stop the app.

Manual switching is the default. Windows startup is enabled by default from version 1.3. New installations use the light neumorphic appearance; existing appearance preferences are preserved. Both light and dark appearances are available.

## System tray and finding the app

The upward arrow beside the Windows clock opens the hidden system tray icons. Look for the supplied blue/violet terminal icon while Codex Accounts is running. Windows controls whether it appears directly beside the clock or inside that arrow.

Click the icon to show or hide Accounts. Right-click it to open Preferences, choose an account, open the app's folder, read the embedded user guide, return control to official Codex, or quit the manager.

Use **Open app location** to find the running EXE in File Explorer. Use **Windows tray settings** to manage icon visibility. You can drag the icon from the hidden area onto the visible system tray. [Microsoft's Windows tray guide](https://support.microsoft.com/en-gb/windows/experience/personalization/customize-the-taskbar-in-windows).

The manager starts quietly in the tray after you sign in to Windows and restores its saved settings. It does not open Codex Desktop or a browser automatically. Leave the EXE at its chosen location. To disable future startup, turn off **Preferences > Start quietly when I sign in to Windows** and save. Your opt-out stays off when you reopen the app.

Version 1.3 updates the earlier startup default once for attached managers. If you previously disconnected through **Return to official Codex**, it stays disconnected and does not resume startup automatically. Disconnection always removes the manager's owned startup entry. Windows can also disable startup through its Startup Apps settings; the manager respects that system control.

**Quit manager** exits completely for the current Windows session and removes the running tray icon. It is disabled while an account operation or dialog is active. Finish or cancel the operation and close the dialog first. The current official Codex sign-in is kept. Quitting does not turn off startup for the next sign-in; use Preferences for that.

When updating, quit the older tray version before opening the new EXE. Your saved accounts and preferences are reused.

## Automatic switching

Enable **Automatically switch by account priority** in Preferences. Accounts are considered in your chosen order. Only fresh, eligible usage is used; unavailable accounts are skipped. The manager reads reported quota windows dynamically, including five-hour, weekly, and future windows. A plan name alone never means unlimited usage.

Automatic switching waits while a Codex task is active. If activity cannot be verified, close Codex to allow a safe switch. An active response cannot be promised to continue without interruption across accounts. Account-specific cloud access, model availability, and connections depend on the selected account.

## Back up your accounts

Select **Preferences > Export backup** and choose a password of at least 12 characters. Keep the resulting encrypted `.accounts` bundle and its password safe. Use **Import backup** to restore it on another PC. The local Windows-encrypted vault alone is not a portable backup.

## Disconnect or remove the manager

1. Select **Return to official Codex**. If you also downloaded the source repository, its **Return to official Codex.cmd** shortcut is an alternative when placed beside the EXE.
2. Keep the current account, which is recommended, or restore the sign-in saved before your first switch. An expired original sign-in may require login again.
3. Leave **Permanently delete my saved accounts and settings** unchecked if you may use the manager again. Select it only to erase the manager's own vault.
4. Select **Return to Codex**. The manager disables its automation, shortcut, and optional startup entry, then exits.
5. You may now delete the portable app folder.

Your Codex configuration, project files, chat history, plugins, and skills remain in place. The manager does not install a service or proxy and does not change global environment variables.

If you keep your saved accounts, open the app again and select **Resume**. Automatic switching stays off until you enable it again.

## Storage and compatibility

Credentials are encrypted for your current Windows user under `%LOCALAPPDATA%\CodexAccounts`. No account data is stored beside the executable. The runtime, icons, help, and license notices are inside the EXE. The release ZIP contains only `CodexAccounts.exe`, with no extra files or nested folders.

This build supports default file credential storage and explicitly configured direct Windows keyring storage. Unsupported encrypted-secrets, ephemeral, or API-key-only modes are refused without rewriting Codex configuration. Advanced settings can select the installed Codex CLI and desktop executables if automatic detection fails.

Validation notes are available in the repository README. Real paid-account browser login and switching have not been performed during development; the first sign-ins require the account owner.
