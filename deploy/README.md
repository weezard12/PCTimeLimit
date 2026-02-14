# Deploy Runbook (Ubuntu Host)

## Prerequisites

- DNS `A` record for `pctimelimit.example` pointing to this host
- Docker + Docker Compose plugin installed
- Certbot installed on host

## 1) Create certificates on host

```bash
sudo certbot certonly --standalone -d pctimelimit.example
```

Certificates are expected at:

- `/etc/letsencrypt/live/pctimelimit.example/fullchain.pem`
- `/etc/letsencrypt/live/pctimelimit.example/privkey.pem`

## 2) Configure environment

```bash
cp ../.env.example ../.env
# edit ../.env with secure values
```

## 3) Build and run

```bash
docker compose --env-file ../.env up -d --build
```

## 4) Verify

```bash
curl -I https://pctimelimit.example/health/live
curl -I https://pctimelimit.example/health/ready
```

## 5) Certificate renewal hook

Create `/etc/letsencrypt/renewal-hooks/deploy/pctimelimit-nginx-reload.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail
cd /path/to/repo/deploy
docker compose --env-file ../.env exec -T nginx nginx -s reload
```

Make executable:

```bash
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/pctimelimit-nginx-reload.sh
```
