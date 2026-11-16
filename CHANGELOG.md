# Changelog

## 0.1.57

- Finishing a copy or move that stopped part way no longer deletes a file that had already made it across (after a move, the only copy).
- Windows' menu (Show more options) on search results from different folders no longer acts on same-named files in the first one's folder: it says they're in different folders. Properties shows each folder's items.
- Undo after copying or moving a folder into one of the same name leaves that folder alone - it used to take everything that was already in it too.
- Try again on a failed "Keep both" item keeps both again, instead of replacing the one you kept.
- Extracting a zip can't write into a folder beside the one it extracts to.

## 0.1.56 · release

- Open in vTerm, Open in vXplorer and other apps' items on a folder's right-click menu work again (they said no app was associated).

## 0.1.55 · release
- Behind the scenes: every build uses the newest shared library (vSuite.Core 1.0.26).

## 0.1.54 · pre-release
- The update notice's progress bar follows the download, instead of standing still until the end (vSuite.Core 1.0.24).
- Options: the scrollbar is the new one too, and keeps clear of the window's resize edge.

## 0.1.53
- Scrollbars easier to grab - never a tiny thumb in a big folder, wider when you point at them (vSuite.Core 1.0.23).

## 0.1.52 · release
- The tabs are the suite's shared tab strip now (the same as vPad's and vTerm's) - they look and work as before.
- The shared library brought up to date (vSuite.Core 1.0.22).

## 0.1.51
- View menu: Use this view for all sub-folders - the folders inside take this folder's view and sort (one with its own view keeps it).

## 0.1.50
- The first release from vsuite.vrsion.com only - a test that updating from the site works.

## 0.1.49
- Updates now come from vsuite.vrsion.com instead of GitHub - no more "GitHub is limiting requests".
- The update notice lists the changes of every version since yours, not just the newest one.
- Options > Updates: "Releases page" is now "vSuite website" (vSuite.Core 1.0.18).

## 0.1.48
- The status bar adds up what's inside selected folders too (counted in the background, "counting…" while it goes); Options > Files can switch it off.
- Typing in the address bar lists matching folders under it: Down / Up to pick, Tab to fill one in and carry on, Enter or a click to go there. Before a backslash it offers This PC, Network, Quick access folders and drives ("c" finds C:).

## 0.1.47
- Options > Files > Progress graph: the line's thickness, fill and box show only when the Line graph is picked; Smooth curves is gone.

## 0.1.46
- The file operations panel's progress can be a Meter, a Line (speed all the way through) or a Ring, drawn like vShell's CPU module, with the line's thickness, fill, curves and box to choose (Options > Files > Progress graph).

## 0.1.45
- The file operations panel can pop out into its own window that you can move anywhere (Options > Files, or the pop-out button on the panel).

## 0.1.44
- The installer is now called vXplorer-setup.exe in every release (no version in the name), so the latest one always has the same download link. (shared library 1.0.17)

## 0.1.43
- Thumbnails Windows has seen before show straight away instead of waiting behind new ones (shared library 1.0.15).
- Programs' and shortcuts' own icons load about 2.5x faster.
- Big folders (Downloads) show all their icons in about a third of the time, the rows at the top first (shared library 1.0.16).

## 0.1.42
- The see-through background and the Wallpaper Theme always read the picture each monitor really shows (shared library 1.0.14).

