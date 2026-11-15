@echo off
rem Double-click to build a release. Needs the .NET 10 SDK (https://dotnet.microsoft.com/download/dotnet/10.0).
rem Produces dist\vXplorer.exe (self-contained, one file: nothing else to install), its checksum, and dist\setup\vXplorer-setup.exe
rem (Inno Setup 6, installer\vXplorer.iss). dist\site\ = the update site's files (vsuite.vrsion.com - copy it over the site's root;
rem "build.cmd pre" = a pre-release, in vxplorer\pre\). Nothing goes to GitHub any more (the bridge release was 0.1.49).
cd /d "%~dp0"
rem the installer is in every release (vShell's Settings > Updates > vSuite apps installs from it); build.cmd nosetup skips it
set WANTSETUP=1
for %%a in (%*) do if /i "%%~a"=="nosetup" set WANTSETUP=
dotnet --version >nul 2>&1 || (echo .NET SDK not found. Install .NET 10 SDK first. & pause & exit /b 1)
rem the newest vSuite.Core library first (the submodule, committed if it moved) - vSuite.Core\scripts\suite-core.ps1
if not exist vSuite.Core\scripts\suite-core.ps1 git submodule update --init vSuite.Core
powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\suite-core.ps1"
if errorlevel 1 (echo. & echo BUILD STOPPED - vSuite.Core, see above. & pause & exit /b 1)
dotnet publish vXplorer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist
if errorlevel 1 (echo. & echo BUILD FAILED - copy the errors above and send them to me. & pause & exit /b 1)
echo Built: %~dp0dist\vXplorer.exe
set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
rem an older installer is never put up with this version: dist\setup is emptied first
del /q dist\setup\*.exe 2>nul
if not defined WANTSETUP (echo No installer this time - nosetup.) else if not exist "%ISCC%" (echo Inno Setup 6 not found - no installer made.) else (
  "%ISCC%" /Q installer\vXplorer.iss
  if errorlevel 1 (echo. & echo INSTALLER FAILED - see above. & pause & exit /b 1)
  echo Installer: %~dp0dist\setup\
)
rem release notes for this version, taken from CHANGELOG.md (also on the clipboard)
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\release-notes.ps1"
rem the update site's files (exe, installer, latest.json, changelog.md) in dist\site\vxplorer\ - build.cmd pre: dist\site\vxplorer\pre\
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Select-Xml -Path vXplorer.csproj -XPath '//Version').Node.InnerText"`) do set VER=%%v
set SITEPRE=
for %%a in (%*) do if /i "%%~a"=="pre" set SITEPRE=-Pre
if exist dist\site rmdir /s /q dist\site
powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\site-release.ps1" -App vXplorer -Version %VER% -Install dist\vXplorer.exe -Installer dist\setup\vXplorer-setup.exe -Notes dist\release-notes.md -Changelog CHANGELOG.md -Out dist\site %SITEPRE%
if errorlevel 1 (echo. & echo UPDATE SITE FILES FAILED - see above. & pause & exit /b 1)
rem "build.cmd upload" (with "pre" for a pre-release): dist\site\ straight onto the update site - SFTP with the vsuite-site key
rem (%USERPROFILE%\.ssh\config), latest.json last, then checked over https (vSuite.Core\scripts\site-upload.ps1)
set UPLOAD=
for %%a in (%*) do if /i "%%~a"=="upload" set UPLOAD=1
if defined UPLOAD powershell -NoProfile -ExecutionPolicy Bypass -File "vSuite.Core\scripts\site-upload.ps1" -Site dist\site
if defined UPLOAD if errorlevel 1 (echo. & echo UPLOAD FAILED - see above. & pause & exit /b 1)
echo For the update site: copy everything in %~dp0dist\site\ over the site's root (the files there are replaced).
set PAUSEIT=1
for %%a in (%*) do if /i "%%~a"=="nopause" set PAUSEIT=
if defined PAUSEIT pause
