# vXplorer - notes for Claude Code

vXplorer is a Windows 11 file manager (C# / .NET 10, WPF) in vShell's look: it reads vShell's theme from
`%APPDATA%\vShell\settings.json` and follows it live. Owner: V (Steven). Repo: https://github.com/version-au/vXplorer (branch `main`, GPL-3.0).
vShell itself is at https://github.com/version-au/vShell - read its `src/Ui/Theme.cs` and
`src/Core/Settings.cs` when the theme mapping needs to change. vXplorer doesn't reference vShell's code; it copies the bits it needs.

This machine's notes (working folder, remotes): `CLAUDE.local.md` (not in git).

## The shared library: vSuite.Core (since 0.1.34)

- **The suite's design guide** is `vSuite.Core\docs\design-guide.md` (the window, theme, icon, controls, keys, words, Options, release): follow it for anything new here.
- **The changelog says which builds were which** (vSuite.Core 1.0.28): every build marks its version's heading in CHANGELOG.md " · pre-release" (`build.cmd pre`) or " · release" (a release replaces the pre-release mark) - commit CHANGELOG.md after each build. Write new headings without a mark.

- What vShell, vXplorer and vView share lives in **vSuite.Core** (https://github.com/version-au/vSuite.Core, MIT), here as the **git submodule `vSuite.Core\`** - read its CLAUDE.md. A fresh clone needs
  `git submodule update --init`. vXplorer.csproj references `vSuite.Core\vSuite.Core` and `vSuite.Core\vSuite.Shell` and keeps the
  submodule's files out of its own compile.
- From the library (namespaces `vSuite`, `vSuite.Ui`, `vSuite.Shell`, global usings in `src/GlobalUsings.cs`): `App` (Program calls
  `App.Init("vXplorer", "VXPLORER", "https://vsuite.vrsion.com")`), `Log` (queued - `Log.Flush()` before the process can die),
  `Instance`, `VShellThemeSource` (+ Wallpaper Theme), `Updater` / `UpdateCenter` / `UpdateNotice`, `Theme`, `WindowEffects`,
  `Controls.xaml` (pack://application:,,,/vSuite.Core;component/Ui/Controls.xaml), `Ask`, `AppIcon` (+ vXplorer's drawing in
  `Ui/Brand.cs`, set as `AppIcon.Drawer`), `WindowIcons`, `OptionsWindowBase` (vXplorer's `OptionsWindow` adds its pages), `ShellIcons`.
- **A change to one of those is made in vSuite.Core**, not here: commit and push it there (with its tests), then in vXplorer
  `git -C vSuite.Core pull` and commit the submodule move with vXplorer's own version bump. Notes below about those files describe
  the library's code.

## Build and run

- `dotnet build vXplorer.csproj -c Release` -> `bin\Release\net10.0-windows\vXplorer.exe`. Close a running vXplorer first.
- "Release build" = that **and** `build.cmd` 

(by full path on a network share - see `CLAUDE.local.md`): `dist\vXplorer.exe` (self-contained single file, ~155 MB) (the library is bundled into it) (+ `dist\setup\vXplorer-setup-<ver>.exe` via Inno
  Setup 6 (`installer\vXplorer.iss`, like vShell's). The installer's "replace File Explorer" task runs `vXplorer.exe
  --default-file-manager on` (so the stash / Options state stay right); uninstall runs `off` only if the keys point at the installed
  exe. `AppMutex=vXplorer.Running` (Program.cs holds it). Don't run the installer in tests - it installs into V's account.
- Releases are on the update site https://vsuite.vrsion.com (since 0.1.49 - GitHub before; 0.1.49 was the bridge, the last GitHub
  release - build.cmd makes nothing for GitHub now): build.cmd writes `dist\site\vxplorer\` (vXplorer.exe, the setup, latest.json with each file's SHA-256, changelog.md =
  CHANGELOG.md - the notice lists every version since yours; `build.cmd pre` -> `vxplorer\pre\`) via vSuite.Core's
  `scripts\site-release.ps1`; V copies `dist\site\` over the site's root by hand. Only the newest release is kept there. **Every release has the installer too**
  (V, 2026-11-03 - vShell's Settings > Updates > vSuite apps installs from it): build.cmd always makes it (`nosetup` skips it).
- Updates (vSuite.Core's `Updater`, `UpdateCenter`, `UpdateNotice` - placed over the file list in MainWindow's constructor, Options > Updates):
  download -> SHA-256 -> running exe renamed to vXplorer.old.exe, new one in place -> `vXplorer.old.exe --update-watchdog <pid> <exe>`
  -> every window Close()d (saves tabs) -> the watchdog starts the new exe and waits for `update-ok.txt` (ReportStarted, from the first
  window's ContentRendered) or rolls back. Files live in Log.Folder. Only the single-file exe can update (`ExePath` is null when
  vXplorer.dll sits beside it). **V wants the notice inside vXplorer only - not in vShell's notifications.** The notice sits over the
  file list column (not the preview, whose native previewer would cover it).
- Testing updates: `VXPLORER_UPDATE_SITE` = a site to use instead of vsuite.vrsion.com (serving vxplorer/latest.json - the scratchpad scripts below served the old GitHub list and need redoing); hooks `updatecheck` / `updateinstall` - scratchpad `tupdate-vx.ps1` (TcpListener on 127.0.0.1, -Broken for the roll-back) or the older `fakegh.ps1` (HttpListener on
  localhost, offers v9.9.9 with dist\vXplorer.exe) + a copy of dist\vXplorer.exe in a scratch folder with its own VXPLORER_DATA_DIR.
- Log: `%APPDATA%\vXplorer\vxplorer.log` (theme loads / changes and errors). Settings: `%APPDATA%\vXplorer\settings.json` (`AppSettings`).
- Test a theme without touching vShell: set `VXPLORER_VSHELL_DIR` to a folder with a test `settings.json`, start vXplorer, edit the file.
- NuGet: `Vanara.Windows.Shell` (shell icons, `ShellContextMenu`, later `ShellFileOperations`). Check Vanara's XML docs in
  `%USERPROFILE%\.nuget\packages\vanara.windows.shell.common\<ver>\lib\net10.0-windows7.0\` for exact signatures.

## House rules (as in vShell)

- **Only make a release build when V asks.** Otherwise just make the change and commit it.
- Every user-visible change: bump `<Version>` in `vXplorer.csproj` (0.1.N - next N once the current one has been given to V), add a
  section at the top of **CHANGELOG.md** (`## 0.1.N`, one short line per change - what, not how), and test steps in **TESTING.md**.
- Commit messages: `0.1.N: what changed` (short, lower-case after the colon), one commit per change set.
- Times shown to the user are **12-hour** (`Format.Stamp`).
- UI text is plain, friendly English ("vXplorer can't find …", "You don't have permission to open this folder.").

## Layout

- `src/Program.cs` - entry: app resources (`src/Ui/Controls.xaml`), settings, theme source, main window.
- `src/Core/` - `VShellThemeSource` (reads + watches vShell's settings; `VShellColours`; vShell saves the accents as `Accent` / `Spare`),
  `AppSettings`, `FileItem` + `FolderReader` (list rows), `FolderNode` (sidebar tree, lazy), `Native` (SHGetFileInfo type / display
  names, natural sort, Downloads folder), `Log`, `Paths` (`Clean` / `Same`, used everywhere via `global using static` in
  GlobalUsings.cs), `FolderWatcher` (the open folder's FileSystemWatcher + `Unseen`), `SizeCounter`, `PathSuggest`.
- **Tests: `dotnet test tests\vXplorer.Tests`** (xunit, 0.1.48) - everything in `src/Core` that has no window (paths, suggestions,
  folder sizes, settings' Normalize (internal, InternalsVisibleTo), zip paths, network paths, Format). Run them after every Core change
  and add tests with new Core code. Logic worth testing goes in Core (or a plain class), not in a MainWindow partial. vXplorer.csproj
  keeps `tests\**` out of its own compile.
- New window features: prefer a separate class that MainWindow creates and talks to (`AddressSuggest`, `OpsPanel`) over another
  MainWindow partial - MainWindow is ~5,000 lines over 20 partials already.
- `src/Ui/` - `Theme` (vShell colours -> `vx.*` resources), `WindowEffects` (DWM: dark mode, corners, outline, Mica / Acrylic, dark native
  menus), `ShellIcons` (icon loader on its own STA thread, cached per extension), `Controls.xaml` (every control's style),
  `MainWindow.xaml(.cs)` (layout, navigation, list, sidebar, address bar, live folder updates), `MainWindow.Files.cs` (file keys,
  delete, rename in place, new folder, clipboard, drag and drop, the shell's right-click menu, Properties), `MainWindow.Menu.cs` (the
  themed menu for the folder background, Paste shortcut, New file, Windows' background menu via `ShellFolder.GetViewObject<IContextMenu>`),
  `Core/ShellNew` (the New menu's types from the registry, like Explorer: PostSetup\ShellNew "Classes", each ext's ShellNew key,
  MenuText / ItemName resolved with SHLoadIndirectString), `MainWindow.Tabs.cs` + `Core/TabState` (tabs: each has its folder, Back /
  Forward stacks, saved selection and scroll; `_back` / `_forward` are the active tab's; `SwitchTo` saves the old tab's view and
  navigates; `_restoreSelected` / `_restoreScroll` are applied by the next load), `FileOps` (copy / move /
  delete / rename via Vanara's `ShellFileOperations` = IFileOperation, each on its own STA thread).

## File operations

- Everything goes through `FileOps` (Windows' copy engine: progress, conflicts, Recycle Bin, undo) - never `File.Copy` / `Directory.Move`.
  The one exception is New folder (`Directory.CreateDirectory`, so it can be renamed straight away).
- The operations panel (0.1.31): each copy / move / delete is an `Operation` (`Ui/Operations.cs`, static `Operations.All`, every window's
  `OpsPanel` shows them all). Windows' window is hidden with FOF_SILENT and the engine reports to `OpProgress`, an
  **IFileOperationProgressSink** we `Advise` (our own IFileOperation, CoCreated, wrapped by Vanara's `ShellFileOperations`): progress =
  UpdateProgress work points, files / bytes totals counted by us first (`FileOps.Count`), pause = blocking inside the sink, cancel = the sink
  returning ERROR_CANCELLED. **`IFileOperation.SetProgressDialog` is not implemented by Windows** (E_INVALIDARG) - don't try a custom
  IOperationsProgressDialog. Copies to the same drive root queue (`Operation.Gate`). NoErrorUI: failures come from Post…Item HRESULTs
  (copy engine codes 0x8027xxxx - `Operations.Reason`). Renames / undo steps are `Hidden` operations (no row, still silent).
- Popped out (`OpsPopOut`, 0.1.45): one app-wide `OpsWindow` holds an `OpsPanel(floating: true)` working with `OpsWindow.Host` (the
  last activated MainWindow); the docked panels hide and call `OpsWindow.Sync()`. Its close = fold (hide, the chip shows); the last
  MainWindow closing calls `CloseForGood` - a hidden window would keep the app running (OnLastWindowClose).
- Already there: `ConflictWindow` (Replace / Skip / Keep both per item) before `FileOps.Transfer`, which runs up to two IFileOperations
  (NoConfirmation for replace, RenameOnCollision for keep both - Windows names those "x - Copy").
- Journal: `Core/CopyJournal` in Log.Folder\unfinished while a copy / move runs (sources, dest, done top items, the file in progress). The engine posts a **folder** as copied when it's created, before its contents - so a top-level folder is marked done only when the next top-level item starts (`OpProgress.Before`);
  deleted at the end. Found at start: `OfferUnfinishedCopies` -> `ResumeJournal` deletes the half-written file (it's already full size on
  disk - size alone can't tell) and copies what's missing / different file by file (`FileOps.TransferEach`). Closing during a copy asks
  (`MayClose`); Stop keeps the journals.
- Tests: hooks `copyto` / `moveto <folder>`, `pauseop`, `resumeop`, `cancelop`, `opsdump <file>`; `VXPLORER_TEST_CONFLICT=replace|skip|both|cancel`.
- After an operation, `AfterOperation` selects what it created in the open folder (`_selectNext`) and refreshes. `_renameNext` starts a
  rename once the next load shows the item.
- A refresh during a rename is held back (`_refreshPending`) - reloading the list would throw the typing away.
- The watcher ignores hidden items while they aren't shown (`Unseen`, on the watcher's thread): a home folder's ntuser.dat.LOG files
  change every ~3 s and kept the list reloading (icons flashed). A refresh of the same folder (`_listPath`) copies unchanged items'
  Icon / Thumb (same path, IconKey, Modified) and doesn't ask for them again.
- Clipboard and drags use Explorer's formats: FileDrop + "Preferred DropEffect" (2 = cut / move, 5 = copy). A paste of a Cut empties the
  clipboard afterwards, as Explorer does.
- A drop that vXplorer carries out returns `DragDropEffects.None` to the source, so a source doing an "unoptimised move" never deletes
  the originals itself.
- File keys are handled in the window's PreviewKeyDown (`HandleFileKeys`), never while a TextBox has focus (address bar, rename).

## Theming

- Every colour / radius in a style is a **DynamicResource** `vx.*` key (`vx.Bg`, `vx.Panel`, `vx.PanelBorder`, `vx.Fg`, `vx.FgDim`, `vx.Accent`,
  `vx.Hover`, `vx.Active`, `vx.Selected`, `vx.Thumb`, `vx.Radius`, `vx.ItemRadius` …). `Theme.Apply` replaces them in the app resources,
  so a theme change re-colours everything. New controls must use these keys, never fixed colours (the close button's red is the exception).
- Panels use the `vx.Card` style (vShell widget look). Item rows use `vx.ItemRadius` (the widget radius, at most 4).
- Blur / Frosted (0.1.35; off = the plain wallpaper, 0.1.36): vSuite's `Glass` under the window's content (`WindowEffects.Look` in ApplyTheme - the wallpaper, as
  vShell's bars) and `vx.Bg` keeps vShell's alpha over it - unless Options > Look > Use the theme's opacity (`ThemeOpacity`) is off: `_blur` = that setting, solid and no layer. **No DWM backdrop any more** (Mica hid the opacity);
  the window is opaque to Windows (GlassFrameThickness 0).

## Things learned the hard way

- `Visibility.Visible` inside the window's code means the window's own `Visibility` property - write `System.Windows.Visibility.Visible`.
- XAML setters can't take `Double.NaN` for Width - use `Auto`.
- `Environment.GetFolderPath(ApplicationData)` ignores the APPDATA variable (hence `VXPLORER_VSHELL_DIR` for tests).
- **Never use Vanara's `ShellContextMenu.CreateFromItems`** (or the `ShellItem[]` constructor): its item IDs get freed again by the
  finalizer, which corrupts the heap and kills vXplorer (0xC0000374) at some later garbage collection - it showed up as "crash after any
  right-click, once the sidebar opened down to a folder". `WithShellMenu` builds the IContextMenu itself (SHParseDisplayName ->
  SHBindToParent -> GetUIObjectOf, IDs freed once in `finally`) and wraps it in `new ShellContextMenu(IContextMenu)`.
- Windows' menu for folders gets vXplorer's Pin / Unpin (`AddPinItem`, on `MenuCreated`) in place of Windows' "Pin to Quick access"
  (found by its text - don't call GetVerbForCommand on every item: some extensions crash in GetCommandString). Command ids from Vanara's
  ShowContextMenu start at 1, and the callback gets id - 1. Work done from a menu command that rebuilds things goes through
  `Dispatcher.BeginInvoke` (after the menu has gone).
- Native crash with no managed stack: `catch.ps1`-style - attach with DebugActiveProcess, write a MiniDumpWriteDump on the fatal
  exception, then `dotnet-dump analyze <dmp> -c "setthread --tid <tid>" -c "clrstack -f"`. (No cdb / WinDbg on V's PC.)
- Shell icons come back as 32-bit premultiplied DIBs: `ShellIcons.FromHBitmap` copies the bits (flipping bottom-up ones) to keep alpha;
  `CreateBitmapSourceFromHBitmap` would lose it.
- A borderless (WindowChrome) window maximised hangs off the screen by its frame: `WindowEffects.MaximizedOverhang` pads the content.
- Buttons in the title row need `WindowChrome.IsHitTestVisibleInChrome="True"` (the `vx.IconButton` style sets it).
- Coloured fringes on thin glyphs in screenshots are ClearType, not a colour bug.
- Vanara has types named like WPF's (`DragEventArgs`, `DataObject`, `IDataObject`, `MouseButtonState` …): alias the WPF ones at the top
  of files that use both (see `MainWindow.Files.cs`).
- A rename ends (as Enter) on any click outside its box: the window's PreviewMouseDown, the WM_NCLBUTTONDOWN hook for title-bar clicks, and
  Deactivated.
- Tab width is the `vx.TabWidth` resource, worked out in `UpdateTabWidths` (220 down to 90, leaving 56px of title bar free to drag).
- The icon (`Ui/AppIcon.cs`, concept "A": folder outline + vShell's three dots, folder filled with the background) is drawn from the theme:
  `Window.Icon` gets a multi-size ICO built in memory (16-24 px use a heavier variant), `AppMark` the vector. `vxplorer.ico` (the exe's
  icon) is made by `vXplorer.exe --make-icon vxplorer.ico` in vShell's default colours - rerun it and commit when the drawing changes.
- **Tests must not touch V's own vXplorer:** set `VXPLORER_DATA_DIR` (settings + log) and `VXPLORER_VSHELL_DIR` (theme) to scratch
  folders, and stop only the test's own process by its PID - never `Get-Process vXplorer | Stop-Process`, which also closes V's window.
- Views (`MainWindow.Views.cs`): Details is the GridView; the icon views swap in `View = null`, the `vx.TileItem` container style
  (MainWindow.xaml) and a `VirtualizingWrapPanel` (NuGet, MIT) with Standard virtualisation, so tiles get Loaded / Unloaded as they
  scroll - `Tile_Loaded` asks for the thumbnail, `Tile_Unloaded` lets big ones go. Thumbnails come from `ShellIcons.RequestThumb` (own
  STA thread, LIFO so what's on screen loads first).
- Icons (0.1.31): shared icons (`ext:` / `<folder>` keys) come from the type alone (SHGetFileInfo + USEFILEATTRIBUTES - no file is
  touched) and, once cached, are handed out synchronously; an item's own icon on a network drive goes to a second queue (`_slow`). Sidebar
  folders on network drives use `<folder>` (`ShellIcons.NodeKey`) - one network trip per folder was what made big shares slow. Thumbnails (1.0.15):
  2 threads ask Windows' thumbnail cache only (SIIGBF_INCACHEONLY, ~1-2 ms), misses go on to 4 threads that make them (~50 ms); both
  LIFO stacks. Own icons: 3 threads. Results reach the UI thread in batches (`Deliver` / `Drain`, ≤15 ms each, 1.0.16) - one
  BeginInvoke per icon made a 544-item folder take ~0.8 s for icons loaded in ~0.2 s. `Load` asks in display order (`_items`).
- Shell bitmaps: icons come back bottom-up and thumbnails top-down, and GetObject's header doesn't tell - `FromHBitmap` uses GetDIBits
  with a negative height to get top-down rows either way.
- Preview pane: images decoded with WIC at most the pane's size (`LoadPicture`), text via `ReadText` (128 KB), else `RequestThumb`.
  `_previewVersion` drops results for a selection that has moved on.
- Preview handlers (`Ui/PreviewHost.cs`, an HwndHost): the handler is found with AssocQueryString (ASSOCSTR_SHELLEXTENSION + IPreviewHandler's
  IID), created **out of process only** (CLSCTX_LOCAL_SERVER - prevhost.exe; never in-proc, a crash would take vXplorer down) and driven
  on its own STA thread with a Dispatcher, so a slow handler can't freeze the window. Order: thumbnail first, the handler's window is shown
  (Visibility Hidden -> Visible) once DoPreview returns. Images and TextExts keep vXplorer's own preview (in the theme).
- A handler keeps the file open: `FileOps.Starting` -> `ReleasePreview` (synchronous Unload, 3 s at most) before every operation, and also
  before Windows' menu commands that aren't read-only (`SafeVerbs`) and before a drag. No handler is loaded while `FileOps.Busy` > 0.
  If vXplorer is killed (not closed), prevhost.exe can hold the file for a while - close test windows normally when you can.
- The handler's host window is **layered** (WS_EX_LAYERED, alpha 255): with Blur / Frosted the window is glass, and handlers drawing with
  plain GDI (the font previewer) were see-through to the wallpaper. Edge's PDF handler still renders in it.
- `_items` is an ObservableCollection (`ShowItems` sorts with `CompareItems` and replaces it). Search (`MainWindow.Search.cs`) fills it
  as results come in (`InsertSorted`, from a ConcurrentQueue filled by a breadth-first walk on a worker; `_searchPump` drains it). While
  `Searching`: the watcher is ignored, `Refresh` = `RerunSearch`, `Navigate` calls `StopSearch`, Back / Esc = `ClearSearch`, and the
  Details view has the extra `_folderColumn` (never sorts - `SortColumns()` leaves it out). Creating something here (`ShowFolderAgain`)
  ends the search so the new item can be shown and renamed.
- Undo (`MainWindow.Undo.cs`): `FileOps` reports a `Result` (top-level `Pairs` old -> new, `Deleted`, `Started`); the Post… events also
  fire for everything inside folders, so only the paths asked for count. Undo of a delete finds the items through the `$I` files in
  `<drive>\$Recycle.Bin\<SID>` (original path + deletion time, newest wins) and restores them with Vanara's `RecycleBin.Restore`.
- Selection box (`MainWindow.Select.cs`): the covered items are worked out from the layout (row height / tile size and columns, measured
  from item controls on screen, plus the scroll offset), so items scrolled out of view count. SelectionChanged skips the status while
  `_banding`.
- Bringing a test window to the front: `SetForegroundWindow` is refused while V is using another window - click vXplorer's empty title
  bar instead, but only after `WindowFromPoint` confirms that point is vXplorer (not something over it).
- The window's icons are set with WM_SETICON (`SetNativeIcons`: 64 px big, 16 px small, by DPI, after WPF's Icon): WPF hands Windows only the 16 px frame, which vShell stretched (blurry pin). `_iconVersion` starts from the time so vShell re-saves the pin's icon each run.
- vShell's taskbar (0.38.161+): the window property `vShell.LiveIcon` (set in `ApplyTheme`, value = a counter bumped with every new icon,
  removed on close) makes vShell use the window's own large icon on combined and pinned buttons - otherwise it shows the exe's / pin's
  fixed icon. The counter matters: Windows can reuse an icon handle's number, and vShell compares the value to notice the change.
- Tabs (0.1.52): the strip is vSuite.Core's `TabStrip` (1.0.22; vPad and vTerm use the same) - the look, widths and arrows, the
  sliding drag (250 ms cubic ease-out), the torn-out Popup copy and a single-tab window's DragMove are the library's. `MainWindow.Tabs.cs`
  keeps `_tabs` (TabState) as the model: `SyncStrip` makes the strip match it (a `StripTab` per TabState in `_stripTabs`, following its
  Title / Icon / Path / IsActive / IsDropTarget), `_syncing` stops the echo when the strip reorders. Files dropped on a tab: its
  `StripTab.Element` (AllowDrop, Tab_DragOver / Drop). `MainWindow.TabDrag.cs` hears Picked / CloseClicked / MenuRequested / Reordered
  / MovedTo (`LetGoOfTab` + the other window's `TakeTab(t, index)`) / TornOut (`TearOut`, a new window the same size under the mouse).
- Several windows: `ShutdownMode.OnLastWindowClose`; each window unsubscribes from the theme source on close, and hands
  `FolderNode.RequestIcon` (static) to a remaining window (`ClaimSidebarIcons`). Each window saves the settings on close - last one wins.
- V may be running vXplorer from `bin\Debug` (it locks the exe): build tests into the scratchpad with `-o`, don't stop V's process.
- This PC (`MainWindow.ThisPc.cs`, `Core/DriveTile.cs`): `_current` is `ThisPcPath` (the shell's `::{20D04FE0-…}`, so ShellItem icons,
  Properties and its native menu work). Anything that assumes a folder must check `InThisPc` / use `PlaceExists`, `NameOf` (Clean() and
  Directory.* fail on it). The list card swaps `Files` for `PcList` (`ShowPcView`); a grouped ListBox: `GroupStyle.Panel` stacks the
  sections, the ListBox's `ItemsPanel` (WrapPanel) lays out the tiles in each. Drive names / space are read per drive on worker threads
  (an offline network drive must not hold the others up). WM_DEVICECHANGE (arrival / removal) reloads the drives.
- With the glass on (Blur / Frosted), DWM draws Windows' own caption buttons into it and they show through vShell's see-through
  background behind ours: `WindowEffects.SetSystemMenuStyle` takes WS_SYSMENU off while the glass is on. The window menu still opens on a
  title-bar right-click (WindowChrome does it) and Alt+Space (WndProc, SC_KEYMENU).
- Don't send Alt+Space in tests: on V's PC it's a global launcher hotkey (vShell's run box / PowerToys), not vXplorer's.
- Sidebar following (`MainWindow.Sidebar.cs`): `RevealInSidebar` picks the closest Quick access / drive root, awaits
  `FolderNode.EnsureLoaded()` a level at a time, expands, sets the node's IsSelected (Tree_SelectedItemChanged sees it's `_current` - no
  second navigation), then BringIntoView on the row's "Row" border. `_revealVersion` drops a reveal you've moved on from. Called from
  Navigate and after the drives load.
- Options (`OptionsWindow.cs`, code-built, one at a time via `OptionsWindow.Open`): each change sets an `AppSettings` field, saves, and
  calls `ApplyOptionsEverywhere` (every window: Refresh + reveal). Laid out like vShell's settings: a page list (`vx.NavItem`) and pages
  (`Page`) of cards (`Card`) whose rows have the name + a short hint on the left, the control on the right, and an (i) tooltip for the
  longer story. New options: add the field (+ Normalize), a `Switch(...)` or `Row(..., Segmented(Opt(...), ...))` on the right page, and
  where it's used. Styles: `vx.Switch` (CheckBox), `vx.Segment` (joined buttons), `vx.TextButton`; brushes `vx.Line`, `vx.Side`.
  Test hook `options <page>` opens it on a page.
- **Never switch "Open folders with vXplorer" on in a test** - it writes V's real HKCU\Software\Classes. `DefaultFileManager` takes its
  roots from fields; `vXplorer.exe --selftest-registry <report.txt>` runs on / off against HKCU\Software\vXplorerTest and deletes it.
- One process (`Core/Instance.cs`): a mutex per settings folder; a second start finds a window with the `vXplorer.Instance` property
  (same id), sends the folder in WM_COPYDATA and exits; the window opens it with `OpenWindowAt`. Tests have their own
  VXPLORER_DATA_DIR, so their id differs from V's - they never hand off to V's vXplorer.
- Look options are window resources: `vx.RowPad` (ListViewItem padding) and `vx.ListFont` (Files / tile names), defaults in Controls.xaml,
  set by `ApplyLook`. Hidden extensions: `FileItem.DisplayName` / `HidesExtension` (static `ShowExtensions`, known = HKCR has the ext);
  renaming keeps the extension (`_renameKeepsExt`).
- Network (`Core/NetworkPlaces.cs`, `MainWindow.Network.cs`): Network (`::{F02C1A0D-…}`) and a computer (`\\NAME`) are "places" like
  This PC - `IsPlace` / `InPlace` / `NormalPlace` (use them, not `IsThisPc`, wherever "not a folder" matters: no watcher, new, paste,
  search, drops, file keys, pins). Computers = `KnownComputers` (servers of mapped drives, via WNetGetConnection - instant) + network
  discovery (`Computers`: the shell's Network folder enumerated on its own STA thread - 15-40 s on V's network, and V's NAS boxes never
  appear in it, only TVs; non-`\\` items are dropped). Shares: `NetShareEnum` level 1, disk shares, no `$` ones. **V's rule: nothing is
  discovered until Network is clicked / expanded** - the sidebar's reveal only walks into Network if it's already expanded.
- Glass: vShell's Blur = DWM Mica, Frosted (BlurMode "Acrylic") = **Mica Alt** (DWMSBT_TABBEDWINDOW) - never DWM Acrylic, which shows the
  windows behind (a blue app turned a red see-through theme blue). DWMWA_CAPTION_COLOR = Theme.CaptionColor (the gradient's middle):
  with "accent colour on title bars" Windows otherwise paints its accent behind the tab strip.
- vShell 0.38.162 keeps a pinned app's last live icon (%APPDATA%\vShell\cache\live-icons) for when it's closed - nothing to do here.
- Columns (`MainWindow.Columns.cs`): `_colKey` maps each GridViewColumn to its sort key (never by position - columns come and go);
  `SortKeys` / `Headers` list all seven. Dimensions / Length: `Core/FileProps` (own IPropertyStore interop, STA thread, only for
  picture / video / audio types, only while shown). Group by = a PropertyGroupDescription on FileItem.Group, `ShowItems` sorts by
  GroupOrder first; grouped icon views use a plain WrapPanel; the selection box uses the item controls on screen when grouped.
  Per-folder view memory (`MainWindow.FolderViews.cs`) keeps view + sort + group.
- Zips (`Core/ZipFolders.cs`, `MainWindow.Zip.cs`): a path inside a zip (C:\x\a.zip\sub) is a read-only "place" - `IsPlace` includes
  `ZipFolders.IsZipPath` (disk check), `InPlace` uses `_inZip` (set in Navigate) so it costs nothing. Listing = .NET ZipArchive; icons by
  type (`RequestForType` - no file to ask). Open / Ctrl+C / drag extract to %TEMP%\vXplorer\zip\<time> first (old ones tidied at start).
  Extract all / Compress run on a worker with % in the status bar and go on the undo list as "extract" / "compress".
- Crash safety net (`Core/Session.cs`, `MainWindow.Session.cs`): `session.json` in Log.Folder holds every window's tabs (TouchSession,
  2 s debounce, from Navigate / tab changes / window open-close); Program deletes it after app.Run returns. Found at start = the last run
  ended unexpectedly: `Session.LogCrashDetails` copies the Application event log's entries (Application Error / .NET Runtime / WER)
  into vxplorer.log, and `OfferSessionBack` asks (Ask.YesNo) to reopen. Tests: hook command `crash` (FailFast), and
  `VXPLORER_TEST_ASK=yes|no` answers Ask.YesNo without showing it.
- Themed questions: `Ask.YesNo(owner, title, message, yes, no, danger)` - use it instead of MessageBox.
- Window rows: tab strip 40, toolbar 44 (both caption), content (row 2), status (row 3). The content grid has its own rows: the command
  bar (`MainWindow.CommandBar.cs`, row 0, over the list and preview columns only) and the list / preview (row 1); the sidebar spans both. The command bar's buttons call the same methods as the keys; `UpdateCommandBar` (from
  `UpdateStatus`, and WM_CLIPBOARDUPDATE via AddClipboardFormatListener) greys them. New ▾ and the background menu share `FillNewItems`.
- Address bar suggestions (`Ui/AddressSuggest.cs`, 0.1.48): a code-built `Popup` under `AddressCard` (styled like the menus), lookups
  in `Core/PathSuggest` on a worker 120 ms after typing (`_version` drops stale ones), the box keeps the focus. Rows highlight on a
  real mouse move only (the list can open under a still mouse - Enter would have picked that row). Hooks: `address <text>`,
  `addresskey Down|Up|Tab|Escape|Enter`; `dump` has an `address:` line.
- Selection size (`MainWindow.SelectionSize.cs`, 0.1.48): selected folders are counted by `SizeCounter` 250 ms after the selection
  settles, cancelled when it changes (`StopSizeCount` - also on every Load). Progress doesn't write over a `Status()` message
  (`_messageUntil`; `UpdateStatus` clears it). Not in places (This PC, Network, zips). Option `CountFolderSizes`.
- The address bar takes the click in **PreviewMouseLeftButtonDown** (the crumbs' ScrollViewer marks plain MouseLeftButtonDown handled,
  so clicks on the empty part did nothing); clicks inside a crumb Button pass through.
- Launching vXplorer from a test: `Start-Process` (ShellExecute) on the network share can open Windows' "Open File - Security Warning"
  and block - use `[Diagnostics.Process]::Start` with `UseShellExecute = $false`. Capture the window with `PrintWindow` (flag 2) rather
  than a screen copy, which needs the window in front.
- Before sending SendKeys in a test, check vXplorer is the foreground window - otherwise the keys go to whatever is in front.
- The top 84px (tab strip 40 + toolbar 44) is all `WindowChrome.CaptionHeight`: anything clickable up there needs
  `IsHitTestVisibleInChrome` (the button styles set it; the address bar sets it itself).
- vXplorer's own menus are WPF `ContextMenu`s styled in Controls.xaml (`vx.MenuRadius`, `vx.MenuGlyph`); Windows' menus (files, Show more
  options, the sidebar) are native and only follow dark / light mode.
- Shift+F10 must not count as "Shift held" for the background menu - only a Shift+right-click with the mouse goes straight to Windows' menu.
- Testing menus without a person: right-click with `mouse_event`, then move along the menu (onto the submenu's row first, then down) -
  a diagonal move or a click outside closes it.
- **Tests drive vXplorer through the test hooks, not SendKeys** (`MainWindow.TestHooks.cs`): with `VXPLORER_TEST_HOOKS=1` it takes
  commands by WM_COPYDATA (kind 0x76580002: select / undo / redo / delete / newfolder / rename / go / view / sort / dump <file>) with no
  focus needed - SendKeys and clicks need vXplorer in front and take the keyboard away from V. Scratchpad `tvx.ps1` (VxStart, Vx,
  VxDump, VxStop). Add a command when a test needs one.
- Undo of a delete restores **from the bin's own files** ($R moved back, $I deleted, SHChangeNotify), not the shell's Recycle Bin
  folder: its listing goes stale within one run (a second delete of the same item was missing). Redo: `_redo` in MainWindow.Undo.cs.
- Testing file operations without a person: start vXplorer on a scratch folder and drive it with `SendKeys` (Ctrl+Shift+N, F2, Ctrl+C / V,
  Delete), then check the folder's contents.
