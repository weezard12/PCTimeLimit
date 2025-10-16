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