## 0.1.41
- A file made hidden leaves the list straight away (while hidden items aren't shown).
- An update waits while a copy is running, instead of closing vXplorer in the middle of it.
- Updates: Update in vShell's Settings no longer leaves an install waiting for later if the check fails, never installs a version that was skipped or didn't start, and a release without its checksum isn't installed.
- Questions: Enter presses the button that has the focus (Tab moves it).
- The see-through background asks Windows about the wallpaper less often, and a plain-colour desktop shows that colour (not the old picture).

## 0.1.40

- Include pre-releases (Options > Updates) can be changed from vShell's Settings > Updates > vSuite apps - the switch follows straight away.

## 0.1.39

- vShell's Settings > System > vSuite apps can update vXplorer: it checks and installs straight away (started with --update, or the open vXplorer is asked).
- With a plain-colour desktop the see-through window shows that colour, not black.

## 0.1.38

- Options looks like vShell's settings: the (i) opens a help card, and the buttons and other controls are the same as vShell's.

## 0.1.37

- Your user folder (C:\Users\you) no longer keeps reloading with its icons flashing - hidden Windows files changing there are ignored while hidden items aren't shown.
- When a folder does change, the icons and thumbnails of what's still there stay put instead of loading again.

## 0.1.36

- Options > Look > Use the theme's opacity: switch it off for solid windows.
- With Blur and Frosted off, the window shows your wallpaper (not blurred) through the theme colour at your opacity, matching the bars and widgets - it was solid.

## 0.1.35

- Blur and Frosted are drawn as vShell's bars draw them - your blurred wallpaper under the theme colour - so vShell's opacity setting now shows (Windows' Mica hid it).
- Cards and the Options page list are a touch stronger against the background, as in vShell's settings.

## 0.1.34

- Follows vShell's Wallpaper Theme: with it on, vXplorer takes the colours vShell works out from your wallpaper (it showed vShell's saved colours before).
- Picks up vShell's theme when vShell is installed while vXplorer is running, without a restart.
- The first start on a new PC no longer has a problem setting up the Details columns.
- Built on vSuite.Core, the library vXplorer now shares with vShell and vView (the theme, glass, questions, log, updates and Options are the same code in each).

## 0.1.33

- After vXplorer closes unexpectedly it no longer asks to reopen your tabs when it starts with them anyway - it just puts back every window and tab you had open.

## 0.1.32

- A folder copy that stopped part way (vXplorer ended in Task Manager, the PC turned off) is now offered to finish next time - before, only single files were.
- The taskbar icon is sharp: vXplorer gives vShell a large icon drawn at its size, and pins keep it once vXplorer closes.

## 0.1.31

- Copies, moves and deletes show their progress in a panel under the file list instead of Windows' copy window: files and size done, speed, time left, pause and cancel.
- Copies to the same drive wait their turn; Start it now runs one straight away. The panel can be folded into the status bar, and the taskbar button shows the progress.
- When it's done: Undo and Open folder; files that couldn't be copied are listed with the reason, and Try again.
- Items that are already there: a dialog in the theme to Replace, Skip or Keep both - for all of them or for each one, with both sizes and dates.
- If vXplorer (or the PC) stops part way through a copy or move, next time it offers to finish it - only what's missing is copied.
- Closing vXplorer while a copy runs asks first.
- Options > Files > Use Windows' copy window brings back Windows' own progress window.
- Icons in big folders on network drives show straight away; thumbnails load four at a time.

## 0.1.30

- Options looks like vShell's settings: a list of pages on the left, settings in cards, joined buttons for choices and (i) for the details.
- Options opens on the page you had open last.

## 0.1.29

- Click the name of a selected file or folder again to rename it, as in Explorer.

## 0.1.28

- If vXplorer ever closes unexpectedly, next time it says so and offers to reopen the windows and tabs you had open.
- What Windows recorded about it (the error and where it happened) goes into vxplorer.log, so there's something to look at or send.
- Errors on vXplorer's background threads are written to the log too.

## 0.1.27

- Zip files open like folders: double-click one to look inside (and inside its folders); Up goes back out.
- Open, copy (Ctrl+C) or drag files out of a zip - they're taken out for you. Zips can't be changed in place.
- Extract all (in the command bar when you're in a zip or have one selected): everything into a folder next to it.
- Compress (command bar): the selected items into a new zip, ready to name.
- Ctrl+Z takes back an Extract all or a Compress. Opening vXplorer straight into a zip, Network or a computer works too.

## 0.1.26

- Right-click a column header to choose the Details columns: Date modified, Type, Size, Date created, Dimensions (pictures and videos) and Length (music and videos).
- Sort by any of them (click its header).
- Group by Name, Date modified, Type or Size (Sort > Group by), with a header over each group - in Details and the icon views; kept per folder like the sort.

## 0.1.25

- Each folder remembers its own view and sort (icons in Pictures, Details elsewhere …); folders you haven't changed use the default.
- View > Use this view for all folders makes the current view and sort the default and clears every folder's own.
- Options > Look > Remember each folder's view and sort (on; off = one view for everything, as before).

## 0.1.24

- Redo: Ctrl+Y (or Ctrl+Shift+Z) does again what Ctrl+Z undid - renames, moves, copies, new items and deletes; also in the right-click menu.
- Fixed: undoing a delete could do nothing when the same item had been deleted, brought back and deleted again.

## 0.1.23

- Type to jump: start typing a name in the file list (or This PC) and it goes to the first match.
- The arrows between the folder names in the address bar open a list of that folder's subfolders - pick one to go there.

## 0.1.22

- The title bar follows the theme: with Windows' "accent colour on title bars" on, its blue no longer shows through a see-through theme.
- vShell's Blur and Frosted backgrounds show your wallpaper behind vXplorer, as vShell's bars do - not the windows behind it (a blue app
  behind tinted a red theme blue).

## 0.1.21

- Updates, like vShell's: vXplorer looks for a new version on GitHub a minute after it starts and then about once a day.
- A notice in the window says when one is out, with what's new: Install and restart, What's new, Skip, or close it for later.
- Install downloads it, checks it, puts it in place and starts it again with your tabs; if the new version won't start, the old one comes back.
- Options > Updates: check automatically (on or off), include pre-releases, Check now; also Check for updates in the menu on vXplorer's icon.

## 0.1.20

- Network in the sidebar (and in the address bar - type Network): the computers on your network, each with its shared folders under it.
- Nothing is looked up until you open Network or a computer, so it never slows vXplorer down otherwise.
- The computers behind your mapped network drives show straight away; the ones Windows' network discovery finds join them when the search is done.
- Type \\name or \\address in the address bar to see that computer's shared folders; Up goes from a share to its computer, and from there to Network.
- An installer (vXplorer-setup-0.1.20.exe), like vShell's: installs for you only (no admin), with a Start menu shortcut and options to
  open folders and Win+E with vXplorer instead of File Explorer, add "Open in vXplorer" to folders' right-click menu, and a desktop shortcut.
- The uninstaller puts File Explorer back and asks whether to keep your settings.

## 0.1.19

- This PC in the sidebar is a row with its icon, like the folders: it lights up under the mouse and stays highlighted while This PC is open.
- The drives sit under it, as in Explorer; right-click This PC for Windows' menu (Manage, Properties …).

## 0.1.18

- The command bar sits above the files only; the sidebar reaches up beside it.
- Sort and View in the command bar (the View button moved there from the top right).

## 0.1.17

- Click anywhere in the address bar (not on a folder's name) to type a location; there's always a free space at its end to click, even with a long path.
- A command bar above the files, like Explorer's: New (a folder or any file type), Cut, Copy, Paste, Rename and Delete.
- The buttons grey out when there's nothing for them to do; Paste lights up as soon as files are copied, here or in another app.

## 0.1.16

- Right-click a folder (in the list or the sidebar) for Pin to Quick access / Unpin from Quick access - vXplorer's, in place of Windows' item that pinned to File Explorer's.
- Fixed: vXplorer could close itself a little after any right-click menu, when the sidebar opened down to a folder or Quick access changed.

## 0.1.15

- Options > Look: compact, normal or roomy rows; small, normal or large text in the file list; show or hide file name extensions.
- Options > Opening: open with a single click or a double-click; open folders in the same tab or a new one.
- Options > Files: ask (in the theme) before moving things to the Recycle Bin.
- Options > Sidebar: your own Quick access folders - add, remove, reorder; pin and unpin from the folder's and tab's right-click menus.
- Options > Sidebar: show or hide network drives.
- Right-click a Quick access folder in the sidebar: open in a new tab, move up / down, unpin.
- Options > Preview pane: Windows' previewers for pictures and text too; how much of a text file to show.
- Options > Windows: open folders, drives and Win+E with vXplorer instead of File Explorer.
- Starting vXplorer while it's running opens a new window in the running one.
- The "change the extension?" question is in the theme.

## 0.1.14

- The sidebar follows the open folder: it opens down to the folder you're in and highlights it.
- Click vXplorer's icon at the top left for a menu: New tab, New window (Ctrl+N), Options (Ctrl+,), About and Close window.
- Options: what vXplorer opens with (last tabs, Home or This PC), what a new tab opens, the sidebar following the folder, hidden items, and folders before files.
- Options changes apply straight away, to every vXplorer window.

## 0.1.13

- With vShell's Blur or Frosted background, Windows' own minimise / maximise / close buttons no longer show faintly behind vXplorer's.
- Dragging a tab to the end of a full tab strip scrolls a little faster.

## 0.1.12

- This PC, like Explorer's: your drives under "Devices and drives" and mapped drives and network locations under "Network locations".
- Each drive shows a bar for the space used and how much is free (the bar changes colour when a drive is nearly full).
- Open This PC from the sidebar's "This PC", the address bar ("This PC"), the breadcrumbs, or Up from a drive.
- Right-click a drive for Windows' menu (Eject, Format, Properties …); drop files on a drive to put them there.
- Drives that are plugged in or removed appear and disappear by themselves.
- Dragging a tab to the end of a full tab strip scrolls it more slowly and smoothly.

## 0.1.11

- When the tabs fill the title bar, arrows either side scroll through them (hold to keep going); they grey out at the ends.
- Dragging a tab to the end of a full tab strip scrolls it along.

## 0.1.10

- Drag tabs along the strip to reorder them: the tab follows the mouse and the others slide out of its way, like vShell's pinned icons.
- Drag a tab out of the strip to open it in a new window, with its folder, history and selection.
- Drop a dragged-out tab on another vXplorer window's tabs to move it there; drag a window by its only tab to merge it into another.

## 0.1.9

- vShell's taskbar shows vXplorer's icon in your theme's colours on combined and pinned buttons too, and re-colours it when the theme changes (needs vShell 0.38.161).

## 0.1.8

- Search: the box in the toolbar (Ctrl+F, Ctrl+E or F3) finds names in the open folder and all its subfolders as you type.
- Search results show up as they're found, with a Folder column saying where each one is; * and ? work as wildcards.
- Esc, the x in the box or Back goes back to the folder from the results.
- Ctrl+Z undoes the last rename, move, copy, new item or delete to the Recycle Bin (also in the right-click menu as "Undo …").
- Drag a box from an empty part of the list to select everything it touches; Ctrl adds to the selection, and the list scrolls if you drag past its edge.

## 0.1.7

- The preview pane uses Windows' preview handlers: PDF pages, fonts, and anything else a handler is installed for (Office, mail, PowerToys' previewers …).
- Handlers that can take colours (the font previewer and others) draw in the theme.
- A file shown in the preview pane can still be renamed, moved or deleted straight away - the pane lets go of it first.

## 0.1.6

- Icon views: Medium, Large and Extra large icons with thumbnails of photos, videos, documents and folders, as well as Details.
- Switch views with the View button, View in the right-click menu, Ctrl+Shift+1 / 2 / 3 / 6 or Ctrl+mouse wheel.
- A preview pane (Alt+P or its button): pictures, text and code files, Windows' thumbnail for everything else, and the item's details.
- The preview pane shows the folder's details when nothing is selected and a count and total size for several items.
- Big folders stay quick in the icon views: only what's on screen is loaded.

## 0.1.5

- vXplorer has its own icon: a folder holding vShell's three-dot mark.
- The window's icon (taskbar, Alt+Tab) and the mark in the tab strip are drawn in your vShell theme and change with it.
- vXplorer.exe has the icon too, in vShell's default colours.

## 0.1.4

- Tabs in the title bar, each with its own folder and back / forward history; the active one has an accent line, like vShell's taskbar.
- Ctrl+T new tab, Ctrl+W close, Ctrl+Tab / Ctrl+Shift+Tab to switch, Ctrl+1-9 to jump; middle-click a folder to open it in a new tab.
- Right-click a tab: New tab, Duplicate, Close, Close other tabs, Close tabs to the right; middle-click a tab to close it.
- Drag files onto a tab to put them in its folder; hold them over a tab to switch to it.
- Tabs come back, with the one you were on, next time vXplorer starts.
- Renaming finishes when you click anywhere else (or switch to another app), as if you pressed Enter.
- New > Text Document is always there.

## 0.1.3

- A taller title bar: a strip with the vShell mark and the folder name above the toolbar; grab anywhere on either to move the window.
- Right-click the empty part of a folder: Sort by, Refresh, Paste, Paste shortcut, New, Show hidden items, Copy folder path, Properties.
- New makes a folder or any file type Windows' New menu offers, ready to name.
- Show more options (or Shift+right-click) opens Windows' full menu for the folder.
- Right-click a folder in the sidebar for Windows' menu.
- Menus are drawn in the vShell theme.

## 0.1.2

- Delete sends to the Recycle Bin; Shift+Delete deletes for good (Windows asks first).
- F2 renames in place (the name is selected without its extension); Rename in the right-click menu does the same.
- New folder (Ctrl+Shift+N), ready to name.
- Cut, Copy and Paste (Ctrl+X / C / V) that work with Explorer both ways; cut items show faded until pasted.
- Copy as path (Ctrl+Shift+C).
- Drag files out to Explorer, the desktop or other apps, and drop files onto the list, a folder in it or a folder in the sidebar.
- Ctrl copies and Shift moves while dragging, as in Explorer.
- Copies, moves and deletes use Windows' own progress window and conflict questions, and can be undone like Explorer's.
- Properties (Alt+Enter) and the right-click menu from the keyboard (Shift+F10 / the menu key).

## 0.1.1

- First version: a file manager window in your vShell theme, re-coloured live when the theme changes in vShell.
- Sidebar with Quick access folders and every drive; folders expand as you open them.
- File list with shell icons, Date modified, Type and Size; sort by any column; Explorer's name order.
- Breadcrumb address bar; click it or press Ctrl+L to type a path.
- The shell's own right-click menu on files and folders, dark or light to match the theme.
- The open folder updates by itself when its files change.
- Back, forward, up, refresh, Ctrl+H for hidden items, mouse back / forward buttons.
- Remembers the window, sidebar width, last folder and sort order.
