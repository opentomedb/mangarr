#!/bin/bash
# Mangarr container entrypoint — mirrors the Servarr/LSIO model:
#   1. Drop from root to PUID:PGID (nobody:users) so files match the rest of the
#      *arr stack instead of being root-owned.
#   2. Supervise the app like s6: relaunch in place on an app-initiated exit (the
#      in-app Settings -> Restart, or a crash), but stop cleanly when the container
#      itself is told to stop/restart (SIGTERM/SIGINT from the Unraid button or
#      `docker stop`). This is why the container can be `restart: no` (the Unraid
#      convention for managed containers) and still survive an in-app restart.

PUID=${PUID:-99}
PGID=${PGID:-100}
UMASK=${UMASK:-002}

umask "$UMASK" 2>/dev/null || true

# Resolve the data dir from -data=... (default /config)
DATA_DIR="/config"
for arg in "$@"; do
  case "$arg" in
    -data=*|--data=*|/data=*) DATA_DIR="${arg#*=}" ;;
  esac
done

echo "================================"
echo " Mangarr entrypoint"
echo " PUID=$PUID  PGID=$PGID  UMASK=$(umask)  DATA_DIR=$DATA_DIR"
echo "================================"

RUNNER=""
if [ "$(id -u)" -eq 0 ]; then
  mkdir -p "$DATA_DIR"

  # One-time ownership migration: only recurse if the dir isn't already PUID-owned
  # (keeps subsequent starts fast even if MediaCover grows).
  cur_uid=$(stat -c '%u' "$DATA_DIR" 2>/dev/null || echo -1)
  if [ "$cur_uid" != "$PUID" ]; then
    echo "Migrating ownership of $DATA_DIR -> $PUID:$PGID (one-time)..."
    chown -R "$PUID:$PGID" "$DATA_DIR" 2>/dev/null || echo "Warning: could not chown $DATA_DIR"
  fi

  export HOME="$DATA_DIR"
  RUNNER="gosu $PUID:$PGID"
  echo "Running as $PUID:$PGID"
else
  echo "Not root (running as $(id -u):$(id -g)); no privilege drop"
fi

# --- supervisor loop ---
# Stop ONLY when the container is signalled (Stop/Restart button, docker stop).
# Any other exit (in-app Restart, or a crash) relaunches in place, like s6.
# Crash-loop backoff (beta readiness 2026-09-28, D7): an exit within 30 s of the
# launch waits before the relaunch -- 5 s, then doubling up to 60 s while it keeps
# happening -- so a bad config or a corrupt DB can't spin at full speed and flood
# the docker log. A run that lasted 30 s or more (an in-app Restart) relaunches at
# once and resets the delay. The wait is interruptible: Stop still stops.
child=""
STOP=0
term() { [ -n "$child" ] && kill -TERM "$child" 2>/dev/null; STOP=1; }
trap term TERM INT

QUICK_EXIT=30
delay=0

while :; do
  started=$(date +%s)
  $RUNNER "$@" &
  child=$!
  if wait "$child"; then code=0; else code=$?; fi

  if [ "$STOP" = 1 ]; then
    exit "$code"
  fi

  ran=$(( $(date +%s) - started ))

  if [ "$ran" -lt "$QUICK_EXIT" ]; then
    if [ "$delay" -eq 0 ]; then delay=5; else delay=$(( delay * 2 )); fi
    if [ "$delay" -gt 60 ]; then delay=60; fi

    echo "Mangarr exited ($code) ${ran}s after starting; relaunching in ${delay}s (use the container Stop button to stop)..."
    sleep "$delay" &
    child=$!
    wait "$child"

    if [ "$STOP" = 1 ]; then
      exit "$code"
    fi
  else
    delay=0
    echo "Mangarr exited ($code); relaunching in place (use the container Stop button to stop)..."
  fi
done
