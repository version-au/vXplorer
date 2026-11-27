# Testing

Automated (no window): `dotnet test tests\vXplorer.Tests` - run after every change to `src\Core`.

## 0.1.59 - signed with the Certum certificate
1. Before building: SimplySign Desktop connected (its tray icon > Connect, the code from the phone). `build.cmd` says "Signed: vXplorer.exe",
   then Inno Setup signs the setup and its uninstaller. Not connected: a local build says "Building unsigned"; `build.cmd upload` stops.
2. dist\setup\vXplorer-setup.exe and the app: right-click > Properties > Digital Signatures - the Certum certificate, with a timestamp.
   Run the setup: Windows names the publisher (not "Unknown publisher"). After installing, the uninstaller in the app's folder is signed too.
3. An update from the update site still installs (the site's SHA-256 is of the signed files).

## 0.1.59: snap layouts on the maximise button
1. Windows Settings > System > Multitasking > Snap windows on, "Show snap layouts when I hover over a window's maximize button" ticked: rest the pointer on vXplorer's maximise button - Windows' layouts menu opens under it; pick one and the window snaps there. The button lights up while the pointer is on it and goes back when it leaves (also when it leaves fast, off the window).
2. Click it: maximises; again: restores (the glyph follows). Maximised and on the other monitor: the menu opens at the button.
3. Untick the setting with vXplorer open: within a few seconds the button is the plain one again (its own hover and tooltip, no menu). Minimise, close and moving the window: as before.
4. Checked before release: a test copy (its own VXPLORER_DATA_DIR) answers WM_NCHITTEST with 9 (HTMAXBUTTON) only over the maximise button. Automatic: vSuite.Tests `SnapLayoutsTests`.

## 0.1.58: the glass across monitors
1. With the window see-through (Blur, Frosted or the wallpaper as it is): drag it slowly from one monitor onto the next and back - smooth, each part showing its own monitor's wallpaper, moving with the window.
2. Drag it half off a screen's edge: the background keeps moving with it (it used to stop at the edge).
3. Drag it around one monitor: smooth; let go - it's live at once (hover highlights), text as sharp as before.

## 0.1.57: speed
1. `dotnet test tests\vXplorer.Tests`: passes (`NetworkAwayTests` - takes about 4 s).
2. A folder with 20,000 files: Ctrl+A, then F5, a click on a column header, and a change of view: each is done in a moment and the status line shows the whole selection's count and size.
3. Pin a folder on a NAS and leave a tab on it; close vXplorer, switch the NAS off (or block it), start vXplorer: the window is usable within about 4 seconds; the pin is listed (by its folder name), the tab on it is left out; the log says "<place> didn't answer within 4 seconds". Clicking the pin: "vXplorer can't find …". NAS back on: within a quarter of a minute everything on it works again. With the NAS on throughout: nothing changes.
4. While a folder on the NAS is open, take the NAS away and press F5: "<place> isn't answering - showing what was there" (it used to jump to another folder after a long wait).
5. In a zip with a big file (1 GB+): double-click it, and Ctrl+C on it: "Taking … out of the zip…" shows and the window stays usable; the file opens / can be pasted when it is out. Dragging it out still waits while it is taken out.

## 0.1.57: file operations that could lose files
1. `dotnet test tests\vXplorer.Tests`: passes (`ResumeTests`, `ZipPathTests.Extract_all_never_writes_beside_its_folder`).
2. Finishing later: move a few thousand small files to another drive, close vXplorer while it runs > Stop and close, start it > Move the rest. Every file is in one place or the other afterwards - count them (before: the file named in the journal, already moved, was deleted). The log says "removed the half-written …" only for a file that really was cut short (its source is still there and differs).
3. The same with a copy where one item was Keep both, stopped while that item was next: the file that was already there (the one you kept) is still there.
4. Search, select two results in different folders (a\x.txt, b\y.txt), right-click > Show more options: the status line says "Windows' menu works on items in one folder at a time - these are in different folders" and no menu shows. vXplorer's own Delete / Cut / Copy on them work on the right files. Alt+Enter (Properties): one window per folder. Items in one folder: Windows' menu as before.
5. Undo a merge: D:\Backup\Photos holds files; copy another Photos folder into D:\Backup > Replace (merge), then Ctrl+Z: "There's nothing to undo" (or the step before) - D:\Backup\Photos and everything in it stay. Copied together with a loose file: Ctrl+Z takes back only the loose file. A folder copied where there was none: Ctrl+Z takes it back as before. The same with a move.
6. Try again: copy report.docx into a folder that has one > Keep both, with the source locked so it fails (open in Word); close Word > Try again: "report (2).docx" appears and the old report.docx is unchanged.
7. Extract all on a zip with an entry named "..\<zipname>-private\a.txt": nothing is written outside the new folder.

## 0.1.56: apps' items on the folder's right-click menu. Right-click the empty part of a folder > Show more options (or Shift+right-click): Open in vTerm opens vTerm in that folder; Open in
   vXplorer opens it in a new vXplorer window; another app's item (Open with Code, Git Bash Here …) works too. Before: "Windows' menu
   couldn't be shown: No application is associated with the specified file for this operation".. Right-click a folder in the list > Show more options > Open in vTerm / Open in vXplorer: opens that folder.. Windows' own items there (New, Paste, Properties, Sort by …) still work.

## 0.1.54: the update's progress bar (vSuite.Core 1.0.24)
1. Update to a newer release from this version (the running copy downloads it): the notice's bar and percentage move steadily with the
   download, and the window stays responsive during "Checking and installing…". (Updating TO this version still shows the old behaviour -
   it's the version doing the download that matters.)
2. Settings / Options made small enough to scroll: its scrollbar is the thin one that widens when pointed at, with a ~40 px thumb, and sits a
   little in from the window's right edge - grabbing it never starts a resize.

## 0.1.53: scrollbars (vSuite.Core 1.0.23)
1. A folder with thousands of files: the list's scrollbar thumb stays about 40 px tall and widens when pointed at; dragging it top to bottom
   goes through the whole folder. The sidebar, preview and Options scrollbars look the same.

## 0.1.52: the shared tab strip (vSuite.Core 1.0.22 TabStrip). Tabs look as before: folder icons, the chosen one with the accent line, close on hover; many tabs: arrows, the wheel scrolls them.. Click a tab: it shows. Middle-click / its close button: closes. Right-click: New tab, Pin, Duplicate, Close / others / to the right.. Drag along the strip: the others slide aside, it glides into place (and stays there after a restart).. Pull a tab out: a copy follows the mouse; let go on the desktop: a new window the same size with that folder (history and selection
   too); on another vXplorer window's tabs: it moves there, where it was let go. A window's only tab moves the window, joining another's.. Drag files from the list onto another tab: it lights up, shows after a moment, and the files go into its folder on drop.. Esc while dragging a tab: it goes back.

## 0.1.51: Use this view for all sub-folders
1. In your home folder pick Large icons, then View > Use this view for all sub-folders (it shows ticked). Open Documents\any folder:
   Large icons, same sort. The status line said so.
2. Give Pictures its own view (e.g. Details) before or after step 1: Pictures stays Details - the home folder's rule never overwrites it.
   A folder inside Pictures (Pictures has no rule of its own) still gets the home folder's Large icons.
3. Pictures > View > Use this view for all sub-folders: now folders inside Pictures take Pictures' view (the nearest rule wins).
4. Change the view in a sub-folder that was following a rule: only that folder changes. Change it in the home folder itself: the rule
   follows (still ticked), and the sub-folders without their own view follow it.
5. Untick it in the home folder: only that folder keeps the view; sub-folders go back to the defaults (unless they have their own).
6. Drive roots (D:\) and shares (\\server\share) work as rules. Use this view for all folders still clears everything (rules too).
7. Not shown in search results / This PC / Network, or with Options > Look's per-folder views off.

## 0.1.50: the first site-only release
1. On 0.1.49, with 0.1.50 copied to the site: Options > Updates > Check now (or the check at start): 0.1.50 is offered and the
   notice lists the changes of 0.1.50 and 0.1.49.
2. Install: vXplorer restarts on 0.1.50; its log shows it read vsuite.vrsion.com, no api.github.com.
3. A copy older than 0.1.49 (GitHub only): it updates to 0.1.49 from GitHub, then to 0.1.50 from the site.

## 0.1.48: folder sizes in the status bar, address bar suggestions
1. Select a big folder (Windows, Program Files): a moment later the status bar says "1 selected  (counting…)", then the size grows and
   settles. Select something else straight away: the count stops (no CPU / disk left busy).
2. Select files and folders together: one total. A file alone: its size at once, as before.
3. Options > Files > Count the size of selected folders off: folders add nothing (files still do). In This PC, Network or a zip nothing
   is counted.
4. Click the address bar, type `C:\Pro`: a list of the matching folders shows under it, with icons, in the theme. Down / Up move along
   (the box shows the one picked); going past either end puts back what you typed; Esc closes the list (what you typed comes back), a second
   Esc leaves the address bar.
5. Tab fills in the highlighted (or first) folder with a backslash and lists what's in it. Enter on a picked one goes there; Enter with
   none picked goes to what's typed, as before. Click a row: it goes there.
6. Type `do`: Documents and Downloads (Quick access); `c`: the C: drive; `this`: This PC; `\\`: the computers behind mapped drives;
   `\\NAS\`: its shares.
7. Hidden folders are suggested only with hidden items shown. With the list open, move or switch away from the window: it closes.
8. The list opening under a mouse that isn't moving doesn't highlight the row under it; moving the mouse over rows does.

## 0.1.47: line-only graph options
1. Options > Files > Progress graph on Meter or Ring: only the Graph row shows. Pick Line: Line thickness, Fill under the line and Box
   behind the line appear; pick Meter again: they go. Reopen Options on Line: they're there. No Smooth curves row.

## 0.1.46: progress graph styles
1. Options > Files > Progress graph: Meter (the default) - a big copy shows the bar under its text, as before.
2. Line: the row shows a box with the speed drawn from the left edge as the copy goes; across = how far, up = how fast. It stops where
   the copy has got to. Pause: it goes grey. Thickness, fill (None / Light / Solid / Fade), Smooth curves and Box change it straight away.
3. Ring: a round gauge with the percent in it, in place of the copy icon; paused = grey. When it finishes, the tick icon is back.
4. The same in the popped-out window, and in a second vXplorer window's panel.
5. A delete (Shift+Delete on a big folder) shows the chosen graph too.

## 0.1.45: the file operations panel popped out
1. Start a big copy: the panel shows under the list as before. Click its pop-out button (next to fold): the panel leaves the list and
   opens in its own "File operations" window near the bottom right of vXplorer, without taking the focus. Drag it by its title row
   to another screen; make it wider - its height follows the rows.
2. Pause, resume, cancel, Undo, Open folder and Try again work from the window (Open folder opens in the vXplorer window used last).
   Its taskbar button shows the progress.
3. Its close (or fold) button: it goes, the status bar chip shows; click the chip - it comes back where you left it.
4. Clear every row (the window goes), then start another copy: it shows by itself, where you left it - also after a restart.
5. Its dock button (or Options > Files > Pop the panel out, off): back under the list. The switch and the button stay in step.
6. Popped out with a copy finished: close vXplorer (every window) - the panel window goes too and vXplorer exits.
7. Change the vShell theme with the window open: it re-colours.

## 0.1.44: installer without the version in its name
1. build.cmd: dist\setup\ and dist\release\ have vXplorer-setup.exe (no version number); run it - it installs / upgrades as before, and
   Programs and Features shows the right version.
2. Attach it to the release: https://github.com/version-au/vXplorer-releases/releases/latest/download/vXplorer-setup.exe downloads it.
3. vShell's Settings > Updates > vSuite apps with vXplorer not installed: Install finds the new vXplorer-setup.exe (and an older
   release's vXplorer-setup-<version>.exe while the newest release still has the old name).

## 0.1.43

1. Large icons in a big picture folder (Pictures): scroll down a long way and back up - the tiles you've seen fill in at once, new ones follow.
2. Open a folder full of folders you've opened in Large icons before (C:\Program Files): their thumbnails show straight away.
3. Details view in C:\Windows\System32 and the Start menu's Programs folder: the .exe / shortcut icons fill in quickly, none left blank.
4. Downloads in Details, sorted by Date (newest first), opened from a closed vXplorer: the icons at the top show at once, the rest within
   a moment; scrolling stays smooth while they come in.

## 0.1.40

1. vXplorer open on Options > Updates; vShell > Settings > Updates > vSuite apps > vXplorer > Include pre-releases: flip it - vXplorer's switch follows. Flip vXplorer's - vShell's follows.

## 0.1.39

1. With a newer release out: vShell > Settings > System > vSuite apps > vXplorer > Update - vXplorer (open or not) shows its update notice and installs; it restarts on the new version.
2. Desktop set to a solid colour, Use the theme's opacity on: vXplorer shows that colour through its background, not black.

## 0.1.38

1. Options: hover an (i) - a card with the help opens under it (stays while the mouse is on it); clicking the (i) doesn't flip the switch. Buttons (Check now, Add a folder…) look like vShell's settings' buttons. Everything works as before.

## 0.1.37

1. Open C:\Users\<you> (hidden items not shown): the list stays still - no icons flashing every few seconds.
2. In a folder with pictures (Large icons), copy a file into it from elsewhere: the new file appears; the other thumbnails and icons don't blink or reload.
3. Show hidden items (Ctrl+H) in C:\Users\<you>: ntuser.dat.LOG files are listed, and their changes show (the list updates - icons don't flash).

## 0.1.36

0. Options > Look > Use the theme's opacity: off - every vXplorer window (and Options, questions) goes solid at once; on - see-through again.
1. vShell > Appearance > Background: Off, opacity 50%: vXplorer, Options and questions show the wallpaper (sharp) through the theme colour, as a bar does. 100%: solid. Move the window: the picture follows.

## 0.1.35

1. vShell > Appearance > Background: Frosted at 90%: vXplorer (and Options, questions) show a hint of the blurred wallpaper, like the bars.
   Lower the opacity: more wallpaper shows; Blur: no grain; Off: solid. Move the window / to another monitor: the picture follows.
2. Maximise and restore: nothing odd at the edges; the caption buttons work; no faint Windows buttons behind ours.
## 0.1.34

1. Everything looks and works as in 0.1.33: the theme, glass on and off, Options (all pages), a question (Delete), the update notice.
2. vShell > Wallpaper Theme on: within a few seconds vXplorer takes the wallpaper's colours (the same as vShell's bars). Change the wallpaper: vXplorer follows within about 15 seconds. Wallpaper Theme off: back to the saved colours.
3. Options > Updates > Check now: the line updates; with a newer release, the notice appears over the file list.
4. Start vXplorer again with a folder (a shortcut, Win+E with Open folders with vXplorer): it opens in the running vXplorer.

## 0.1.33
. Options > General > When vXplorer opens: Last tabs. Open a few tabs (and a second window), end vXplorer in Task Manager, start it: no question - the windows and tabs are back as they were when it stopped; the status bar says it closed unexpectedly.. The same with Home or This PC: it still asks whether to reopen them.

## 0.1.32
. Copy a big folder (several GB), end vXplorer in Task Manager part way, start it again: "A copy didn't finish … Copy the rest" - only the rest is copied, and the folder matches the original.. vXplorer pinned on vShell's taskbar: the icon is sharp while it runs and after it's closed (the first close after updating refreshes the pin's saved icon). Change the vShell theme: the icon follows.

## 0.1.31

1. Copy a big folder (a few GB) to another drive: no Windows copy window; the panel under the list shows "Copying 37 of 120 files to …", size, speed, time left and the file. The taskbar button fills too.
2. Pause: it stops (the bar greys); Resume carries on. Cancel: it stops, the half-copied file is removed, the row says "Stopped".
3. Start a second copy to the same drive while the first runs: it says Waiting; it starts when the first is done (or at once with ▶). A copy to another drive runs at the same time.
4. Done: "Copied 120 files to …" with Undo (while it's the last thing to undo) and Open folder; it goes after a few seconds (not while the mouse is on the panel).
5. Open a file in another app (e.g. a video playing) and copy its folder: "Copied 119 of 120 files", "1 file couldn't be copied", the file and "It's open in another app."; close the app, Try again.
6. Paste items that are already there: the dialog lists them with both sizes / dates (same ones default to Skip). Replace, Skip, Keep both ("name - Copy") each do that; For all of them sets every row; Cancel copies nothing.
7. Delete a folder to the Recycle Bin: a row "Moved to the Recycle Bin: …" with Undo.
8. Fold the panel (⌄): a chip in the status bar shows the progress; click it to unfold. It stays folded next time.
9. Close the window during a copy: "A copy is still running". Keep going: stays open. Stop and close: closes; start vXplorer again: "A copy didn't finish … Copy the rest" copies only what's missing (the half-written file again).
10. End vXplorer in Task Manager during a big copy, start it again: the same offer; Forget it drops it. With the destination drive unplugged: "Keep it for later".
11. Options > Files > Use Windows' copy window: on = Windows' progress window and questions as before.
12. A network folder with hundreds of subfolders (also opened out in the sidebar): every row has its icon at once. Large icons: thumbnails fill in quickly.

## 0.1.30
. Options (Ctrl+,): pages on the left (General, Look, Files, Sidebar, Preview pane, Windows, Updates, About), the open one with the accent bar. Each page: a big title, then cards.. Click a joined button (e.g. Look > Compact): it fills with the accent and the open windows change at once. Switches: a click anywhere on the row flips them.. Hover an (i): its tooltip; clicking the (i) doesn't flip the switch.. Close Options on Sidebar, open it again: it's on Sidebar. About vXplorer (menu): opens on About.. Change the theme in vShell (or Blur / Frosted): Options re-colours, the page list a shade darker than the page.. Sidebar > Quick access: up / down / remove on each folder, Add a folder…, Back to the standard folders. Updates: Check now and the status line.

## 0.1.29

1. Click a file, wait a moment, click its name again: after a short pause the name turns into the rename box. Same in the icon views.
2. Double-click a file: it opens, no rename. Click a selected file's icon (or its Size / Date): no rename.
3. With several selected, clicking one's name selects just it - no rename. Ctrl / Shift + click: no rename.
4. Click a selected item's name and drag it: a drag, no rename. Click it, then press a key or right-click straight away: no rename.
5. Switch to another app, then click vXplorer on the selected item's name: the window comes to the front, no rename.
6. Options > Opening > single click: clicking never renames. Inside a zip / This PC / Network: no rename.

## 0.1.28

1. Open a few tabs (and a second window with tabs). End vXplorer in Task Manager (End task). Start it again: "vXplorer closed
   unexpectedly" - Reopen: the windows and tabs come back (the second window where it was); Not now: it starts as usual.
2. %APPDATA%\vXplorer\vxplorer.log: "The last run … didn't close normally", and what Windows recorded (after a real crash: the
   error and the stack; after End task: that there was nothing).
3. Close vXplorer normally and start it again: no question.
4. While it runs, %APPDATA%\vXplorer\session.json follows your tabs (a couple of seconds after a change); a normal close removes it.

## 0.1.27

1. Double-click a .zip: its contents, as a folder (the address bar shows … > photos.zip > …). Its folders open too; Up goes back up
   and out of the zip, with the zip selected.
2. Double-click a file in the zip: it opens (from a temporary copy). Ctrl+C / Copy, then paste in a folder: a copy comes out.
   Drag a file out of the zip to the desktop: copied there.
3. In a zip: New, Paste, Cut, Rename, Delete are grey; Ctrl+V / F2 / Delete say zips can't be changed. Search says to open a folder.
4. Extract all (command bar, in a zip or with one selected): a folder named after the zip appears next to it, selected, with
   everything in it. Ctrl+Z sends that folder to the Recycle Bin.
5. Select some files / folders > Compress (the zip icon in the command bar): "name.zip" appears, ready to rename. Opens in vXplorer
   and in Explorer with the same contents. Ctrl+Z takes it back.
6. A big zip: the status bar counts up while extracting / compressing; vXplorer stays usable.

## 0.1.26

1. Right-click a column header: a menu of columns (Name always on). Tick Date created, Dimensions, Length: they appear in every window;
   untick Type: it goes. Remembered after a restart.
2. A folder of photos / videos / music with Dimensions and Length showing: "4032 x 3024", "3:45" fill in after a moment. Click their
   headers: sorted by them.
3. Sort > Group by > Type: "File folder (2)", "JPEG image (40)" … with a line; Date modified: Today, Yesterday, Earlier this week …;
   Size: Folders, Tiny, Small, Medium …; Name: A - H, I - P … Works in the icon views too. (None) takes the groups away.
4. The grouping is remembered per folder (with the view and sort). Search results are never grouped.
5. Grouped: drag a selection box over items in two groups - it selects what it touches.

## 0.1.25

1. In Pictures switch to Large icons; open Documents: Details (the default). Back to Pictures: Large icons again. Same for the sort
   (column header, Sort menu): each folder keeps its own. Tabs too - each shows its folder's view.
2. View > Use this view for all folders: the status bar says so; folders you'd set go back to this one.
3. Options > Look > Remember each folder's view and sort, off: changing the view changes it everywhere, as before 0.1.25.
4. Search results, This PC and Network: changing the view there isn't remembered for a folder.

## 0.1.24

1. Rename a file, Ctrl+Z (old name), Ctrl+Y (new name again), Ctrl+Z (old name). The status bar says what happened.
2. Delete a folder, Ctrl+Z (back), Ctrl+Y (deleted again), Ctrl+Z (back again) - every time.
3. New folder, name it, Ctrl+Z twice (name, then the folder), Ctrl+Y twice (folder, then the name).
4. Undo something, then do something new (e.g. rename another file): Ctrl+Y says there's nothing to redo.
5. Right-click the folder's empty space after an undo: "Redo …" (Ctrl+Y) under "Undo …".

## 0.1.23

1. Click in the file list and type the start of a name ("be"): it jumps to and selects it. Pause a second and type again: a new search.
   Works in the icon views and on This PC's tiles too.
2. Click an arrow between two names in the address bar: it points down and lists that folder's subfolders with icons, the one you're in
   in bold; a big folder's list scrolls. Pick one: it goes there. The arrow after This PC lists the drives, after Network the computers.

## 0.1.22

1. A see-through theme with Blur or Frosted (e.g. the red one, Background #7F090127): the tab strip at the top is the theme's colour, not
   blue - also with Settings > Personalisation > Colours > "Show accent colour on title bars" on. Change the theme in vShell: it follows.
2. Put a brightly coloured window behind vXplorer: its colour doesn't show through; the wallpaper's does (as behind vShell's bars).
3. Pinned on vShell's taskbar, close vXplorer: the pin keeps the themed icon (needs vShell 0.38.162).

## 0.1.21

Updates only work for the installed vXplorer (or dist\vXplorer.exe) - a build folder (bin\Debug, bin\Release) can check but not install.

1. With a release newer than yours on https://github.com/version-au/vXplorer-releases (tag v0.1.N, vXplorer.exe and vXplorer.exe.sha256
   attached): a minute after vXplorer starts, a notice bottom-right of the file list: "vXplorer 0.1.N is available", "You have …", the
   release notes. It's in every open window; the x closes it for now.
2. Install and restart: Downloading … % (MB of MB), Checking and installing…, Installed - restarting vXplorer…; every window closes and
   vXplorer comes back with the same tabs, now the new version (Options > About).
3. Skip: the notice goes and that version isn't offered again (Check now still shows it, marked "You skipped it").
4. Options > Updates: the switches; Check now says "You have vXplorer 0.1.N - the newest, last checked 4:01 PM", or that one is
   available (with Install … and restart), or why it couldn't check. From bin\Debug it also says it can't update itself and why.
5. The menu on vXplorer's icon > Check for updates: the status bar says it's up to date, or the notice comes up.
6. No internet / no releases yet: Check now says why ("the releases page … wasn't found", "GitHub is limiting requests …"); nothing else.

(Tested here with a local stand-in for GitHub - VXPLORER_UPDATE_API - offering v9.9.9: notice, install, swap, restart and the watchdog's
"update ok" all worked.)

## 0.1.20

1. Start vXplorer: Network is at the bottom of the sidebar, closed. Nothing is searched for yet (no delay at start).
2. Click Network: the list empties at once, the computers behind your mapped drives (192.168.1.200, 192.168.1.223) show straight away,
   and the status says it's looking for more. After up to half a minute any others network discovery finds are added. Media devices
   (TVs) aren't listed.
3. Open Network's arrow in the sidebar: the same computers under it. Open a computer's arrow: its shared folders (no C$ / IPC$ / printers).
4. Double-click a computer in the list: its shared folders; the address bar says Network > 192.168.1.223. Double-click a share: its
   files, as any folder. Up: back to the computer (the share selected), Up again: Network.
5. Type \\192.168.1.223 (or Network) in the address bar: goes there. A computer that isn't there: the list says it can't see its shared
   folders, and why.
6. In Network or a computer: New, Paste, Cut, Copy, Rename and Delete are grey; Delete / F2 do nothing; search says to open a folder.
   Right-click a computer or share: Windows' menu (Map network drive …).
7. Open a share, then a folder in it, with Network open in the sidebar: the sidebar opens down to it. With Network closed it doesn't
   start a search to do so.

**Installer** (dist\setup\vXplorer-setup-0.1.20.exe; close vXplorer first - setup asks if it's open)
8. No admin prompt; it goes to %LOCALAPPDATA%\Programs\vXplorer; vXplorer is in the Start menu. Your settings carry over.
9. "Open folders … instead of File Explorer" ticked: afterwards double-clicking a folder on the desktop and Win+E open vXplorer;
   Options > Windows shows the switch on (pointing at the installed vXplorer.exe).
10. "Open in vXplorer" ticked: right-click a folder, a drive and a folder's empty space (Show more options on Windows 11) - Open in
    vXplorer opens it.
11. Run the setup again: the boxes you ticked last time are ticked.
12. Uninstall (Settings > Apps): it asks you to close vXplorer if it's open; afterwards folders and Win+E open File Explorer again,
    Open in vXplorer is gone from the menus, and it asks whether to delete %APPDATA%\vXplorer (No keeps it).

## 0.1.19

1. The sidebar's This PC: Windows' This PC icon, normal text, the drives indented under it. Hover: the row lights up. Click: This PC
   opens and the row stays highlighted; open a drive or folder - the highlight moves to it.
2. Right-click This PC: Windows' menu (Manage, Properties …); Properties opens the System page.

## 0.1.18

1. The command bar starts at the file list's left edge; the sidebar's top lines up with the command bar. Drag the sidebar's edge: the
   bar moves with the file list. With the preview pane open the bar still fits.
2. Sort ▾: Name / Date modified / Type / Size and Ascending / Descending, the current ones ticked; picking one re-sorts (and the column
   header's arrow follows). Grey in This PC.
3. View ▾: Extra large / Large / Medium icons, Details, Preview pane - same as before; no View button left at the top right.

## 0.1.17

1. Click the empty end of the address bar, or between two folder names: it turns into a box with the path selected. Type a path, Enter:
   it goes there; Esc goes back to the folder names. A click on a folder's name still goes to that folder. Long paths: the free space
   at the end is still there to click.
2. Command bar (below the address bar): with nothing selected, Cut, Copy, Rename and Delete are grey; select something and they light up.
3. Copy (or Cut) a file with the button - Paste lights up; click Paste: "name - Copy" appears (Cut: it moves). Copy a file in File
   Explorer: vXplorer's Paste lights up straight away.
4. New: a menu with Folder and Windows' New types; pick one - it's made and ready to name.
5. Rename: the selected item's name turns into a box. Delete: to the Recycle Bin (asks first if Options says so); Shift+click Delete:
   for good (Windows asks).
6. This PC: New and Paste are grey (it can't hold files).

## 0.1.16

1. Right-click a folder in the list: "Pin to Quick access" is third, after Open and Open in new window (Windows' own item, which
   pins to File Explorer's Quick access, isn't there any more). Pick it: the folder appears at the end of the sidebar's Quick access.
   Right-click it again: "Unpin from Quick access" takes it off.
2. Select two or three folders, right-click: Pin pins them all; once they're all pinned it says Unpin.
3. Right-click a folder in the sidebar's This PC part (or a drive): Pin to Quick access is there too. Files get no Pin item.
4. The crash: right-click any file (Esc to close the menu), then go to a folder in another part of the disk - type it in the address
   bar, or pin a folder - so the sidebar opens down to it. vXplorer stays open. (Before, it closed itself a moment later.)
5. Windows' menus still work as before: Open with, Send to, 7-Zip, Cut / Copy, Delete, Rename (in place), Properties, on one item and on
   several; a drive's menu in This PC; Alt+Enter.

## 0.1.15

**Look**
1. Options > Look > rows Compact / Normal / Roomy: the Details rows close up / open out at once, in every window.
2. Text size Small / Normal / Large: the file list's text (Details and the icon views' names) changes size.
3. Show file name extensions off: "report.txt" shows as "report", "notes.md" as "notes"; a type Windows doesn't know ("x.weirdext")
   keeps its extension. F2: the box has just "report"; type a new name, Enter - it's still a .txt. Search results and the preview pane
   show full names.

**Opening**
4. Single click: one click on a file opens it, on a folder goes in. Ctrl+click / Shift+click still select; dragging still drags;
   a click on a drive tile in This PC opens it. Double-click doesn't open things twice.
5. "In a new tab": double-click a folder (or a drive in This PC) - it opens in a new tab; the sidebar still opens in the same tab.

**Files**
6. "Ask before moving to the Recycle Bin" on: Delete asks in the theme ("Move "x" to the Recycle Bin?"); Enter or the first button
   moves it, Esc / Cancel keeps it. Off: Delete goes straight to the Recycle Bin as before.
7. Rename a file's extension (.txt to .md): the "Change the extension?" question is in the theme (no plain Windows box).

**Sidebar**
8. Show network drives off: they leave the sidebar's This PC part (This PC itself still lists them).
9. Options > Quick access: move a folder up / down, remove one, "Add a folder…" (a folder picker), "Back to the standard folders":
   the sidebar changes at once in every window.
10. Right-click the empty part of a folder > Pin to Quick access: it's added at the end; on a pinned folder it says Unpin. Same on a
    tab's right-click menu.
11. Right-click a Quick access folder in the sidebar: Open, Open in new tab, Move up / down, Unpin, Show more options (Windows' menu).
    Shift+right-click: Windows' menu straight away. Other folders in the sidebar: Windows' menu as before.

**Preview pane**
12. "Use Windows' previewers for pictures and text too" on (with PowerToys or another previewer installed): a .txt / .md / photo shows
    in that previewer; types with no previewer still use vXplorer's own. Off: vXplorer's own, in the theme.
13. Text size limit 64 KB / 128 KB / 1 MB: a long text file stops there, with "(the first … )" at the end.

**Windows**
14. Options > Windows > Open folders with vXplorer, on: double-click a folder on the desktop, press Win+E, open a folder shortcut,
    "Open folder" in a browser's downloads - vXplorer opens (a new window in the running vXplorer, if it's open). Off: Explorer again,
    and anything that was the default before comes back. (It points at vXplorer.exe where it is now - turn it off and on again after
    moving vXplorer.)
15. Start vXplorer.exe (or `vXplorer.exe C:\Users`) while it's running: a new window in the running vXplorer; no second vXplorer in
    Task Manager.

## 0.1.14

**Sidebar follows the folder**
1. Open a folder a few levels down (e.g. Documents > something > something): the sidebar opens Documents down to it and highlights it,
   scrolled into view. It uses the nearest Quick access folder (Documents rather than C: > Users > you > Documents).
2. A folder on another drive or a network drive: opened under that drive in This PC's part of the sidebar.
3. Back / Forward / Up / a breadcrumb / switching tabs: the highlight moves with you. This PC or a hidden folder (AppData): nothing highlighted.
4. Clicking a folder in the sidebar still opens it (no flicker or double load).

**Menu and Options**
5. Click vXplorer's icon at the top left: New tab, New window, Options, About vXplorer, Close window - in the theme.
6. New window (or Ctrl+N): a second window, a little down and right, opened on what Options says a new tab opens.
7. Options (or Ctrl+,): a window in the theme (Blur / Frosted too, no ghost buttons). Esc or x closes it. Opening it again brings the open one forward.
8. Starting up: pick Home or This PC, close vXplorer, start it: that, in one tab. Back to "The tabs you had open last time": your tabs again.
9. Tabs: pick This PC, then Ctrl+T or + : a This PC tab. "The same folder": a new tab on the folder you're in.
10. Sidebar: switch "Follow the open folder" off - moving around no longer opens the sidebar or moves the highlight. On again: it catches up.
11. Files: "Show hidden items" - the list shows / hides them at once (same as Ctrl+H). "Folders before files" off - folders and files
    mixed in the sort order (e.g. by name); on - folders on top again. Every open vXplorer window changes together.
12. About vXplorer (menu): Options opens scrolled to About - the version, whether vShell's theme was found, "Open settings folder"
    opens vXplorer's settings folder in a new tab.

## 0.1.13

1. vShell's background blur on Blur or Frosted: look closely at vXplorer's minimise, maximise and close buttons - just the one set, no
   faint darker copies offset behind them. Hover each: the hover colour as before; close turns red.
2. Right-click the empty title bar: Windows' window menu (Restore / Move / Size / Minimise / Maximise / Close), in dark or light to
   match. Alt+F4 closes. Win+Up / Win+Down / snapping still work. (Alt+Space opens the same menu, unless another app - a launcher -
   has taken Alt+Space.)
3. Switch vShell's blur Off and back on with vXplorer open: the buttons stay right both ways.
4. With the tab strip full, drag a tab to an end: it scrolls a bit faster than 0.1.12 did.

## 0.1.12

**This PC**
1. Click "This PC" in the sidebar (it lights up under the mouse): a page with "Devices and drives (N)" and "Network locations (N)",
   each drive as a tile - its icon, name ("Local Disk (C:)"), a bar for the space used and "199 GB free of 465 GB".
2. A drive with less than a tenth free: its bar is in the Secondary Accent instead of the Main Accent.
3. Mapped network drives are under Network locations; so are locations added with Explorer's "Add a network location". A
   disconnected mapped drive says "Disconnected network drive" and has no bar; the others don't wait for it.
4. Double-click a drive or select it and press Enter: it opens. The breadcrumbs say "This PC > Local Disk (C:)" - click "This PC" to go back.
   Alt+Up (or the Up button) from C:\ goes to This PC with C: selected. Back / Forward work through it.
5. Type "This PC" in the address bar: This PC. A tab can show This PC; it's remembered when vXplorer restarts.
6. Right-click a drive: Windows' menu for it (Open, Eject on a USB drive, Format, Properties …). Right-click the empty part: Refresh,
   Properties (Windows' About page), Show more options (Map network drive …). Alt+Enter: Properties of the selected drive.
7. Preview pane: a selected drive shows its big icon, Used (with %), Free, Capacity and File system; nothing selected shows This PC's
   name and the drives' total space.
8. Drag files from the list onto a drive tile: it lights up; drop - they're copied (or moved, same drive) to its top folder.
9. Plug in a USB stick (or eject one) with This PC open: its tile appears (goes) by itself, and the sidebar follows.
10. On This PC the search box says "Open a drive to search it" and is off; Ctrl+V and New folder say a drive or folder needs opening first.
11. Middle-click a drive: it opens in a new tab.

**Tab strip**
12. With the tab strip full (arrows showing), drag a tab towards either end: the strip scrolls slowly (faster the closer to the edge,
    and it keeps going while you hold the tab still there); the dragged tab stays under the mouse.

## 0.1.11

1. Open tabs (Ctrl+T) until they're at their narrowest and don't fit: a left and a right arrow appear either side of them. With few tabs
   there are no arrows.
2. Click an arrow: the tabs move along by one, a whole tab at the edge. Hold it: they keep going. At the start the left arrow greys out,
   at the end the right one. The mouse wheel over the tabs still scrolls them too.
3. Close tabs until they fit again: the arrows go away and the tabs widen.
4. Drag a tab towards an arrow end of a full strip: the strip scrolls, so it can be dropped among tabs that were out of view.
5. Make the window narrower / wider: the arrows come and go as the tabs stop / start fitting.

## 0.1.10

1. Open four tabs. Press on one and drag it sideways: it follows the mouse (kept within the strip), and the tabs it passes slide out of
   its way. Let go: it glides into its place. The order is kept next time vXplorer starts.
2. A plain click on a tab still just switches to it. Esc during a drag (or Alt+Tab away): the tab goes back where it was.
3. Drag a tab down into the list (or up off the window): it comes loose - a copy of it follows the mouse and its place in the strip fades.
   Bring it back to the strip: it's a normal drag again. Let go away from the strip: a new window the same size opens with that tab under
   the mouse - same folder, Back / Forward, selection and scroll. The first window shows the next tab.
4. Drag a loose tab onto another vXplorer window's tab strip: it joins that window where it was dropped, and that window comes to the front.
5. A window with one tab: dragging the tab moves the window (snaps like a title bar). Let go over another vXplorer window's tabs: the tab
   joins it and the empty window closes. Maximised with one tab: the tab doesn't move the window.
6. Close the windows in any order: vXplorer quits after the last one; the sidebar's icons keep loading in the window that's left.
7. File drags onto tabs (hover to switch, drop to move into its folder) still work as before.

## 0.1.9

1. With vShell 0.38.161 or newer, its taskbar combining buttons: start vXplorer - the button shows the folder-and-dots icon in your theme's
   colours, not the exe's fixed blue one.
2. Change the theme in vShell: the taskbar button, Alt+Tab and the tab strip's mark all re-colour within a second or two.
3. Pin vXplorer to vShell's taskbar and start it: the pin shows the themed icon while vXplorer runs, the pin's own icon when it's closed.
4. With an older vShell nothing breaks: the combined button keeps the exe's icon, as before.

## 0.1.8

**Search**
1. The box right of the address bar says "Search &lt;folder&gt;". Ctrl+F (or Ctrl+E, F3, or a click) puts the cursor in it.
2. Type part of a name: after a moment the list shows every match in this folder and all folders under it, with "Searching… N so far"
   in the status bar, then "N results in &lt;folder&gt; and its subfolders (time)". The Folder column says where each one is
   ("Projects\alpha", "(this folder)"); hover it for the full path.
3. Several words: names containing all of them, any order. `*.pdf`, `report-202?.txt`: wildcards. `"two words"` in quotes: that exact text.
4. Search C:\Windows for `*.dll`: results stream in, vXplorer stays responsive, it stops at 10,000 with a message to narrow it down.
5. In the results: open, rename (F2), delete, copy / cut / paste, drag, the right-click menu, the preview pane and the icon views all work.
   A renamed item that no longer matches drops out; F5 searches again.
6. Esc (in the box or the list), the x, Back / Backspace: the folder's contents again, the box empty. Opening a folder from the results
   or switching tabs ends the search too. Hidden items only turn up with Ctrl+H on.

**Undo**
7. Rename a file, Ctrl+Z: it has its old name back ("Undid the rename."). The empty part's right-click menu shows "Undo rename" at the top.
8. Delete a file (Recycle Bin), Ctrl+Z: it's back where it was, selected. Shift+Delete (gone for good) can't be undone.
9. Cut / paste or drag files to another folder, Ctrl+Z: they move back. Copy / paste, Ctrl+Z: the copies go to the Recycle Bin.
10. New folder (or New > a file type, or Paste shortcut), Ctrl+Z: it goes to the Recycle Bin. Ctrl+Z several times goes further back.
11. Rename a file, rename it again in Explorer, then Ctrl+Z in vXplorer: "Can't undo the rename: … isn't there any more."
12. Ctrl+Z while typing in the address bar, the search box or a rename box undoes the typing, not a file operation.

**Selection box**
13. Large icons: press on an empty spot (between or after the tiles) and drag: a box in the theme's colours; every tile it touches is
    selected as it grows and shrinks. The status bar counts them. Let go: they stay selected, arrow keys carry on from there.
14. Details: drag up from below the last row: the rows it reaches are selected.
15. Ctrl while starting the box: adds to what was selected. A plain click on an empty spot: clears the selection.
16. In a long folder, drag past the bottom (or top) of the list: it scrolls, faster the further out, and the box keeps growing -
    items scrolled out of sight stay selected.

## 0.1.7

1. Preview pane on (Alt+P). Click a PDF: its thumbnail, then within a second the real pages with Windows' page controls (page number,
   up / down, fit). Scroll it and click into it - it works like Explorer's preview.
2. Click a font (.ttf / .otf): "The quick brown fox" in sizes, in the theme's text colour on the pane's colour - also with vShell's Frosted /
   Blur background on (not see-through to the wallpaper).
3. With Word / Excel / PowerPoint installed: a .docx / .xlsx / .pptx shows the document. A type with no handler (.zip, .exe): its thumbnail
   or icon, as before. Photos and text / code files: vXplorer's own preview, as before.
4. With a PDF showing in the pane: F2 and rename it, Delete it, Ctrl+X / Ctrl+V it into another folder, drag it to the desktop, and Delete
   from the right-click menu - each works first time, with no "file is open in another program" message.
5. Arrow quickly through a folder of PDFs: vXplorer stays responsive and ends up showing the one you stopped on.
6. Drag the pane's edge, maximise / restore the window: the PDF view resizes with the pane. Alt+P: the pane (and the PDF) goes away.
7. Change the vShell theme with a font showing: it redraws in the new colours.
8. Close vXplorer with a PDF showing, then delete that PDF in Explorer: it isn't "in use".

## 0.1.6

1. View button (top right, next to the address bar) > Large icons: tiles with the picture's thumbnail for photos, the first page or frame
   for documents / videos Windows can thumbnail, a peek inside for folders, the big icon for everything else. Names wrap to two lines.
2. Ctrl+Shift+1 / 2 / 3 / 6: Extra large, Large, Medium, Details. Ctrl+mouse wheel steps between them. Right-click the empty part > View
   has the same, with a tick on the current one. The view is remembered.
3. In an icon view: click, Ctrl / Shift + click, arrow keys, Enter, F2 (the rename box under the icon), drag and drop, cut (faded tile) and
   the right-click menus all work as in Details.
4. Open C:\Windows\System32 in Medium and Extra large icons and scroll quickly to the end: vXplorer stays responsive, the tiles on screen
   fill in first, memory stays reasonable.
5. Alt+P or the Preview button: the pane opens on the right (drag its edge to resize; the width is remembered). Click a photo: the picture,
   its dimensions, size and dates. A .txt / .cs / .json / .md file: its text in a monospace font (the first 128 KB). A PDF, video or Office
   file: Windows' thumbnail. Nothing selected: the folder, how many items it has, its dates. Several selected: the count and total size.
6. Arrow quickly through a folder of photos with the pane open: it keeps up and shows the one you stop on.
7. Theme change with the pane and an icon view open: tiles, selection and the pane re-colour.

## 0.1.5

1. Start vXplorer: the taskbar button, Alt+Tab and the top-left of the tab strip show a folder outline in your Main Accent with the three
   dots (Main Accent, Secondary Accent, text) on your background colour.
2. Change the vShell theme: all three re-colour within a second, without a restart.
3. Small sizes (taskbar at 100 % scaling, the 16 px mark) stay crisp: the outline and dots are heavier there.
4. vXplorer.exe in Explorer / a pinned shortcut shows the icon in vShell's default blues and pink, readable on light and dark backgrounds.

## 0.1.4

1. Ctrl+T: a new tab with your user folder, after the current one. The + button does the same. Each tab shows its folder's icon and name;
   the active one is lit with an accent line under it.
2. Browse in one tab, switch to another and back: the first is where you left it - same folder, selection and scroll position, and its own
   Back / Forward.
3. Ctrl+Tab / Ctrl+Shift+Tab cycle; Ctrl+1 … Ctrl+8 jump to that tab, Ctrl+9 to the last. Ctrl+W closes the tab; closing the last one
   closes the window.
4. Middle-click a folder in the list or the sidebar: it opens in a new tab behind ("Opened … in a new tab"). Middle-click a tab: it closes.
5. Right-click a tab: New tab, Duplicate tab, Close tab, Close other tabs, Close tabs to the right (greyed out when there are none).
6. Open many tabs: they get narrower (down to a minimum), then the strip scrolls with the mouse wheel. There's always some empty title bar
   left to drag the window by.
7. Drag a file from the list onto another tab: the tab lights up; drop - the file moves / copies into that tab's folder. Hold it over the tab
   for a moment instead: that tab opens, and you can drop into its list or a folder in it.
8. Close vXplorer with several tabs open and start it again: the same tabs, on the same one. `vXplorer.exe <folder>` opens just that folder.
9. F2 or New folder, type a name, then click anywhere else - another file, the empty part of the list, the sidebar, the title bar, or
   another app: the name is kept, as with Enter. Esc still cancels.
10. Right-click > New > Text Document: "New Text Document.txt", ready to name, even on a PC where Explorer's New menu has no Text Document.

## 0.1.3

1. The top of the window is a 36px strip (vShell's three dots, the folder name, the window buttons) over the toolbar. Drag from the strip,
   or from the gaps around the toolbar's buttons: the window moves. Double-click either: maximises / restores. The buttons and the address
   bar still work as before.
2. Right-click the empty part of the list: the selection clears and vXplorer's menu opens in the theme (rounded, theme colours, shadow).
   Hover Sort by / New: the submenus open to the side.
3. Sort by: pick a column and Ascending / Descending - the list re-sorts and the column header shows the arrow. The tick shows the current one.
4. Paste and Paste shortcut are greyed out with no files on the clipboard. Copy a file somewhere, then Paste shortcut: "name - Shortcut"
   appears, selected; it opens the file.
5. New > Folder: like Ctrl+Shift+N. New > a file type: "New &lt;type&gt;" with today's date, ready to name. The list of types is the same as
   Explorer's New menu on this PC (types that need a program to run, such as Shortcut, are under Show more options).
6. Show hidden items: ticks / unticks, same as Ctrl+H. Copy folder path: the open folder's path is on the clipboard. Properties: Windows'
   Properties for the folder.
7. Show more options: Windows' own menu for the folder (Open in Terminal and the like, if installed). Shift+right-click on the empty part
   goes straight to it.
8. Shift+F10 or the menu key with nothing selected: vXplorer's menu at the top of the list.
9. Right-click a folder in the sidebar: Windows' menu for that folder (no Rename there).
10. Change the vShell theme with a menu open, or open one after: it's in the new colours; Square-cornered themes give square menus.

## 0.1.2

1. Select a file, press Delete: it goes to the Recycle Bin (with Windows' question if you have that turned on). Shift+Delete: Windows
   asks to delete it for good.
2. F2 on a file: a box over the name with the name selected but not the extension; type, Enter renames, Esc cancels, clicking elsewhere
   renames. Change the extension: vXplorer warns first. Type `a:b`: a message about the characters a name can't have.
3. Right-click > Rename: the same in-place box.
4. Ctrl+Shift+N: "New folder" appears (or "New folder (2)" …) with its name ready to type.
5. Ctrl+C a file, Ctrl+V in the same folder: "name - Copy". Ctrl+V in another folder: copied there and selected.
6. Ctrl+X: the item goes faded. Paste it in another folder: it moves, and pasting again does nothing (the clipboard is empty, as in Explorer).
7. Copy files in Explorer, paste in vXplorer - and the other way round. Cut in one, paste in the other: moves.
8. Copy a big folder: Windows' progress window, and vXplorer keeps working meanwhile. Paste where the names already exist: Windows' "Replace
   or skip" question.
9. Ctrl+Shift+C: the selected paths (in quotes) are on the clipboard.
10. Drag a file from vXplorer to the desktop: moved (same drive) or copied (another drive); hold Ctrl to copy, Shift to move.
11. Drag files from Explorer onto the list: they come into this folder; onto a folder in the list or the sidebar: that folder lights up and
    they go into it. A folder can't be dropped into itself (no-entry cursor).
12. Select several files, press on one of them and drag: all of them go. Click one of them without dragging: just it stays selected.
13. Alt+Enter: Windows' Properties for the selection (or the open folder when nothing is selected). Shift+F10 / the menu key: the
    right-click menu under the selected item.
14. Rename while a copy is still running into the same folder: the rename isn't interrupted; the list catches up afterwards.

## 0.1.1

**Theme**
1. With vShell installed, start vXplorer: the background, text, accent (selected rows), corners and panel outlines match your vShell theme.
2. Change the theme in vShell: within about a second vXplorer re-colours, without a restart. Try a light one: the window and the
   right-click menu turn light.
3. Pick a widget style with no outline / square corners: the panels and the window follow.
4. Turn vShell's background blur to Frosted: the wallpaper shows blurred through vXplorer (Acrylic); Blur gives Mica; Off is solid.
5. Without vShell's settings (or with `VXPLORER_VSHELL_DIR` pointing at an empty folder): vShell's default theme, and the status bar says so.

**Browsing**
6. The sidebar lists your user folder, Desktop, Documents, Downloads, Pictures, Music, Videos and every drive (network drives too).
   Arrows open folders underneath; clicking one shows it in the list.
7. Double-click a folder (or select it and press Enter): it opens. Double-click a file: it opens in its program.
8. Back / Forward / Up buttons, Alt+Left / Alt+Right / Alt+Up, Backspace and the mouse's side buttons all work. Up selects the folder you
   came out of.
9. Click a breadcrumb: goes there. Click the empty part of the address bar (or Ctrl+L, Alt+D, F4): type `%USERPROFILE%\Downloads`, Enter:
   goes there. Type nonsense: a friendly message in the status bar. Esc puts the breadcrumbs back.
10. Click each column header: sorts by it; again reverses. Folders stay on top. `file2` comes before `file10`.
11. Open `C:\Windows\System32`: the list shows quickly and icons fill in after; scrolling is smooth.
12. Open a folder you can't read (e.g. `C:\System Volume Information` with Ctrl+H on): "You don't have permission to open this folder."
13. Ctrl+H: hidden files appear and "Hidden items shown" shows in the status bar; again hides them.

**Shell menu and live updates**
14. Right-click a file: Windows' menu with Open with, Send to, Properties … Right-click an unselected file in a multi-selection: only it
    gets selected. Shift+right-click: extra items (Copy as path …).
15. With the folder open in vXplorer, create, rename and delete files in Explorer: the list updates by itself, keeping your selection.
16. Delete the folder you're in from Explorer: vXplorer moves to its parent.

**Window**
17. Drag the window by the empty part of the top row; double-click there maximises. Maximised: nothing is cut off at the edges. Snap
    (Win+arrows) works.
18. Close and reopen: same size, position, sidebar width, folder and sort. `vXplorer.exe C:\Users` opens that folder.

## Review fixes (0.1.41)
1. Show hidden off: right-click a file > Properties > Hidden > OK - it leaves the list at once. Untick it again (or Show hidden on):
   it comes back.
2. Start a big copy, then Options > Updates > Install (or vShell's Update): the notice says "A copy is still running - install the
   update once it has finished" and nothing closes. After the copy, Install works.
3. vShell's Update while offline: nothing installs later by itself (the next automatic check only shows the notice).
4. A question (e.g. Delete permanently): Tab to the other button and press Enter - that button's answer is taken.

## 0.1.42: the wallpaper read right (vSuite.Core 1.0.14)
1. vShell with a different wallpaper on each monitor, and its Wallpaper Theme on. Start vXplorer with Blur / Frosted: on each monitor the
   background shows that monitor's own picture, blurred (not the one Windows has in the registry).
2. The colours follow the main monitor's picture; change it in vShell: within about 15 s vXplorer takes the new colours.
3. Wallpaper Theme off, plain colours: nothing changes from before.

## 0.1.49: updates from vsuite.vrsion.com (the bridge release)
Publish: build.cmd. dist\release\ goes to GitHub one last time (copies before 0.1.49 only know GitHub); dist\site\ is copied over the
site's root (vxplorer\latest.json, vXplorer.exe, vXplorer-setup.exe, changelog.md).
1. https://vsuite.vrsion.com/vxplorer/latest.json shows 0.1.49 in a browser; .../vxplorer/vXplorer-setup.exe downloads.
2. A copy on the version before: its update (from GitHub) installs 0.1.49 as before.
3. On 0.1.49, Options > Updates > Check now: up to date, no GitHub error; click it many times: never "limiting requests". "vSuite
   website" opens the site.
4. A newer test build copied to the site: the notice comes up listing the changes of that version AND 0.1.49; Install: it restarts on
   the new version (and goes back if it won't start, as before).
5. build.cmd pre, copied up (it goes to vxplorer\pre\): offered only with Include pre-releases on, marked as a pre-release.
6. vShell's Settings > Updates > vSuite apps shows the site's version for vXplorer, and Update / Install work from it.
