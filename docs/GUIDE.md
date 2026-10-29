# MD301 Plus guide

New here? The [README](../README.md) has the download link and a quick start.

A lightweight **Windows action ring for the ASUS ProArt Mouse MD301**. Tap one
mapped mouse button, then click one of eight shortcuts around the pointer.
An original dark-and-brass interface takes inspiration from the interaction
pattern of Logitech Actions Ring, without using Logitech software or assets.
Assign Back, Forward, or middle-click directly, or use Gear Link for another
button. The editor is centered on the ring: drag actions onto its eight circles,
swap positions by dragging, and choose the triggering button on a mouse guide.
Native Windows shortcuts and media keys are supported.

The native editor uses larger 11-point body text, higher-contrast secondary
labels, rounded buttons/fields/action cards, and visible keyboard-focus rings.
The mouse guide is an original top-view illustration based on the MD301's
ergonomic thumb rest, split front buttons and ribbed center controls; its
highlighted DPI switch remains directly selectable. Longer ring labels wrap
instead of shrinking or hiding common action names.

## Install

Download **MD301Plus-Setup.exe** from the
[latest release](https://github.com/HugoMSFT/MD301_Plus/releases/latest) and run it.
Setup needs Windows 10 or 11 with .NET Framework 4.8, which is included in
Windows 10 version 1903 and later. It does not need administrator rights.

- MD301 Plus installs for the current user in `%LOCALAPPDATA%\Programs\MD301 Plus`
  and adds **MD301 Plus** to the Start menu. The Start menu shortcut opens Settings.
- **Start MD301 Plus when I sign in to Windows** is selected by default. Run
  Setup again to change it.
- To update, run the newer Setup. If MD301 Plus is running, Setup asks you to
  exit it from its tray menu first. Updating keeps your settings.
- The installer is not code-signed yet, so Windows SmartScreen may warn about it.
  Select **More info**, then **Run anyway**.
- Uninstall from **Settings > Apps > Installed apps**. Uninstalling removes the
  app, its Start menu shortcut, and its sign-in entry, but keeps your settings in
  `%LOCALAPPDATA%\MD301Plus` in case you reinstall. Delete that folder to remove
  them too.

## How it works

```text
Back / Forward / middle button ----------------+
                                                |
Other button -> Gear Link keyboard mapping -> F8 +-> Windows tray companion
                                                         |
                                             Eight-action ring at the pointer
                                                         |
                                         Windows / media key / app shortcut / launcher
```

**MD301 Plus runs the overlay on Windows**. Direct assignment needs no Gear Link.
Gear Link remains an optional way to configure buttons Windows cannot see.
The app does not flash firmware, send undocumented HID commands, or require an
ASUS SDK, Armoury Crate, Logi Options+, a browser tab, or administrator access.
The optional single-key F8 mapping avoids requiring an ASUS companion; modified
keyboard combinations in Gear Link may need one. Nothing is assigned until you
select or capture a trigger and save.

### Research and scope

- [ASUS's MD301 product page](https://www.asus.com/accessories/mice-and-mouse-pads/proart/proart-mouse-md301/)
  documents six programmable buttons and Gear Link customization.
- [The live Gear Link site](https://gearlink.asus.com/en) requires a USB cable
  or wireless receiver for setup, and explicitly permits switching to Bluetooth
  afterward.
- [ASUS's Gear Link guide](https://rog.asus.com/articles/guides/how-to-customize-your-rog-peripherals-with-gear-link--no-downloads-required/)
  documents button configuration, applying settings before closing the browser,
  and optional companion modules for more complex mappings/macros.
- The official MD301 device manifests for
  [USB](https://gearlink.asus.com/view/7381/manifest.json) and
  [receiver](https://gearlink.asus.com/view/7379/manifest.json) identify device page
  version **1.00.28**. Inspection of that page's published keyboard-assignment
  component confirms single-key mappings, while multi-key combinations are
  marked service-based and require a connected companion. This is why the
  optional Gear Link route recommends **F8**, not Ctrl+Alt+F8.
  The published button definitions explicitly name the below-scroll top button
  **DPISwitch**; the editor labels and highlights it as **DPI switch (below scroll)**.
- [Microsoft's virtual-key reference](https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes)
  documents the native volume, playback, browser, and launcher keys used by
  the app. Media controls are real Windows media keys, not guessed player shortcuts.
- [Logitech's introduction](https://support.logi.com/hc/en-us/articles/17859079887127-Introducing-Actions-Ring)
  describes a customizable on-screen overlay.
  Its [Actions Ring FAQ](https://support.logi.com/hc/en-us/articles/17859365804439-What-is-the-Actions-Ring-feature-in-Logi-Options)
  confirms eight primary bubbles, and the
  [Creative Console guide](https://support.logi.com/hc/en-us/articles/27046845711511-How-does-the-Action-Ring-work-with-the-MX-Creative-Console)
  documents clicking a bubble to launch an action.

These public pages and the published MD301 device-page bundle were reviewed on
October 28, 2026. No public ASUS API for adding an overlay to Gear Link was
found. **The device page was inspected, but a connected MD301's configuration
and hardware-generated hotkey have not been verified.** The integration uses
ordinary Windows keyboard input rather than assuming a proprietary protocol.

This version provides **one global ring** with eight editable keyboard shortcuts
or launch targets, plus Productivity, Media, and Windows presets.
Logitech plugins, app-specific profiles, folders, dial
adjustments, haptics, and hold-to-release selection are not implemented.

## Build from source

Requires Windows 10/11 with .NET Framework 4.8, or 4.8.1 on Windows ARM64.
No NuGet packages, Python, Node.js, or separate .NET SDK are required.

From Windows PowerShell in this checkout:

```powershell
.\build.ps1
.\bin\MD301Plus.exe
```

The compiler shipped with Windows .NET Framework produces a portable AnyCPU
application. Keep `MD301Plus.exe` and `MD301Plus.exe.config` together.
The build generates and embeds a multi-resolution mouse icon (16-256 pixels):
a minimal graphite MD301-inspired silhouette with its asymmetric thumb rest,
light outline, and brass scroll wheel. The executable, Settings window,
and notification-area icon share this artwork; no external icon file is needed
at runtime. Its source artwork is in `tools\New-AppIcon.ps1`.
First launch opens Settings; afterward the app lives in the notification area.
Double-click its mouse icon, right-click it and choose **Settings...**, or open
**MD301 Plus** from the Start menu. The notification icon may be in Windows's
hidden-icons overflow.

```powershell
.\bin\MD301Plus.exe --settings
.\bin\MD301Plus.exe --preview
```

Preview is deliberately inert: choosing a bubble reports its command but does
not send keys or launch anything. Only one copy runs at a time. Starting
MD301 Plus again, from the Start menu or with any flags, opens Settings in the
running copy.

## Design your action ring

1. Choose **Everyday**, **Media**, or **Windows** in the shortcut palette on the
   right. Selecting a category changes the available actions, not your ring.
2. Drag an action onto any of the eight circles. It replaces that position only.
   Drag an existing circle onto another to swap their positions.
3. Click a position to select it. Click the center hub or **Edit action** to
   customize its label, shortcut/launcher, and icon. **+ Custom shortcut or app**
   creates a custom action at the selected position.
4. **Fill ring with this set** replaces all eight positions after confirmation.
   Otherwise, mix individual actions from different categories freely.
5. Click **Save changes**. The editor stays open so you can test the result.
   **Preview ring** is inert; closing with unsaved edits asks before discarding.

Keyboard alternatives: arrow keys select a ring position, Ctrl+arrow swaps it
with its neighbor, and Enter edits it. In the palette, Enter or double-click
assigns an action to the selected position. Ring positions, the edit hub, the
mouse guide, and palette actions expose named accessible actions as well.

## Assign the below-scroll DPI button

The small top button immediately below the main scroll wheel is the **DPI
switch**. It is not the scroll-wheel click and does not emit a normal Windows
mouse-button event until remapped.

1. On the left of the editor, choose **DPI switch (below scroll)**, or click the
   highlighted top button on the mouse guide.
2. Leave **Mapped key** as **F8**, or use another single key F1-F11 if F8 is
   reserved elsewhere. The instructions update to match the chosen key.
3. Click **Open Gear Link...**. The app temporarily pauses its trigger so it
   cannot intercept the key while you record the device mapping.
4. Connect the MD301 by USB or its 2.4 GHz receiver. In
   [Gear Link](https://gearlink.asus.com/en), select the MD301, select its DPI
   button, choose the keyboard assignment, and assign **F8 alone** (or your
   chosen key). Apply/save the mapping using Gear Link's controls.
5. Return to MD301 Plus and click **Save changes** to apply and resume the ring.
   Press the DPI switch. While the editor has focus, this displays a safe
   preview and reports that the saved trigger was received.

This is an explicit DPI setup option, **not an automatic firmware remapper**.
The Gear Link step is required. If pressing F8 on the keyboard opens the ring
but the physical DPI switch still changes sensitivity, check the device profile
and assignment in Gear Link.

A persistent **RING PAUSED FOR SETUP** notice stays visible until saving or
resuming. If you close the editor without saving, the previous saved trigger
is resumed. If resuming fails, the editor stays open and reports the error.
The tray also offers **Resume saved ring**.

The recommended single-key mapping needs no ASUS macro companion. Multi-key
Gear Link combinations may require one. After applying a device-side mapping,
ASUS says it can also work over Bluetooth and the Gear Link tab can be closed;
companion-dependent mappings may still require that companion to run.

## Assign a side button or wheel click directly

1. On the left, choose **Back**, **Forward**, or **Wheel click**. Alternatively,
   use **Detect side / wheel button** and press the desired button anywhere.
2. Click **Save changes**. The assignment is now active while MD301 Plus runs.
   The selected logical button's original click is consumed, so opening the
   ring does not also navigate Back/Forward or middle-click the active app.
3. Tap that button to open the ring; tap it again to close it. Choose
   **Not assigned** and save, or exit the tray app, to restore normal
   button behavior.

First launch is **unassigned**: no mouse button or keyboard shortcut is claimed
before you save an assignment. Left and right click cannot be assigned.
Changes, including detected buttons, remain inactive until Save changes.
Direct assignment applies to the matching button on **all connected mice**;
it does not rewrite the MD301's firmware or identify an individual device.

The mouse must emit an ordinary Back (`XBUTTON1`), Forward (`XBUTTON2`), or
middle-button event. If Gear Link already remapped it to a key, restore its
normal mouse function there, or use the keyboard option below. ASUS-only buttons
such as DPI generally need that Gear Link route. Other remapping software can
also intercept a button before MD301 Plus.

## Other Gear Link keyboard mappings

1. Choose **Keyboard**, type the desired keyboard trigger, and save.
   F8 will be reserved globally, including on your physical keyboard.
   You can choose a different unused function key instead.
2. Use **Open Gear Link...** to pause the trigger while recording the key.
3. Connect the MD301 with its USB cable or 2.4 GHz receiver, not Bluetooth.
   Open [Gear Link](https://gearlink.asus.com/en) in a WebHID-capable browser
   such as Edge or Chrome. Connect and grant access to your mouse.
4. Open the MD301's button-assignment page. Select a spare side/top button,
   preserving normal primary click. Choose the keyboard assignment and assign
   **F8 alone**, without modifiers, a repeat macro, or a toggle.
   Exact control names depend on the device page and firmware.
5. Optional: if you prefer a modified trigger such as `Ctrl+Alt+F8`, set that
   same trigger in MD301 Plus and use Gear Link's combination-key assignment.
   The inspected MD301 page requires its ASUS companion service for multi-key
   mappings. Install a companion only through Gear Link's official controls
   if you choose this option. It is not needed for a single F8 key.
6. Apply/save the mapping using Gear Link's controls, return to MD301 Plus and
   save changes to resume. Try the keyboard trigger first, then the mouse button.
   Both must open the same ring.

After applying an ordinary device-side keyboard mapping, ASUS says settings
can also be used over Bluetooth and the Gear Link tab can be closed.
Mappings dependent on a companion may still require that companion to run.
This project does not automatically change your mouse's existing settings.

## Use and customize

Tap the mapped button to open the ring at the pointer, then click a bubble.
The app waits for buttons and trigger modifiers to be released before sending
the shortcut. The Productivity preset starts with these slots,
**clockwise from the top**:

| Slot | Action | Shortcut |
|---|---|---|
| 1 | Copy | Ctrl+C |
| 2 | Paste | Ctrl+V |
| 3 | Cut | Ctrl+X |
| 4 | Undo | Ctrl+Z |
| 5 | Redo | Ctrl+Y |
| 6 | Save | Ctrl+S |
| 7 | Task view | Win+Tab |
| 8 | Screenshot | Win+Shift+S |

Press **1-8** (including the number pad) to choose without clicking.
Press the mapped button again, Escape, right-click, or click the center/outside
to cancel. Canceling mouse clicks are consumed, so they cannot accidentally
click the document underneath. An unrelated key cancels the ring and passes
through normally. Changing foreground applications also dismisses the ring.

The ring-first editor persists all eight positions and the button choice with
**Save changes**. Filling the ring with a category changes actions, not the
button assignment. Only DPI/keyboard-mode triggers need a matching Gear Link mapping.

### Native media and Windows controls

Choose **Media** in the action palette for these eight native Windows keys:

| Action | Command |
|---|---|
| Play / pause | `MediaPlayPause` |
| Next track | `MediaNextTrack` |
| Previous track | `MediaPreviousTrack` |
| Volume up | `VolumeUp` |
| Volume down | `VolumeDown` |
| Mute | `VolumeMute` |
| Stop | `MediaStop` |
| Open default media app | `MediaSelect` |

These are normal `shortcut` actions without modifiers. They control Windows
volume and the active media session, rather than sending spacebar or a
player-specific shortcut to the foreground app. Playback needs a player that
handles Windows media keys; MediaSelect depends on an installed/default handler.
Volume changes by the normal Windows media-key increment per activation.
Aliases `PlayPause`, `NextTrack`, `PreviousTrack`, `PrevTrack`, `Mute`, `Stop`,
and `LaunchMedia` are also accepted.

The **Windows** preset provides Task view (`Win+Tab`), Desktop (`Win+D`),
Explorer (`Win+E`), Settings (`Win+I`), Clipboard history (`Win+V`),
Emoji (`Win+Period`), Quick settings (`Win+A`), and Screenshot (`Win+Shift+S`).
Win+A opens Action Center on Windows 10. Normal Windows feature/enablement
requirements still apply, such as enabling clipboard history.

- `shortcut`: modifiers `Ctrl`, `Alt`, `Shift`, `Win` plus one letter, digit,
  function key F1-F24, or named key. Examples: `Ctrl+Shift+S`, `Win+E`,
  `Alt+Left`, `Ctrl+Plus`. Supported named keys are Enter, Tab, Space, Escape,
  Backspace, Delete, Insert, Home, End, PageUp, PageDown, Left, Up, Right, Down,
  Plus, Minus, Comma, Period, Slash, Backslash, LeftBracket, RightBracket.
  PrintScreen, Pause, CapsLock, NumLock, ScrollLock, the media keys above,
  BrowserBack, BrowserForward, BrowserRefresh, BrowserStop, BrowserSearch,
  BrowserFavorites, BrowserHome, LaunchMail, LaunchApp1, and LaunchApp2 are
  supported too. A keyboard-mode trigger needs a modifier or a single function key.
- `launch`: an absolute app/file/folder path such as
  `C:\Windows\System32\notepad.exe`, or an HTTP(S) URL. There is no shell-command
  interpreter or separate command-line-arguments field. Use only trusted local
  files and shortcuts.

## Behavior, storage, and limits

- Triggers are system-wide, not restricted to the MD301. A direct assignment
  takes over that logical button on every mouse. In keyboard mode, any device
  emitting the chosen key can open the ring. Disable the assignment or exit
  MD301 Plus to restore the normal button/key behavior.
- The ring is a topmost **non-activating** native window. Shortcuts stay targeted
  at the application that was active when the ring opened. If that application
  closes or loses foreground status before delivery, the shortcut is canceled;
  the app never forces another window into focus.
- Placement follows the pointer's monitor, scales with its DPI, and stays inside
  its working area, including monitors with negative coordinates. Near a screen
  edge, the ring shifts inward without moving the pointer.
- A direct mouse assignment uses a lightweight mouse listener while active.
  Button capture temporarily listens for Back/Forward/middle clicks, without
  changing the saved assignment. Menu selection listeners run only while the
  ring is visible; keyboard-only mode has no persistent mouse listener.
  There is no key history, telemetry, network service, or firmware modification.
  The app itself writes no registry keys. The installer adds the Start menu
  shortcut, the Installed apps entry, and the optional sign-in entry under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; uninstalling removes
  them. A launch action can open its configured URL
  through the user's default browser.
- Settings are stored in `%LOCALAPPDATA%\MD301Plus\settings.json`. Saves are
  validated and atomically replace the file. Invalid JSON is reported and left
  untouched rather than silently resetting your actions. Exit the app before
  manually editing the file. The `trigger` accepts `None`, `Mouse4`, `Mouse5`,
  `MiddleMouse`, or a keyboard shortcut; existing version-1 keyboard settings
  remain compatible. Optional `button` metadata remembers an explicit DPI or
  other keyboard-button choice without changing the existing version-1 format.
- Errors appear in a dialog or tray notification, with diagnostics in
  `%LOCALAPPDATA%\MD301Plus\app.log`. If a trigger is already taken, Settings
  opens so you can choose another one. Windows-reserved shortcuts may also fail.
- Elevated applications, UAC/secure desktops, games, and apps that reject
  simulated input can block shortcuts. The app does not bypass these boundaries.
  Windows accepting injected keys does not guarantee that a particular app
  supports the assigned command.
- A selected action is canceled after two seconds if keyboard modifiers or the
  trigger button remain held. Release them and try again.

## Development

```powershell
.\build.ps1 -Test
```

To build while the current executable is running:

```powershell
.\build.ps1 -OutputDirectory 'bin\preview' -Test
```

The dependency-free suite checks eight-slot configuration and persistence,
shortcut event ordering, Windows input ABI layout, DPI/monitor-edge hit testing,
actual ring-editor controls, nonce-protected drag/drop and position swapping,
DPI selection persistence, native mouse-button capture/consumption,
media key codes and real SendInput delivery, native hotkey registration/conflicts,
transactional settings/binding rollback, full tray startup/shutdown, and a real
non-activating overlay. It briefly displays inert settings and ring windows,
without sending shortcuts to the user's applications. Rendered previews are written to
`bin\ring-preview.png`, `bin\media-ring-preview.png`, `bin\settings-preview.png`,
`bin\settings-compact-preview.png`, `bin\mouse-guide-preview.png`, and
`bin\action-dialog-preview.png`.

The native smoke test checks dispatch of a Windows hotkey message; it does not
claim to exercise ASUS hardware. Injected media events are intercepted by a
test-only monitor before changing the user's volume/playback.
Confirm the saved button assignment with the physical mouse. For Gear Link
mode, also compare the keyboard trigger with the mapped mouse button.

### Installer and releases

`.\build.ps1 -Installer` also builds `bin\MD301Plus-Setup.exe` with
[Inno Setup](https://jrsoftware.org/isinfo.php) 6.5.2 or later. The build finds
`ISCC.exe` on `PATH` or in the default Inno Setup folders; otherwise pass
`-InnoSetupCompiler` with its path. The setup script is
`installer\MD301Plus.iss`, and the version comes from `src\AssemblyInfo.cs`.

```powershell
.\tests\Installer.Tests.ps1 -Setup bin\MD301Plus-Setup.exe
```

This end-to-end test installs, updates, and uninstalls MD301 Plus for the
current user. It checks the Start menu shortcut, sign-in entry, Installed apps
entry, second-launch Settings handoff, and that Setup will not replace a running
copy. Because it changes your user profile, it refuses to run while MD301 Plus
is installed or running. GitHub Actions runs it, with the unit tests, for every
pull request.

To publish a release:

1. In `src\AssemblyInfo.cs`, update all three version numbers, for example to
   `1.0.1.0`, `1.0.1.0`, and `1.0.1`.
2. Merge the change to `main`.
3. Tag that commit with the matching version and push the tag:
   `git tag v1.0.1` and then `git push origin v1.0.1`.

The workflow builds and tests the installer, then publishes a GitHub release with
`MD301Plus-Setup.exe` attached. The README download link always points to the
latest release.