@echo off
echo ========================================
echo PC Time Limit HTTPS System Test
echo ========================================
echo.
echo 1. Start the ASP.NET server:
echo    cd PCTimeLimitServer
echo    dotnet run
echo.
echo 2. Create an admin account (choose one):
echo    a) In PCTimeLimitAdmin login window, click Create Account
echo    b) Or use Ops CLI:
echo       set PCTIMELIMIT_OPS_BASEURL=https://your-domain
echo       set PCTIMELIMIT_OPS_KEY=your_ops_key
echo       dotnet run --project PCTimeLimitOpsCli -- create-admin admin password123
echo.
echo 3. Start the admin client:
echo    cd PCTimeLimitAdmin
echo    dotnet run
echo.
echo 4. Start a child client:
echo    cd PCTimeLimit
echo    dotnet run
echo.
echo 5. Use the admin app to manage the child computer.
echo.
echo Allowed Usage JSON example:
echo {
echo   "monday": [{ "start": "08:00", "end": "15:00" }],
echo   "tuesday": [{ "start": "08:00", "end": "15:00" }],
echo   "wednesday": [{ "start": "08:00", "end": "15:00" }],
echo   "thursday": [{ "start": "08:00", "end": "15:00" }],
echo   "friday": [{ "start": "08:00", "end": "15:00" }],
echo   "saturday": [],
echo   "sunday": []
echo }
echo.
echo Press any key to continue...
pause > nul
