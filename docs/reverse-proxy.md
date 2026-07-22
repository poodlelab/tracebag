# HTTPS reverse proxy

Tracebag's Compose default is intentionally local-only. For a debugging session
on a remote server, leave `TRACEBAG_BIND_ADDRESS=127.0.0.1`, place a trusted
HTTPS reverse proxy on the same host, and set `TRACEBAG_PUBLIC_URL` to the final
`https://` URL.
Loopback proxies are trusted automatically. If the proxy reaches Tracebag from
another address, set `TRACEBAG_TRUSTED_PROXIES` to that exact IP or controlled
CIDR network so forwarded client IP and HTTPS scheme headers are accepted.

Authentication cookies are secure-only. HTTPS is therefore mandatory for
network access. Restrict the hostname further with a VPN, firewall, or identity
proxy where practical; the Docker socket makes this an administration-grade
service.

The reverse-proxy route can remain configured between sessions. When Tracebag
is stopped it has no upstream and should return an unavailable response; after
Tracebag starts, the same URL becomes available. A permanent route does not
keep Tracebag or its Docker connection running.

Do not use a catch-all trusted-proxy network. If an untrusted peer can connect
to Tracebag and is also trusted as a proxy, it can forge forwarded addresses to
evade per-client login limits. Keep Tracebag's own port private even when the
proxy runs on a separate container network.

## Caddy

Copy [the example Caddyfile](../deploy/reverse-proxy/caddy/Caddyfile) into your
Caddy configuration, replace `tracebag.example.com`, and point its DNS record at
the server. Caddy can obtain the TLS certificate automatically.

## Nginx

Copy [the Nginx server block](../deploy/reverse-proxy/nginx/tracebag.conf), then
replace its hostname and certificate paths. Obtain a trusted certificate before
enabling the site.

Both examples forward the original host and scheme and disable response
buffering so log and counter Server-Sent Events arrive immediately. Their
upstream remains `127.0.0.1:9090`. They also apply the same browser security
headers as the application, including CSP, frame denial, MIME sniffing
protection, referrer restrictions, permissions policy, and HSTS.

## Traefik

When Traefik runs in Docker, use
[`compose.traefik.yaml`](../deploy/reverse-proxy/traefik/compose.traefik.yaml)
as an additional Compose file. It attaches Tracebag to an existing external
proxy network and supplies an HTTPS router. Set `TRACEBAG_HOSTNAME`,
`TRACEBAG_PROXY_NETWORK`, `TRACEBAG_TRAEFIK_CERT_RESOLVER`, and a narrowly
scoped `TRACEBAG_TRUSTED_PROXIES` value before starting it. The host port remains
bound to loopback.

From a release installation directory:

```bash
curl -fsSLo compose.traefik.yaml \
  "https://raw.githubusercontent.com/poodlelab/tracebag/v${TRACEBAG_VERSION}/deploy/reverse-proxy/traefik/compose.traefik.yaml"

docker compose --env-file .env \
  -f compose.yaml -f compose.traefik.yaml \
  up -d --wait
```

For example, if the existing proxy network is an operator-controlled
`172.30.0.0/24` network:

```dotenv
TRACEBAG_HOSTNAME=tracebag.example.com
TRACEBAG_PROXY_NETWORK=proxy
TRACEBAG_TRAEFIK_CERT_RESOLVER=letsencrypt
TRACEBAG_TRUSTED_PROXIES=172.30.0.0/24
```

Use the actual narrowly scoped network range rather than copying this example
blindly.

After changing the public URL in `.env`, apply it with:

```bash
docker compose --env-file .env -f compose.yaml up -d --wait
curl --fail https://tracebag.example.com/health/live
curl --fail https://tracebag.example.com/health/ready
```

`/health/live` checks only that the web process can answer. `/health/ready`
returns HTTP 200 only when PostgreSQL, Docker Engine access, and writable
artifact storage are all healthy; it returns HTTP 503 otherwise.

End the session without removing evidence volumes:

```bash
docker compose --env-file .env -f compose.yaml down
```
