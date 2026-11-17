# vXplorer  (v0.1)

A file manager for Windows 11 in **vShell's** look. It reads the theme from vShell's settings and follows it live: change the theme in
vShell and vXplorer re-colours straight away. C# / .NET 10 + WPF, the same stack as vShell. It's a separate app - it works with or
without vShell running (without vShell installed it uses vShell's default theme). Part of the **vSuite** apps with vShell and
[vView](https://vsuite.vrsion.com/).

NuGet packages: `Vanara.Windows.Shell` (MIT) for the Windows Shell parts (icons, the native right-click menu, Windows' copy engine) and
`VirtualizingWrapPanel` (MIT) for the icon views. The code shared with vShell and vView is **vSuite.Core** (below).

## Install

Download `vXplorer-setup.exe` (always the newest: https://vsuite.vrsion.com/vxplorer/vXplorer-setup.exe) (or click **Install** in vShell's
*Settings > Updates > vSuite apps*). It installs for your account only (no administrator rights) into `%LOCALAPPDATA%\Programs\vXplorer`,
with a Start menu shortcut and options to open folders and Win+E with vXplorer instead of File Explorer, "Open in vXplorer" on folders'
right-click menu, and a desktop shortcut. Uninstalling puts File Explorer back and asks whether to keep your settings.

## What it does

### Folders and files

* **Sidebar:** Quick access (your own folders - pin, unpin, reorder), This PC with every drive, and Network. It follows the open folder,
  opening down to it and highlighting it.
* **File list:** real shell icons; folders first; names sort like Explorer ("file2" before "file10"). **Details columns** (right-click a
  header): Name, Date modified, Type, Size, Date created, Dimensions (pictures, videos) and Length (music, videos) - sort by any of them.
  **Group by** Name, Date modified, Type or Size.
* **Views:** Details, or Medium, Large and Extra large icons with thumbnails (View, Ctrl+Shift+1/2/3/6, Ctrl+wheel). Each folder
  remembers its own view and sort (*View > Use this view for all folders* makes one the default). Big folders stay quick.
* **Preview pane** (Alt+P): pictures, text and code in the theme; PDFs, fonts, Office documents and anything else through Windows' own
  preview handlers (as in Explorer); Windows' thumbnail for the rest, and the item's details. A file in the pane can still be renamed,
  moved or deleted straight away.
* **Tabs** in the title bar, each with its own folder and history (Ctrl+T, Ctrl+W, Ctrl+Tab, Ctrl+1-9; middle-click a folder for a new tab).
  Drag tabs to reorder (they slide aside like vShell's pins), drag one out for a new window or onto another window's tabs. Drop files on a
  tab to put them in its folder. Tabs come back next time.
* **Address bar:** breadcrumbs - click one to go there, or the arrow between two for that folder's subfolders; click anywhere else (or
  Ctrl+L / Alt+D / F4) to type a path (`%USERPROFILE%` and the like work).
* **Search** (Ctrl+F / Ctrl+E / F3): names in the open folder and every folder under it, shown as they're found with a Folder column.
  Several words must all be in the name; `*` and `?` are wildcards. Esc or Back goes back to the folder.
* **Type to jump:** start typing a name in the list to go to it.
* **This PC:** drives and network locations as tiles with a bar for the space used, like Explorer's. Drives appear and go as they're
  plugged in.
* **Network** (the sidebar, or Network / `\\name` in the address bar): the computers behind your mapped drives and the ones Windows'
  network discovery finds, with their shared folders. Nothing is looked up until you open it.
* **Zip files** open like folders. Open, copy or drag files out (they're taken out for you); **Extract all** and **Compress** in the
  command bar.
* **Live:** the open folder updates by itself when files are added, removed or changed.

### Working with files

* **Copies, moves and deletes** go through Windows' own copy engine (Recycle Bin, the same rules as Explorer) and show in a **panel under
  the file list**: files and size done, speed, time left, pause, stop. Copies to the same drive wait their turn. When done: Undo, Open
  folder, and the files that couldn't be copied with the reason and Try again. (*Options > Files > Use Windows' copy window* for
  Windows' own.)
* **Already there?** A dialog in the theme: Replace, Skip or Keep both - for all or each, with both sizes and dates.
* **Stopped part way?** If vXplorer or the PC stops during a copy or move, next time it offers to finish it - only what's missing is
  copied. Closing while a copy runs asks first, and an update waits for it.
* Delete -> Recycle Bin, Shift+Delete -> gone for good. F2 (or click a selected name again) renames in place; Ctrl+Shift+N makes a new
  folder ready to name.
* Ctrl+X / Ctrl+C / Ctrl+V in Explorer's clipboard format, so cut / copy / paste works with Explorer and other apps either way.
  Ctrl+Shift+C copies the paths.
* **Drag and drop:** out to Explorer, the desktop or any app; in onto the list, a folder, a sidebar folder, a tab or a drive.
* **Undo / Redo** (Ctrl+Z / Ctrl+Y): renames, moves, copies, new items, deletes, Extract all and Compress.
* **Command bar** above the files: New ▾ (a folder or any type Windows' New menu offers), Cut, Copy, Paste, Rename, Delete, Sort ▾,
  View ▾ - greyed out when there's nothing to do.
* **Right-click** a file or folder for the shell's own menu (Open with, Send to, 7-Zip, Properties …) with vXplorer's Pin to Quick
  access. Right-click the empty part of a folder for vXplorer's menu in the theme (Sort, Refresh, Paste, Paste shortcut, New, Show hidden
  items, Copy folder path, Properties) and **Show more options** for Windows' full one (Shift+right-click goes straight there).
* **Selection box:** drag from an empty part of the list to select what the box touches.

### The app

* **Options** (Ctrl+, or the menu on vXplorer's icon), laid out like vShell's Settings: what vXplorer opens with and what new tabs open;
  row spacing, text size, extensions and the theme's opacity; single or double click; the Recycle Bin question; Quick access; the
  preview pane; opening folders and Win+E with vXplorer (off by default); updates.
* **Updates:** checks https://vsuite.vrsion.com a minute after starting and then daily, shows a notice with what's
  new, and installs and restarts with one click - putting the old version back if the new one won't start. vShell's *Settings > Updates*
  can update it too.
* **One vXplorer:** starting it again opens a new window in the one that's running.
* **Crash safety:** if vXplorer ever closes unexpectedly, next time it puts back your windows and tabs and writes what Windows recorded
  into the log.
* Keys: Alt+Left / Backspace back, Alt+Right forward, Alt+Up up, F5 refresh, Ctrl+H hidden items, Alt+Enter Properties, the mouse's
  back / forward buttons. `vXplorer.exe <folder>` opens that folder.

## The vShell look

| From vShell | In vXplorer |
|---|---|
| Background, Background 2, gradient and angle | The window background |
| Wallpaper Theme | The same colours vShell works out from your wallpaper |
| Text colour | Text; dim text, hover and pressed shades worked out like vShell's |
| Main Accent | Selected items, the active tab's line; the panel outline when the widget style's outline colour is empty |
| Widget style: corner radius, outline | The sidebar, file list and address bar are drawn like vShell widgets; the window's corners and outline follow too |
| Background blur: Blur / Frosted / off, and the opacity | Your wallpaper behind the window (blurred, frosted or as it is) under the theme colour at vShell's opacity - as vShell's bars draw it. *Options > Look > Use the theme's opacity* off = solid |
| Light or dark text | Windows' dark or light mode for the window and the right-click menus |

The icon (a folder holding vShell's three-dot mark) is drawn in your theme's colours, also on vShell's taskbar. Font: Segoe UI.

## Build and run

Needs the .NET 10 SDK on Windows. After cloning, fetch the shared library: `git submodule update --init`.

```
dotnet build vXplorer.csproj -c Release
bin\Release\net10.0-windows\vXplorer.exe
```

For a release, double-click **build.cmd**: `dist\vXplorer.exe` (self-contained, one file - no .NET needed on the PC) with its
`.sha256`, and, with [Inno Setup 6](https://jrsoftware.org/isinfo.php) installed, the installer `dist\setup\vXplorer-setup-<version>.exe`
(`installer\vXplorer.iss`; `build.cmd nosetup` skips it). `dist\site\` gets the update site's files (`vxplorer\`: the exe, the
installer, `latest.json` with each file's SHA-256, `changelog.md`) - copy it over the root of https://vsuite.vrsion.com, where only the
newest release is kept (`build.cmd pre`: a pre-release, in `vxplorer\pre\`).

Settings: `%APPDATA%\vXplorer\settings.json`. Log: `%APPDATA%\vXplorer\vxplorer.log`.

To try a theme without touching vShell's real settings, point `VXPLORER_VSHELL_DIR` at a folder holding a `settings.json`.

## vSuite.Core

What vShell, vXplorer and vView share - the log, the updater, reading vShell's theme (and the Wallpaper Theme), the glass background,
themed questions, the Options pages - lives in **vSuite.Core** (https://github.com/version-au/vSuite.Core), here as the git
submodule `vSuite.Core\`. A fix there reaches all three apps.

## Known gaps

* Search looks at names only, not what's inside files, and doesn't use the Windows Search index (so it reads the folders each time:
  quick on local drives, slower on big network shares).
* Undo covers what was done in vXplorer (not in Explorer or the shell's own right-click menu commands).
* The preview pane can only show what Windows has preview handlers for: Office documents need Office, and Windows 11 has none for video
  or music (those show their thumbnail). PDF and Office handlers draw in their own colours, not the theme's.
* Zips are read-only: files can be taken out, not changed or added in place.
* This PC shows drives and network locations only - not phones / cameras (MTP devices) or media servers, which aren't drives.

## Licence

Copyright (C) 2026 Steven Van Teylingen (version-au). vXplorer is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License, version 3 - see [LICENSE](LICENSE). It comes with no warranty. The shared library vSuite.Core is MIT, and
the parts by others are listed in [THIRD-PARTY.txt](THIRD-PARTY.txt).

Source: https://github.com/version-au/vXplorer · Downloads and updates: https://vsuite.vrsion.com
