@echo off
setlocal enabledelayedexpansion

echo ============================================================================
echo   Markdown Pro — Windows Installer Build Script (Inno Setup 6)
echo ============================================================================
echo.

set "VERSION=1.0.0"
if not "%~1"=="" set "VERSION=%~1"

set "PROJECT=MarkdownPro\MarkdownPro\MarkdownPro.csproj"
set "PUBLISH_DIR=publish\win-x64"
set "OUTPUT_DIR=installer_output"

if not exist "MarkdownPro\MarkdownPro\Assets\app.ico" (
    echo [1/3] Generating application icon...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0generate-icon.ps1"
)

echo [2/3] Publishing self-contained WinUI 3 application (v%VERSION%)...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish "%PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:Platform=x64 ^
    -p:WindowsPackageType=None ^
    -p:WindowsAppSDKSelfContained=true ^
    -p:PublishTrimmed=false ^
    -p:Version=%VERSION% ^
    -p:AssemblyVersion=%VERSION%.0 ^
    -p:FileVersion=%VERSION%.0 ^
    -o "%PUBLISH_DIR%"

if errorlevel 1 (
    echo.
    echo [ERROR] dotnet publish failed!
    exit /b 1
)

echo [3/3] Locating Inno Setup 6 Compiler (ISCC.exe)...
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"

if "%ISCC%"=="" (
    for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do set "ISCC=%%I"
)

if "%ISCC%"=="" (
    echo.
    echo [WARNING] Inno Setup 6 ^(ISCC.exe^) was not found on this machine.
    echo           Install Inno Setup 6 from https://jrsoftware.org/isdl.php
    echo           or run via winget: winget install JRSoftware.InnoSetup
    exit /b 1
)

echo Compiling installer using: "%ISCC%"
"%ISCC%" "/DMyAppVersion=%VERSION%" "/DSourceDir=%PUBLISH_DIR%" "/DOutputDir=%OUTPUT_DIR%" "installer.iss"

if errorlevel 1 (
    echo.
    echo [ERROR] Inno Setup compilation failed!
    exit /b 1
)

echo.
echo ============================================================================
echo   SUCCESS!
echo   Installer created: %OUTPUT_DIR%\MarkdownPro-Setup-v%VERSION%.exe
echo ============================================================================
exit /b 0

