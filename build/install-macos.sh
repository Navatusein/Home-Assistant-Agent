#!/bin/bash
# Installs or updates Home Assistant Agent on a Mac (Apple silicon) from the
# GitHub release, in a terminal:
#
#   curl -fsSL https://raw.githubusercontent.com/Navatusein/Home-Assistant-Agent/main/build/install-macos.sh | bash
#
#   ... | bash -s -- --version 1.2.0     a given version instead of the latest
#   ... | bash -s -- --service           also install the background service
#
# What the disk image and the first start do by hand, in one go: the app goes
# to /Applications, the data folder is created (the password the first start
# asks for), and an update brings back what was there before: the background
# service if it was installed, the app if it was running. Downloaded by curl,
# the app carries no quarantine: Gatekeeper does not block its first start, no
# "Open Anyway" needed.
#
# Run as yourself, not with sudo: the steps that need root ask for the
# password, and the app has to start in your session.
#
# The agent's own updater runs a copy of this script (it ships in the bundle,
# Contents/Resources) as root, with the disk image it downloaded and checked:
#
#   install-macos.sh --dmg <file> [--remove-dmg] --user <name> --wait-pid <pid>
#
# It waits for the app (pid) to exit, installs, and starts the new app in that
# user's session.
set -euo pipefail

REPO="Navatusein/Home-Assistant-Agent"
APP_NAME="Home Assistant Agent"
APP="/Applications/$APP_NAME.app"
APP_EXE="$APP/Contents/MacOS/HomeAssistant.Agent"
SERVICE_EXE="$APP/Contents/MacOS/HomeAssistant.Agent.Service"
# The app's process: its path alone or followed by arguments — a bare match on
# the path would also find the service, whose name begins the same.
APP_PROCESS="^$APP_EXE( |\$)"

VERSION=""
WITH_SERVICE=0
DMG=""
REMOVE_DMG=0
TARGET_USER=""
WAIT_PID=""

fail() {
    echo "Error: $*" >&2
    exit 1
}

while [ $# -gt 0 ]; do
    case "$1" in
        --version) VERSION="${2:-}"; [ -n "$VERSION" ] || fail "--version needs a version, e.g. 1.2.0"; shift 2 ;;
        --service) WITH_SERVICE=1; shift ;;
        --dmg) DMG="${2:-}"; [ -f "$DMG" ] || fail "--dmg needs an existing disk image"; shift 2 ;;
        --remove-dmg) REMOVE_DMG=1; shift ;;
        --user) TARGET_USER="${2:-}"; id -u "$TARGET_USER" >/dev/null 2>&1 || fail "--user needs an existing user"; shift 2 ;;
        --wait-pid) WAIT_PID="${2:-}"; [[ "$WAIT_PID" =~ ^[0-9]+$ ]] || fail "--wait-pid needs a process id"; shift 2 ;;
        -h|--help) echo "Usage: install-macos.sh [--version X.Y.Z] [--service]"; exit 0 ;;
        *) fail "unknown option $1" ;;
    esac
done

[ "$(uname -s)" = "Darwin" ] || fail "this script is for macOS."
[ "$(uname -m)" = "arm64" ] || fail "the agent is built for Apple silicon (arm64) only."

# Root only on the updater's behalf: it names the user the app belongs to.
if [ "$(id -u)" -eq 0 ]; then
    [ -n "$TARGET_USER" ] || fail "run it as yourself, not with sudo: it asks for the password when it needs it."
    as_root() { "$@"; }
else
    as_root() { sudo "$@"; }
fi

echo "$(date '+%Y-%m-%d %H:%M:%S') Installing $APP_NAME"

WORK="$(mktemp -d)"
MOUNT="$WORK/mount"

cleanup() {
    hdiutil detach "$MOUNT" -quiet 2>/dev/null || true
    rm -rf "$WORK"

    # The updater's download folder, with the copy of this script running now
    # (bash has it open; removing it is fine).
    if [ "$REMOVE_DMG" -eq 1 ] && [ -n "$DMG" ]; then
        local folder
        folder="$(dirname "$DMG")"
        case "$(basename "$folder")" in
            ha-agent-update-*) rm -rf "$folder" ;;
            *) rm -f "$DMG" ;;
        esac
    fi
}
trap cleanup EXIT

