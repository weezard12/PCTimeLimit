# PCTimeLimit Admin (WPF, .NET 10)

Parent control panel for managing child computers through the HTTPS API.

## Features

- Admin account create/login
- Secure server communication via HTTPS
- Token-based auth (access + refresh)
- Per-computer controls:
  - Daily time limit
  - Allowed usage JSON schedule
  - Queue reset
  - Queue force lockout

## API Endpoint Configuration

The app resolves API base URL in this order:

1. Environment variable `PCTIMELIMIT_API_BASEURL`
2. `appsettings.json` -> `Api:BaseUrl`
3. Built-in default (`https://pctimelimit.example`)

HTTPS is enforced for endpoint values.

## Session Security

- Refresh token is stored with DPAPI (`CurrentUser`) in `%APPDATA%\PCTimeLimitAdmin\session.json`.
- Access token is kept in memory and refreshed automatically.

## Run

```bash
cd PCTimeLimitAdmin
dotnet run
```

## Requirements

- PCTimeLimitServer API running and reachable over HTTPS
- Valid admin account
- Windows desktop
