@echo off
setlocal EnableDelayedExpansion

pushd "%~dp0"

echo ===========================
echo Publishing all projects (.NET 10)...
echo ===========================

set "BASEDIR=%~dp0publish"
set "LOGDIR=%BASEDIR%\logs"

if not exist "%BASEDIR%" mkdir "%BASEDIR%"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"

set "CHILD_PROJ=%~dp0PCTimeLimit\PCTimeLimit.csproj"
set "ADMIN_PROJ=%~dp0PCTimeLimitAdmin\PCTimeLimitAdmin.csproj"
set "SERVER_PROJ=%~dp0PCTimeLimitServer\PCTimeLimitServer.csproj"
set "OPSCLI_PROJ=%~dp0PCTimeLimitOpsCli\PCTimeLimitOpsCli.csproj"
set "MSI_PROJ=%~dp0PCTimeLimitPackage\PCTimeLimitPackage.wixproj"

REM Publish Child Client (Windows)
echo Publishing PCTimeLimit (child client)...
dotnet publish "%CHILD_PROJ%" -c Release -r win-x64 --self-contained true ^
 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
 -o "%BASEDIR%\PCTimeLimit"
set "PUBLISH_EXIT=%errorlevel%"
if not "%PUBLISH_EXIT%"=="0" call :fail_exit "Failed to publish PCTimeLimit (child client)" "" %PUBLISH_EXIT%

REM Build MSI installer for child client
set "MSI_LOG=%LOGDIR%\msi-build.log"
echo Building PCTimeLimit MSI installer... (log: %MSI_LOG%)
echo --- MSI build start --- > "%MSI_LOG%"
dotnet build "%MSI_PROJ%" -c Release >> "%MSI_LOG%" 2>&1
set "MSI_EXIT=%errorlevel%"
if not "%MSI_EXIT%"=="0" (
    call :fail_exit "Failed to build MSI installer" "%MSI_LOG%" %MSI_EXIT%
) else (
    set "MSI_SOURCE=%~dp0PCTimeLimitPackage\bin\Release\en-US\PCTimeLimitChild.msi"
    set "MSI_TARGET=%BASEDIR%\PCTimeLimitChild.msi"
    if exist "!MSI_SOURCE!" (
        copy /Y "!MSI_SOURCE!" "!MSI_TARGET!" >nul
        echo MSI copied to "!MSI_TARGET!"
    )
)

REM Publish Admin App (Windows)
echo Publishing PCTimeLimitAdmin...
dotnet publish "%ADMIN_PROJ%" -c Release -r win-x64 --self-contained true ^
 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true ^
 -o "%BASEDIR%\PCTimeLimitAdmin"
set "ADMIN_EXIT=%errorlevel%"
if not "%ADMIN_EXIT%"=="0" call :fail_exit "Failed to publish PCTimeLimitAdmin" "" %ADMIN_EXIT%

REM Publish Ops CLI (cross-platform)
echo Publishing PCTimeLimitOpsCli...
dotnet publish "%OPSCLI_PROJ%" -c Release -o "%BASEDIR%\PCTimeLimitOpsCli"
set "OPS_EXIT=%errorlevel%"
if not "%OPS_EXIT%"=="0" call :fail_exit "Failed to publish PCTimeLimitOpsCli" "" %OPS_EXIT%

REM Publish Server API binaries for diagnostics/fallback
echo Publishing PCTimeLimitServer API binaries...
dotnet publish "%SERVER_PROJ%" -c Release -o "%BASEDIR%\PCTimeLimitServerApi"
set "SERVER_EXIT=%errorlevel%"
if not "%SERVER_EXIT%"=="0" call :fail_exit "Failed to publish PCTimeLimitServer API" "" %SERVER_EXIT%

REM Optional Docker image build for production deployment
where docker >nul 2>&1
if "%errorlevel%"=="0" (
    echo Building Docker images with deploy\docker-compose.yml...
    docker compose -f "%~dp0deploy\docker-compose.yml" build
    if not "%errorlevel%"=="0" (
        echo WARNING: Docker image build failed. Check Docker environment and compose file.
    )
) else (
    echo Docker not found. Skipping container build step.
)

echo ===========================
echo All publish steps completed.
echo Output: %BASEDIR%
echo ===========================

popd
endlocal
goto :eof

:fail_exit
set "FAIL_MESSAGE=%~1"
set "FAIL_LOG=%~2"
set "FAIL_CODE=%~3"
echo %FAIL_MESSAGE%
echo Exit code: %FAIL_CODE%
if not "%FAIL_LOG%"=="" (
    if exist "%FAIL_LOG%" (
        echo ---- Begin log "%FAIL_LOG%" ----
        type "%FAIL_LOG%"
        echo ---- End log ----
    ) else (
        echo Log file not found: "%FAIL_LOG%"
    )
)
popd
exit /b %FAIL_CODE%
