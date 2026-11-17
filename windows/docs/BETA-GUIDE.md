# Clicksmith for Windows — private beta guide

Thank you for testing! This takes about 15 minutes.

## Before you start

- **Use a private PC.** Don't install beta software on a work or school computer — company IT rules usually don't allow it, and managed PCs often block unknown apps anyway.
- Windows 10 or 11, x64 or ARM.
- Clicksmith installs for your user only. It needs no administrator rights and sends no data anywhere.

## Install

**Option A — Terminal (recommended).** Open **PowerShell** and paste:

```powershell
irm https://raw.githubusercontent.com/samuelort/clicksmith/main/windows/get.ps1 | iex
```

It downloads the newest beta, checks it hasn't been changed, and installs it. If .NET 8 Desktop Runtime is missing, it asks to install it (about 55 MB, once).

**Option B — Setup.exe.** Download `Clicksmith-Setup-…exe` from the project's **Releases** page and run it.

## If Windows warns you

Clicksmith isn't code-signed yet, so Windows doesn't recognise the publisher.

**“Windows protected your PC” (SmartScreen)** — click **More info → Run anyway**. Or before opening: right-click the file → **Properties** → tick **Unblock** → OK.

**“Smart App Control blocked this app”** (Windows 11) — there's no exception for a single app. Either:
- use **Option A** (Terminal) instead, or
- wait for a signed version (planned before the public beta), or
- turn Smart App Control off in **Windows Security → App & browser control**. This lowers your PC's protection, so only do it if you're comfortable with that. On recent Windows versions you can turn it back on afterwards.

**Your antivirus quarantines a file** — this can happen with new, unsigned apps. Tell us which file and what the antivirus called it; don't turn the antivirus off.

## First run

1. Clicksmith opens with a short welcome guide.
2. Press a side or middle button on your mouse, then choose what it does.
3. Try **Overview → Test a button**: it shows what a button would do, without doing it.

## What to test

- [ ] Back or Forward → a keyboard shortcut (e.g. Ctrl+Shift+M in Teams only)
- [ ] Middle click → Win+Shift+S (screenshot tool)
- [ ] **Hold** a button for a second action — tap and hold should never both run
- [ ] Two profiles, switched from the tray icon
- [ ] Sleep the PC for a minute, wake it, press a mapped button
- [ ] Lock and unlock, then press a mapped button
- [ ] Ctrl+Alt+Shift+F12 (emergency pause), then Resume
- [ ] Dark mode on and off (Windows Settings → Personalisation → Colours)
- [ ] Use only the keyboard (Tab, Enter, Space) for a minute
- [ ] **Extra mouse button / DPI:** Settings → Diagnostics → **Run DPI probe**, press the button a few times, and send us the report from your desktop. If your mouse software can make a button send F13–F24, try **Buttons → Extra buttons**.

## Reporting

Open an issue with the **Windows beta report** template on GitHub, or send it directly. Please include:

- Windows version, mouse model and connection (Bluetooth, USB or receiver)
- What you did, what happened, what you expected
- **Settings → Diagnostics → Export report…** (no keystrokes or button presses are in it)
- If something hangs: the health check —
  ```powershell
  powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\Clicksmith\tools\verify.ps1" 20
  ```

Never include passwords or text snippets.

## Uninstall

**Settings → Apps → Installed apps → Clicksmith → Uninstall.** Your setup is kept in `%APPDATA%\Clicksmith` in case you reinstall; delete that folder to remove it too.
