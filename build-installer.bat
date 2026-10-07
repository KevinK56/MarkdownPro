@echo off
setlocal enabledelayedexpansion

echo ============================================================================
echo   Markdown Pro — Windows Installer Build Script (Inno Setup 6)
echo ============================================================================
echo.

set "VERSION=%~1"
if "%VERSION%"=="" (
    for /f "delims=" %%V in ('powershell -NoProfile -Command "$d = (Get-Date).ToUniversalTime().ToString('yyyy.MM.dd'); $c = try { [int](git rev-list --count HEAD 2>$null) } catch { 1 }; if (-not $c) { $c = 1 }; Write-Output \"$d.$c\""') do set "VERSION=%%V"
)

set "PROJECT=MarkdownPro\MarkdownPro\MarkdownPro.csproj"
set "BUILD_OUT=MarkdownPro\MarkdownPro\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
set "PUBLISH_DIR=publish\win-x64"
set "OUTPUT_DIR=installer_output"

if not exist "MarkdownPro\MarkdownPro\Assets\app.ico" (
    echo [1/4] Generating application icon...
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0generate-icon.ps1"
)

echo [2/4] Building self-contained WinUI 3 application (v%VERSION%)...
dotnet build "%PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:Platform=x64 ^
    -p:WindowsPackageType=None ^
    -p:WindowsAppSDKSelfContained=true ^
    -p:PublishTrimmed=false ^
    -p:Version=%VERSION% ^
    -p:AssemblyVersion=%VERSION% ^
    -p:FileVersion=%VERSION%

if errorlevel 1 (
    echo.
    echo [ERROR] dotnet build failed!
    exit /b 1
)

echo [3/4] Staging complete WinUI 3 output (including .pri, .xbf, and Assets\Web) to %PUBLISH_DIR%...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
mkdir "%PUBLISH_DIR%"
xcopy "%BUILD_OUT%\*" "%PUBLISH_DIR%\" /E /I /H /Y >nul

if errorlevel 1 (
    echo.
    echo [ERROR] Failed to stage build output!
    exit /b 1
)

echo [4/4] Locating Inno Setup 6 Compiler (ISCC.exe)...
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