if [ -z "$DMG" ]; then
    # The disk image of the release.
    if [ -n "$VERSION" ]; then
        VERSION="${VERSION#v}"
        URL="https://github.com/$REPO/releases/download/v$VERSION/HomeAssistantAgent-$VERSION-macos-arm64.dmg"
    else
        echo "Looking up the latest release..."
        URL="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
            | grep -o '"browser_download_url": *"[^"]*-macos-arm64\.dmg"' \
            | head -n 1 | sed 's/.*"\(https[^"]*\)"/\1/')" || true
        [ -n "$URL" ] || fail "the latest release has no macOS disk image."
    fi

    DMG="$WORK/agent.dmg"
    echo "Downloading $URL"
    curl -fL --progress-bar -o "$DMG" "$URL" || fail "the download failed."
fi

mkdir -p "$MOUNT"
hdiutil attach "$DMG" -nobrowse -readonly -mountpoint "$MOUNT" -quiet || fail "the disk image cannot be opened."
[ -d "$MOUNT/$APP_NAME.app" ] || fail "the disk image holds no $APP_NAME.app."

if [ "$(id -u)" -ne 0 ]; then
    echo "Administrator password, to install into /Applications:"
    sudo -v || fail "installing needs an administrator."
fi

# What runs now comes back after the update; a first install starts the app.
FIRST_INSTALL=0
APP_WAS_RUNNING=0
SERVICE_WAS_INSTALLED=0

[ -d "$APP" ] || FIRST_INSTALL=1

# The updater's app exits on its own once it has started this.
if [ -n "$WAIT_PID" ]; then
    APP_WAS_RUNNING=1
    echo "Waiting for the agent ($WAIT_PID) to exit..."

    for _ in $(seq 1 120); do
        kill -0 "$WAIT_PID" 2>/dev/null || break
        sleep 1
    done

    ! kill -0 "$WAIT_PID" 2>/dev/null || fail "the agent did not exit."
fi

if pgrep -f "$APP_PROCESS" >/dev/null 2>&1; then
    # Asked from another session (as root) the agent would not hear it.
    [ "$(id -u)" -ne 0 ] || fail "another copy of the agent is running; quit it from the menu bar and update again."

    APP_WAS_RUNNING=1
    echo "Closing the running agent..."
    # The orderly way: the agent tells Home Assistant it goes offline.
    "$APP_EXE" --shutdown >/dev/null 2>&1 || true

    for _ in $(seq 1 30); do
        pgrep -f "$APP_PROCESS" >/dev/null 2>&1 || break
        sleep 1
    done

    ! pgrep -f "$APP_PROCESS" >/dev/null 2>&1 || fail "the agent did not close; quit it from the menu bar and run this again."
fi

# The service's own status: 0 running, 1 installed but not running, 2 not
# installed. An installed one is meant to run (launchd keeps it alive): it is
# installed again after the update, which also repairs one that cannot start
# — its plist pointing at a copy of the app that is gone, such as the one
# macOS runs a quarantined app from (App Translocation).
SERVICE_STATUS=2
[ -x "$SERVICE_EXE" ] && { as_root "$SERVICE_EXE" status >/dev/null 2>&1; SERVICE_STATUS=$?; } || true

[ "$SERVICE_STATUS" -eq 2 ] || SERVICE_WAS_INSTALLED=1

if [ "$SERVICE_STATUS" -eq 0 ]; then
    echo "Stopping the background service..."
    # It runs from the app bundle: stopped before its files are replaced.
    as_root "$SERVICE_EXE" stop || fail "the background service did not stop."
fi

echo "Installing $APP"
as_root rm -rf "$APP"
as_root ditto "$MOUNT/$APP_NAME.app" "$APP"
as_root xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true
# The service runs from the bundle as root: no user may change its files.
as_root chown -R root:wheel "$APP"
as_root chmod -R go-w "$APP"

# The data folder the app and the service share; idempotent, so an update
# only brings its permissions in line.
as_root "$SERVICE_EXE" setup || fail "creating the data folder failed."

if [ "$SERVICE_WAS_INSTALLED" -eq 1 ] || [ "$WITH_SERVICE" -eq 1 ]; then
    echo "Starting the background service..."
    # Rewrites the daemon's plist for this app and starts it.
    as_root "$SERVICE_EXE" install || fail "installing the background service failed."
fi

if [ "$APP_WAS_RUNNING" -eq 1 ] || [ "$FIRST_INSTALL" -eq 1 ]; then
    echo "Starting $APP_NAME"

    if [ "$(id -u)" -eq 0 ]; then
        # In the user's session and as the user: run as root, the app would
        # be root's.
        launchctl asuser "$(id -u "$TARGET_USER")" sudo -u "$TARGET_USER" open "$APP"
    else
        open "$APP"
    fi
fi

echo "$(date '+%Y-%m-%d %H:%M:%S') Done: $(defaults read "$APP/Contents/Info" CFBundleShortVersionString 2>/dev/null || echo "$APP_NAME") is installed."
