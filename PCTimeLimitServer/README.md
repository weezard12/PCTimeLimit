# PCTimeLimit Server Setup (Ubuntu)

This guide deploys the server to an Ubuntu VM using Docker, Nginx TLS termination, and fixed ports `80/443`.

## Architecture

- API: ASP.NET Core (`net10.0`) in container (`api`)
- Reverse proxy: Nginx in container (`nginx`)
- TLS: Certbot on host, certs mounted into Nginx (`/etc/letsencrypt`)
- DB: SQLite persisted in Docker volume (`pctimelimit_data`)

## 1. Prerequisites

- Ubuntu 22.04 or newer
- Domain DNS `A` record pointing to the VM (example: `pctimelimit.example`)
- Ports open in cloud firewall/security group: `22`, `80`, `443`

Install required packages:

```bash
sudo apt update
sudo apt install -y ca-certificates curl gnupg certbot
```

Install Docker + compose plugin:

```bash
sudo apt install -y docker.io docker-compose-plugin
sudo systemctl enable --now docker
sudo usermod -aG docker $USER
```

Log out/in once so group changes apply.

## 2. Clone and Prepare

```bash
git clone <your-repo-url> /opt/pctimelimit
cd /opt/pctimelimit
cp .env.example .env
```

Edit `.env` and set strong values:

- `PCTIMELIMIT_JWT_SIGNING_KEY`
- `PCTIMELIMIT_OPS_KEY`
- `PCTIMELIMIT_JWT_ISSUER`
- `PCTIMELIMIT_JWT_AUDIENCE`

## 3. Configure Domain in Nginx Config

Edit `deploy/nginx/nginx.conf`:

- Replace `pctimelimit.example` with your real domain in `server_name`
- Ensure certificate paths match your domain:
  - `/etc/letsencrypt/live/<domain>/fullchain.pem`
  - `/etc/letsencrypt/live/<domain>/privkey.pem`

## 4. Issue TLS Certificate on Host

Stop anything using port 80, then run:

```bash
sudo certbot certonly --standalone -d <your-domain>
```

After success, certificates exist under `/etc/letsencrypt/live/<your-domain>/`.

## 5. Start the Stack

```bash
cd /opt/pctimelimit/deploy
docker compose --env-file ../.env up -d --build
```

Check status:

```bash
docker compose ps
docker compose logs -f api
docker compose logs -f nginx
```

## 6. Verify Health

```bash
curl -i https://<your-domain>/health/live
curl -i https://<your-domain>/health/ready
```

Expected: `HTTP/1.1 200 OK`.

## 7. Create First Admin Account (Ops CLI)

From repo root (`/opt/pctimelimit`):

```bash
export PCTIMELIMIT_OPS_BASEURL="https://<your-domain>"
export PCTIMELIMIT_OPS_KEY="<same value as .env>"
dotnet run --project PCTimeLimitOpsCli -- create-admin parent1 StrongPassword123!
```

Save the returned `Admin Code`; child devices use it for initial pairing.

## 8. Certificate Auto-Renew

Create renewal hook:

```bash
sudo tee /etc/letsencrypt/renewal-hooks/deploy/pctimelimit-nginx-reload.sh > /dev/null <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
cd /opt/pctimelimit/deploy
docker compose --env-file ../.env exec -T nginx nginx -s reload
EOF
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/pctimelimit-nginx-reload.sh
```

Test renewal dry-run:

```bash
sudo certbot renew --dry-run
```

## 9. Upgrades

```bash
cd /opt/pctimelimit
git pull
cd deploy
docker compose --env-file ../.env up -d --build
```

EF migrations are applied automatically on API startup.

## 10. Backup and Restore (SQLite Volume)

Find the volume:

```bash
docker volume ls | grep pctimelimit_data
```

Backup:

```bash
docker run --rm -v <volume_name>:/data -v $PWD:/backup alpine sh -c "tar czf /backup/pctimelimit-db-backup.tgz -C /data ."
```

Restore:

```bash
docker run --rm -v <volume_name>:/data -v $PWD:/backup alpine sh -c "rm -rf /data/* && tar xzf /backup/pctimelimit-db-backup.tgz -C /data"
```

## Troubleshooting

- `401 Unauthorized` from ops endpoints:
  - Check `X-Ops-Key` value and `PCTIMELIMIT_OPS_KEY`.
- `502 Bad Gateway` from Nginx:
  - Check API container logs: `docker compose logs api`.
- TLS errors:
  - Verify cert paths and domain in `deploy/nginx/nginx.conf`.
- Port conflicts:
  - Check host process usage: `sudo ss -tulpn | grep -E ':80|:443'`.
