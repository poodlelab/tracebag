# Calling the Tracebag API

Tracebag uses the same cookie authentication and CSRF protection for the web UI
and direct API clients. A successful login returns a `csrfToken` and sets the
authentication cookie.

This example logs in, keeps the cookie in a temporary file, and calls an
authenticated mutation:

```bash
tracebag_url=http://localhost:9090
cookie_jar="$(mktemp)"
read -rsp "Tracebag admin password: " admin_password; echo
login_payload="$(jq -nc --arg password "${admin_password}" \
  '{userName:"admin",password:$password}')"
unset admin_password

login_response="$(curl --fail --silent --show-error \
  --cookie-jar "${cookie_jar}" \
  --header 'Content-Type: application/json' \
  --data "${login_payload}" \
  "${tracebag_url}/api/auth/login")"
unset login_payload
csrf_token="$(printf '%s' "${login_response}" | jq -r '.csrfToken')"

curl --fail --silent --show-error \
  --cookie "${cookie_jar}" \
  --header "X-CSRF-TOKEN: ${csrf_token}" \
  --request POST \
  "${tracebag_url}/api/auth/logout"

rm -f "${cookie_jar}"
```

Send the cookie on authenticated reads. Also send `X-CSRF-TOKEN` on every
authenticated `POST`, `PUT`, `PATCH`, or `DELETE` request. Login is the only
mutation exempt from the header. A current token can also be read from
`GET /api/auth/csrf` while the cookie is valid.

Authentication is intentionally cookie-based; Tracebag 0.1 does not issue API
keys or bearer tokens. Keep client scripts on a trusted host and do not place
passwords, cookies, or CSRF tokens in source control.
