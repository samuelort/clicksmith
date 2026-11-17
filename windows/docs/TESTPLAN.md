# Test plan (Windows)

Run `verify.cmd 20` first, then mark each ✅ / ❌. Note Windows version and mouse model.

| # | Scenario | Expected |
|---|---|---|
| 1 | `install.cmd` on a clean PC | 14 steps, self-test all passed, Clicksmith opens with the welcome guide |
| 2 | Edit a file in `src`, run `install.cmd` | Stops at “Checking the release files” and names the file |
| 3 | Same with `-Dev` | Installs |
| 4 | Back → Ctrl+Shift+M only in Teams | Mutes in Teams; normal Back elsewhere |
| 5 | Forward → Play/Pause | Music toggles |
| 6 | Middle → Win+Shift+S (suggestion) | Screenshot tool opens |
| 7 | Hold Forward 600 ms with a hold action | Hold action once; tap action not run |
| 8 | Tap Forward quickly | Tap action only |
| 9 | Hold, then switch app with Alt+Tab before release | Nothing fires, no stray click |
| 10 | Test mode, press Back in Teams | Shows “would run Ctrl+Shift+M”, nothing happens in Teams |
| 11 | Find a button, press Forward | “That was Forward” |
| 12 | Delete a mapping, press Undo | Mapping is back |
| 13 | Settings › Backups › Restore | Older setup back; current one backed up first |
| 14 | Two profiles, switch from the tray | Buttons follow the profile in use |
| 15 | Export, then Import | Preview shows profiles; setup replaced after confirming |
| 16 | Sleep 1 min, wake, press a mapped button | Works; Diagnostics shows “Last refresh” |
| 17 | Lock and unlock | Works |
| 18 | Ctrl+Alt+Shift+F12 | Emergency pause banner; Resume works |
| 19 | Allow left/right click | Confirmation; emergency switch locks on |
| 20 | Kill `Clicksmith.Engine` in Task Manager while Back is blocked | Back works normally at once |
| 21 | Type text with ÅÄÖ and a new line in Notepad | Typed correctly |
| 22 | Open app pointing to Downloads | Refused, logged |
| 23 | Dark mode on/off | Window follows; readable in both |
| 24 | Close the window | Tray keeps running; engine keeps working |
| 25 | `update.cmd` | Updated; previous version kept |
| 26 | Break the build during update (e.g. stop network on a newer SDK) | Previous version restored automatically |
| 27 | `rollback.cmd` | Previous version back |
| 28 | `uninstall.cmd` | Removed from Start menu and Apps & features; setup kept |
| 29 | `verify.cmd 20` while moving the mouse | Engine CPU under 1% |

## 2.5 Beta 1

| # | Scenario | Expected |
|---|---|---|
| 30 | GitHub Actions › Windows | Green: self-test, build, manifest, script syntax |
| 31 | Tag `windows-v2.5.0-beta.1` | Release with 2 zips, Setup.exe, release-windows.json, SHA256SUMS.txt |
| 32 | `irm …/get.ps1 \| iex` on a clean PC | Checksum verified, forge installer, Clicksmith opens |
| 33 | Same without .NET Desktop Runtime | Offers winget install, then continues |
| 34 | Setup.exe | No admin prompt; Start menu entry; engine running; one Apps & features entry |
| 35 | Settings › Updates with a newer release | Download and install → app closes, installer runs, app returns |
| 36 | Tab through Overview, Buttons, Settings | Copper focus ring on every control; Enter/Space work |
| 37 | Narrator on Overview | Reads status, card titles, mapping rows and switches |
| 38 | Toggle a switch with the keyboard | Focus stays on the switch after the page updates |
| 39 | Dark mode: drop-downs, menus, tooltips, scroll bars, dialogs, tray menu | All Nordic dark; no white system controls |
| 40 | New mapping › press Forward | Editor opens for Forward |
| 41 | Mice card | Shows your mouse and connection; DPI note for SteelSeries |
| 42 | Per-app rule for Teams | Real Teams icon |
| 43 | Extra button: GG sends F13 from DPI, map F13 → Win+Shift+S | DPI opens the screenshot tool |
| 44 | Hold the F13 extra button with a hold action | Hold action once, tap action not run |
| 45 | Run DPI probe (Bluetooth, then receiver) | Report on desktop; nothing changes on the mouse |
| 46 | Type text into a browser password field | Nothing typed; log says password field |
| 47 | Open app → PowerShell | Refused, logged |
| 48 | Disk under heavy load, click a mapped button repeatedly | Still instant; hook stays active |
