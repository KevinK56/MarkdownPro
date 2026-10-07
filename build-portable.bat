@echo off
setlocal enabledelayedexpansion

echo ============================================================================
echo   Markdown Pro — Portable Build Script (Self-Contained WinUI 3 win-x64)
echo ============================================================================
echo.

set "VERSION=1.0.0"
if not "%~1"=="" set "VERSION=%~1"

set "PROJECT=MarkdownPro\MarkdownPro\MarkdownPro.csproj"
set "PUBLISH_DIR=publish\MarkdownPro-Portable-win-x64"
set "ZIP_OUTPUT=publish\MarkdownPro-Portable-v%VERSION%-win-x64.zip"

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

copy /y "LICENSE" "%PUBLISH_DIR%\LICENSE" >nul
copy /y "README.md" "%PUBLISH_DIR%\README.md" >nul

echo [3/3] Creating Portable ZIP archive: %ZIP_OUTPUT%...
if exist "%ZIP_OUTPUT%" del /f /q "%ZIP_OUTPUT%"
powershell -NoProfile -Command "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_OUTPUT%' -Force"

echo.
echo ============================================================================
echo   SUCCESS!
echo   Portable Folder : %PUBLISH_DIR%
echo   Portable ZIP    : %ZIP_OUTPUT%
echo ============================================================================
exit /b 0

