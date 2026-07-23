# Xbox Guide Tray

Turn a Windows PC into a console-like experience using only an Xbox controller.

Xbox Guide Tray runs in the system tray and manages your Xbox controller over Bluetooth. It maps the Guide button to launch your preferred game front-end—such as [Playnite](https://playnite.link/) or Steam Big Picture—and provides a controller-friendly power menu for shutting down the PC, restarting, or disconnecting the controller. The goal is simple: sit on the couch, pick up the controller, and use the PC the way you would an Xbox.

A companion USB dongle is in development that will plug into a USB port and let the controller wake the PC from sleep and power it on remotely. A GitHub link for the dongle project will be added here when it is published.

## Features

- **Guide button (short press)** — Toggle your configured launcher application (open if closed, bring to front if already running, close if already in front).
- **Guide button (long press, ~600 ms)** — Open the power menu.
- **Power menu** — Turn off the PC, turn off (unpair) the controller, or restart the PC. Navigate with the D-pad or left stick; confirm with A; dismiss with B or Guide.
- **Controller management** — Pair, monitor connection status, and disconnect/unpair from the tray menu.
- **Run at startup** — Optional registration so the app starts with Windows.
- **HidHide integration** — Optional input isolation so controller input does not leak into background apps while the power menu is open. This is not strictly necessary, but without it controller inputs whilst the power menu is open may also register in other open programs that accept them.

## Requirements

- Windows 10 or later (64-bit)
- Bluetooth support on the PC
- An Xbox controller paired over Bluetooth (BLE)
- .NET 8 runtime (included in the installer build)

## Installation

1. Download and run `XboxGuideTray-Setup-1.0.0.exe` from the [Releases](https://github.com/code-fiasco/Xbox-guide-tray/releases) page (or build the installer locally—see [Building](#building)).
2. During setup, choose whether to **install HidHide**. This is recommended if you use the power menu while other apps are visible in the background.
3. If prompted after HidHide installation, **restart Windows** so the driver can load fully.
4. On first launch, open **Settings** and select your controller and launcher application.

## How to use

### First-time setup

1. **Pair your Xbox controller** with Windows via **Settings → Bluetooth & devices** if you have not already.
2. Right-click the **Xbox Guide Tray** icon in the notification area and choose **Settings** (or open Settings from the Start Menu shortcut).
3. Under **Xbox Controller**, pick your controller from the dropdown and click **Refresh** if it does not appear. The status dot shows connection state (green = connected).
4. Under **Application**, browse to your launcher executable—for example:
   - Playnite: `Playnite.DesktopApp.exe` or `Playnite.FullscreenApp.exe`
   - Steam: `steam.exe` with arguments `-bigpicture` (or your preferred Big Picture launch command)
5. Optionally enable **Run at Windows startup**.
6. Click **Save**.

### Daily use

| Action | How |
|--------|-----|
| Open or focus your launcher | Short press the **Guide** button (Xbox logo) |
| Open the power menu | Hold the **Guide** button for about half a second |
| Disconnect the controller | Tray icon → **Disconnect Controller** (unpairs; the app will re-pair when the controller is turned on again) |
| Change settings | Tray icon → **Settings** |
| Install HidHide later | Tray icon → **Install HidHide...** (if not already installed) |
| Exit the app | Tray icon → **Exit** |

### Power menu controls

- **D-pad Up/Down** or **left stick** — Move selection
- **A** — Confirm
- **B** or **Guide** (after releasing the initial long-press) — Close menu

While the power menu is open, Xbox Guide Tray uses HidHide (when installed) to prevent controller button presses from reaching other applications.

## HidHide

[HidHide](https://github.com/nefarius/HidHide) is a third-party Windows driver that can hide selected input devices from most applications while allowing specific whitelisted apps to still receive input.

Xbox Guide Tray uses HidHide only while the **power menu** is open:

1. The app adds itself to the HidHide whitelist.
2. Your controller’s HID devices are temporarily blacklisted.
3. When you close the power menu, the blacklist is cleared.

This stops accidental inputs (for example, pressing A) from activating items in Steam, the desktop, or other software behind the menu.

### Installing HidHide

- **During Xbox Guide Tray setup** — Check *Install HidHide* on the optional tasks page, or
- **After installation** — Tray icon → **Install HidHide...**

A **system restart is often required** after installing HidHide before input blocking works. If the power menu still lets input through to other apps, reboot once and try again.

### Managing HidHide manually

HidHide ships with **HidHide Configuration Client** in the Start Menu. You normally do not need to change its settings—Xbox Guide Tray manages whitelist and blacklist entries automatically. Avoid removing Xbox Guide Tray from the whitelist if you rely on the power menu.

### Uninstalling HidHide

When you uninstall Xbox Guide Tray, the uninstaller asks whether you also want to remove HidHide. Choose **Yes** only if you no longer use Xbox Guide Tray or other software that depends on HidHide. You can always remove HidHide later from **Settings → Apps**.

## Configuration and logs

| Item | Location |
|------|----------|
| Settings file | `%LocalAppData%\XboxGuideTray\config.json` |
| Log file | `%LocalAppData%\XboxGuideTray\app.log` |

## Building

### Run from source

```powershell
dotnet run --project "XboxGuideTray" -c Release
```

### Build the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
powershell -ExecutionPolicy Bypass -File "installer\build-installer.ps1"
```

Output: `installer\output\XboxGuideTray-Setup-1.0.0.exe`

Place `HidHide_1.5.230_x64.exe` in `installer\redist\` to bundle HidHide with the installer (optional; otherwise it is downloaded during setup when selected).

## Companion dongle (coming soon)

A small USB companion device is in development. It will:

- Allow the Xbox controller to **wake the PC** from sleep
- Support **powering off the PC** from the controller when the PC is on

The hardware and firmware repository will be linked here when it is ready for public release.

## License

MIT — see [LICENSE](LICENSE).

HidHide is developed by [nefarius](https://github.com/nefarius/HidHide) and is subject to its own license. Third-party notices are included in the installed application.

## Links

- [Xbox Guide Tray on GitHub](https://github.com/code-fiasco/Xbox-guide-tray)
- [HidHide project](https://github.com/nefarius/HidHide)
