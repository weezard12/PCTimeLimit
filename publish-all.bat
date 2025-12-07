@echo off
setlocal

REM Always run relative to this script location
pushd "%~dp0"

echo ===========================
echo Publishing all projects...
echo ===========================

REM Define base output folder relative to this .bat file
set "BASEDIR=%~dp0publish"

REM Ensure publish folder exists
if not exist "%BASEDIR%" mkdir "%BASEDIR%"

REM Project paths
set "CHILD_PROJ=%~dp0PCTimeLimit\PCTimeLimit.csproj"
set "ADMIN_PROJ=%~dp0PCTimeLimitAdmin\PCTimeLimitAdmin.csproj"
set "SERVER_PROJ=%~dp0PCTimeLimitServer\PCTimeLimitServer.csproj"
set "MSI_PROJ=%~dp0PCTimeLimitPackage\PCTimeLimitPackage.wixproj"

REM Publish Child Client (Windows)
echo Publishing PCTimeLimit (child client)...
dotnet publish "%CHILD_PROJ%" -c Release -r win-x64 --self-contained true ^
 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
 -o "%BASEDIR%\PCTimeLimit"
set "PUBLISH_EXIT=%errorlevel%"
echo Child publish exit code: %PUBLISH_EXIT%
if not "%PUBLISH_EXIT%"=="0" (
    echo Failed to publish PCTimeLimit (child client)
    popd
    exit /b %PUBLISH_EXIT%
)
echo Child publish finished.

REM Build MSI installer for child client (requires WiX build tools)
echo Building PCTimeLimit MSI installer...
dotnet build "%MSI_PROJ%" -c Release
set "MSI_EXIT=%errorlevel%"
echo MSI build exit code: %MSI_EXIT%
if not "%MSI_EXIT%"=="0" (
    echo Failed to build MSI installer
    popd
    exit /b %MSI_EXIT%
) else (
    set "MSI_SOURCE=%~dp0PCTimeLimitPackage\bin\Release\en-us\PCTimeLimitChild.msi"
    set "MSI_TARGET=%BASEDIR%\PCTimeLimitChild.msi"
    if exist "%MSI_SOURCE%" (
        copy /Y "%MSI_SOURCE%" "%MSI_TARGET%" >nul
        echo MSI copied to "%MSI_TARGET%"
    ) else (
        echo MSI build finished but file not found at "%MSI_SOURCE%"
    )
)
echo MSI build step finished.

REM Publish Admin App (Windows)
echo Publishing PCTimeLimitAdmin...
dotnet publish "%ADMIN_PROJ%" -c Release -r win-x64 --self-contained true ^
 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
 -o "%BASEDIR%\PCTimeLimitAdmin"
set "ADMIN_EXIT=%errorlevel%"
echo Admin publish exit code: %ADMIN_EXIT%
if not "%ADMIN_EXIT%"=="0" (
    echo Failed to publish PCTimeLimitAdmin
    popd
    exit /b %ADMIN_EXIT%
)
echo Admin publish finished.

REM Publish Server (Linux Ubuntu)
echo Publishing PCTimeLimitServer (Linux)...
dotnet publish "%SERVER_PROJ%" -c Release -r linux-x64 --self-contained true ^
 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
 -o "%BASEDIR%\PCTimeLimitServer"
set "SERVER_EXIT=%errorlevel%"
echo Server publish exit code: %SERVER_EXIT%
if not "%SERVER_EXIT%"=="0" (
    echo Failed to publish PCTimeLimitServer (Linux)
    popd
    exit /b %SERVER_EXIT%
)
echo Server publish finished.

echo ===========================
echo All projects published successfully!
echo Output: %BASEDIR%
echo ===========================
pause
popd
endlocal
