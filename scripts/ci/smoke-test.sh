#!/usr/bin/env bash
# Smoke test for a built Mangarr image (CI, .github/workflows/docker.yml): start it with an empty
# /config and wait until GET /api/v1/system/status answers 200 with the API key Mangarr generated
# into /config/config.xml. With an expected version, the reported version must match it.
#   scripts/ci/smoke-test.sh <image> [expected-version]
set -euo pipefail

IMAGE="${1:?image, e.g. mangarr:smoke}"
EXPECTED="${2:-}"
NAME="mangarr-smoke-$$"
PORT="${SMOKE_PORT:-8787}"
CONFIG="$(mktemp -d)"
STATUS="$(mktemp)"

# shellcheck disable=SC2329  # invoked by the EXIT trap
cleanup() { docker rm -f "$NAME" >/dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --name "$NAME" -p "127.0.0.1:$PORT:8787" \
  -e PUID="$(id -u)" -e PGID="$(id -g)" -e TZ=Etc/UTC \
  -v "$CONFIG:/config" "$IMAGE" >/dev/null

key=""
for _ in $(seq 1 60); do
  if [ -z "$key" ]; then
    key="$(docker exec "$NAME" sed -n 's:.*<ApiKey>\(.*\)</ApiKey>.*:\1:p' /config/config.xml 2>/dev/null || true)"
  fi

  if [ -n "$key" ]; then
    code="$(curl -s -o "$STATUS" -w '%{http_code}' -H "X-Api-Key: $key" "http://127.0.0.1:$PORT/api/v1/system/status" || true)"

    if [ "$code" = "200" ]; then
      version="$(sed -n 's|.*"version": *"\([^"]*\)".*|\1|p' "$STATUS" | head -n 1)"
      echo "system/status 200, version ${version:-?}"

      if [ -n "$EXPECTED" ] && [ "$version" != "$EXPECTED" ]; then
        echo "expected version $EXPECTED" >&2
        exit 1
      fi

      exit 0
    fi
  fi

  sleep 5
done

echo "Mangarr did not answer /api/v1/system/status with 200 within 5 minutes (api key found: $([ -n "$key" ] && echo yes || echo no))" >&2
docker logs --tail 200 "$NAME" >&2 || true
exit 1
