@echo off
setlocal

echo ===========================
echo Publishing all projects...
echo ===========================

REM Define base output folder relative to this .bat file
set BASEDIR=%~dp0publish

REM Ensure publish folder exists
if not exist "%BASEDIR%" mkdir "%BASEDIR%"

REM Publish Child Client (Windows)
echo Publishing PCTimeLimit (child client)...
dotnet publish PCTimeLimit\PCTimeLimit.csproj -c Release -r win-x64 --self-contained true ^
 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
 -o "%BASEDIR%\PCTimeLimit"
if %errorlevel% neq 0 (
    echo Failed to publish PCTimeLimit (child client)
    echo /b %errorlevel%
)

REM Build MSI installer for child client (requires WiX build tools)
echo Building PCTimeLimit MSI installer...
dotnet build PCTimeLimitPackage\PCTimeLimitPackage.wixproj -c Release
if %errorlevel% neq 0 (
    echo Failed to build MSI installer
    echo /b %errorlevel%
) else (
    set MSI_SOURCE="PCTimeLimitPackage\bin\Release\en-us\PCTimeLimitChild.msi"
    set MSI_TARGET="%BASEDIR%\PCTimeLimitChild.msi"
    if exist %MSI_SOURCE% (
        copy /Y %MSI_SOURCE% %MSI_TARGET% >nul
        echo MSI copied to %MSI_TARGET%
    ) else (
        echo MSI build finished but file not found at %MSI_SOURCE%
    )
)

REM Publish Admin App (Windows)
echo Publishing PCTimeLimitAdmin...
dotnet publish PCTimeLimitAdmin\PCTimeLimitAdmin.csproj -c Release -r win-x64 --self-contained true ^
 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
 -o "%BASEDIR%\PCTimeLimitAdmin"
if %errorlevel% neq 0 (
    echo Failed to publish PCTimeLimitAdmin
    echo /b %errorlevel%
)

REM Publish Server (Linux Ubuntu)
echo Publishing PCTimeLimitServer (Linux)...
dotnet publish PCTimeLimitServer\PCTimeLimitServer.csproj -c Release -r linux-x64 --self-contained true ^
 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
 -o "%BASEDIR%\PCTimeLimitServer"
if %errorlevel% neq 0 (
    echo Failed to publish PCTimeLimitServer (Linux)
    echo /b %errorlevel%
)

echo ===========================
echo All projects published successfully!
echo Output: %BASEDIR%
echo ===========================
pause
endlocal
