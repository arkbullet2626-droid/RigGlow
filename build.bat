@echo off
setlocal
cd /d "%~dp0"
set VERSION=1.0.0
if not "%~1"=="" set VERSION=%~1
title Building RigGlow %VERSION%
echo ==== RigGlow %VERSION% build %date% %time% ==== > build.log

where dotnet >nul 2>&1
if errorlevel 1 (
  echo The .NET SDK was not found. Install it, then run this again:
  echo     winget install Microsoft.DotNet.SDK.8
  echo DOTNET NOT FOUND >> build.log
  pause
  exit /b 1
)
dotnet --list-sdks >> build.log 2>&1

if exist publish rmdir /s /q publish
if exist release rmdir /s /q release
mkdir release

echo [1/4] Compiling RigGlow %VERSION%...
dotnet publish src\RigGlow\RigGlow.csproj -c Release -r win-x64 --self-contained true -p:Version=%VERSION% -o publish >> build.log 2>&1
if errorlevel 1 goto fail

echo [2/4] Making the portable zip...
powershell -NoProfile -Command "Compress-Archive -Path 'publish\*' -DestinationPath 'release\RigGlow-%VERSION%-portable.zip' -Force" >> build.log 2>&1
if errorlevel 1 goto fail

echo [3/4] Making the installer...
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC goto noinno
"%ISCC%" /Q /DAppVersion=%VERSION% installer\RigGlow.iss >> build.log 2>&1
if errorlevel 1 goto fail
copy /y "release\RigGlow-%VERSION%-Setup.exe" "release\RigGlow-Setup.exe" >nul
goto sums

:noinno
echo     Inno Setup isn't installed, so only the portable zip was made.
echo     To also make the installer:  winget install JRSoftware.InnoSetup
echo INNO SETUP NOT FOUND, installer skipped >> build.log

:sums
echo [4/4] Writing checksums...
powershell -NoProfile -Command "Get-ChildItem release -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } | Set-Content release\SHA256SUMS.txt" >> build.log 2>&1
echo BUILD OK >> build.log
echo.
echo Done! Your files are in the "release" folder.
start "" explorer "%~dp0release"
pause
exit /b 0

:fail
echo BUILD FAILED >> build.log
echo.
echo BUILD FAILED. Tell Claude "the build failed" - it can read build.log.
findstr /i /c:" error " build.log
pause
exit /b 1
