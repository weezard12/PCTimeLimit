# PC Time Limit - .NET 10 HTTPS Architecture

This repository now uses a hard-cutover architecture:

- `PCTimeLimitServer`: ASP.NET Core API (`net10.0`) over HTTPS
- `PCTimeLimitAdmin`: WPF admin client (`net10.0-windows`) using typed `HttpClient`
- `PCTimeLimit`: WPF child client (`net10.0-windows`) using typed `HttpClient`
- `PCTimeLimitShared`: shared API contracts and constants
- `PCTimeLimitOpsCli`: command-line ops tool for server maintenance

Legacy raw TCP communication is removed.

## Security Model

- Passwords are hashed with `PasswordHasher<TUser>`.
- Admin sessions use short JWT access tokens + rotating refresh tokens.
- Child client registers once with Admin Code and then uses a persisted device token.
- Ops endpoints require `X-Ops-Key`.
- HTTPS is required in production (Nginx TLS termination on `443`).

## Local Development

1. Configure server secrets in `PCTimeLimitServer/appsettings.Development.json`.
2. Start server:

```bash
cd PCTimeLimitServer
dotnet run
```

3. Start admin app:

```bash
cd PCTimeLimitAdmin
dotnet run
```

4. Start child app:

```bash
cd PCTimeLimit
dotnet run
```

## Production Deployment (Ubuntu + Docker + Nginx)

1. Ensure DNS points your domain to the Ubuntu host.
2. Copy `.env.example` to `.env` and set secure values.
3. Place certificates on host via Certbot (`/etc/letsencrypt`).
4. Build and run:

```bash
cd deploy
docker compose --env-file ../.env up -d --build
```

5. Verify health:

- `https://<your-domain>/health/live`
- `https://<your-domain>/health/ready`

## Ops CLI

Set:

- `PCTIMELIMIT_OPS_BASEURL`
- `PCTIMELIMIT_OPS_KEY`

Then run:

```bash
dotnet run --project PCTimeLimitOpsCli -- status
dotnet run --project PCTimeLimitOpsCli -- create-admin parent1 StrongPassword123
dotnet run --project PCTimeLimitOpsCli -- list-users
dotnet run --project PCTimeLimitOpsCli -- list-computers
```

## Publish Script

Use:

```bat
publish-all.bat
```

It publishes:

- Child app
- Admin app
- Ops CLI
- Server API artifacts
- MSI package
- Optional Docker image build (if Docker is available)

## Notes

- Fresh-start migration policy is active: old `accounts.json`/`computers.json` are not imported.
- Both clients must be updated together with the new server release (hard cutover).
