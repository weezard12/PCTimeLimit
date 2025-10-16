@echo off
echo ========================================
echo PC Time Limit System Test
echo ========================================
echo.
echo This script will help you test the PC Time Limit system.
echo.
echo 1. First, start the server by running:
echo    cd PCTimeLimitServer
echo    dotnet run
echo.
echo 2. In the server console, create an admin account:
echo    create-admin admin password123
echo.
echo 3. In another terminal, run the admin client:
echo    cd PCTimeLimitAdmin
echo    dotnet run
echo.
echo 4. In a third terminal, run a child app:
echo    cd PCTimeLimit
echo    dotnet run
echo.
echo 5. Use the admin client to manage computers and set time limits.
echo    You can also paste Allowed Usage JSON per weekday to skip counting during school hours.
echo    Example JSON:
echo    {
echo      "monday": [{ "start": "08:00", "end": "15:00" }],
echo      "tuesday": [{ "start": "08:00", "end": "15:00" }],
echo      "wednesday": [{ "start": "08:00", "end": "15:00" }],
echo      "thursday": [{ "start": "08:00", "end": "15:00" }],
echo      "friday": [{ "start": "08:00", "end": "15:00" }],
echo      "saturday": [],
echo      "sunday": []
echo    }
echo.
echo Press any key to continue...
pause > nul


