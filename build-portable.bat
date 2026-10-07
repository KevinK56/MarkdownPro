@echo off
setlocal enabledelayedexpansion

echo ============================================================================
echo   Markdown Pro — Portable Build Script (Self-Contained WinUI 3 win-x64)
echo ============================================================================
echo.

set "VERSION=%~1"
if "%VERSION%"=="" (
    for /f "delims=" %%V in ('powershell -NoProfile -Command "$d = (Get-Date).ToUniversalTime().ToString('yyyy.MM.dd'); $c = try { [int](git rev-list --count HEAD 2>$null) } catch { 1 }; if (-not $c) { $c = 1 }; Write-Output \"$d.$c\""') do set "VERSION=%%V"
)

set "PROJECT=MarkdownPro\MarkdownPro\MarkdownPro.csproj"
set "BUILD_OUT=MarkdownPro\MarkdownPro\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
set "PUBLISH_DIR=publish\MarkdownPro-Portable-win-x64"
set "ZIP_OUTPUT=publish\MarkdownPro-Portable-v%VERSION%-win-x64.zip"

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

copy /y "LICENSE" "%PUBLISH_DIR%\LICENSE" >nul
copy /y "README.md" "%PUBLISH_DIR%\README.md" >nul

echo [4/4] Creating Portable ZIP archive: %ZIP_OUTPUT%...
if exist "%ZIP_OUTPUT%" del /f /q "%ZIP_OUTPUT%"
powershell -NoProfile -Command "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_OUTPUT%' -Force"

echo.
echo ============================================================================
echo   SUCCESS!
echo   Portable Folder : %PUBLISH_DIR%
echo   Portable ZIP    : %ZIP_OUTPUT%
echo ============================================================================
exit /b 0

