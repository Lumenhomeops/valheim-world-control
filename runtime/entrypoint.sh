#!/bin/sh
set -eu

: "${SERVER_NAME:?SERVER_NAME is required}"
: "${WORLD_NAME:?WORLD_NAME is required}"
: "${SERVER_PORT:=2456}"
: "${SERVER_PUBLIC:=0}"
: "${SERVER_PASSWORD_FILE:=/run/secrets/server_password}"
: "${WORLD_PLAYER_EVENTS:=false}"
: "${WORLD_PASSIVE_ENEMIES:=false}"
: "${WORLD_PRESET:=}"

if [ ! -r "$SERVER_PASSWORD_FILE" ]; then
    echo "Valheim server password file is not readable" >&2
    exit 1
fi

server_password=$(cat "$SERVER_PASSWORD_FILE")
if [ "${#server_password}" -lt 5 ]; then
    echo "Valheim server password must contain at least five characters" >&2
    exit 1
fi

if [ ! -x /opt/valheim/valheim_server.x86_64 ]; then
    echo "Valheim server payload is missing from /opt/valheim" >&2
    exit 1
fi

export SteamAppId=892970
export LD_LIBRARY_PATH="/opt/valheim/linux64${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

set --
case "$WORLD_PRESET" in
    "") ;;
    Normal|Casual|Easy|Hard|Hardcore|Immersive|Hammer)
        set -- "$@" -preset "$WORLD_PRESET"
        ;;
    *)
        echo "Unsupported Valheim world preset: $WORLD_PRESET" >&2
        exit 1
        ;;
esac

if [ "$WORLD_PLAYER_EVENTS" = "true" ]; then
    set -- "$@" -setkey playerevents
fi

if [ "$WORLD_PASSIVE_ENEMIES" = "true" ]; then
    set -- "$@" -setkey passivemobs
fi

exec /opt/valheim/valheim_server.x86_64 \
    -nographics \
    -batchmode \
    -name "$SERVER_NAME" \
    -port "$SERVER_PORT" \
    -world "$WORLD_NAME" \
    -password "$server_password" \
    -public "$SERVER_PUBLIC" \
    "$@"
