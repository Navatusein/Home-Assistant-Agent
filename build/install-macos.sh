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
# service if it was installed, the app if it was running. Downloaded by curl, the app carries no quarantine:
# Gatekeeper does not block its first start, no "Open Anyway" needed.
#
# Run as yourself, not with sudo: the steps that need root ask for the
# password, and the app has to start in your session.
set -euo pipefail

REPO="Navatusein/Home-Assistant-Agent"
APP_NAME="Home Assistant Agent"
APP="/Applications/$APP_NAME.app"
APP_EXE="$APP/Contents/MacOS/HomeAssistant.Agent"
SERVICE_EXE="$APP/Contents/MacOS/HomeAssistant.Agent.Service"
SERVICE_LABEL="com.navatusein.homeassistant-agent.service"
# The app's process: its path alone or followed by arguments — a bare match on
# the path would also find the service, whose name begins the same.
APP_PROCESS="^$APP_EXE( |\$)"

VERSION=""
WITH_SERVICE=0

fail() {
    echo "Error: $*" >&2
    exit 1
}

while [ $# -gt 0 ]; do
    case "$1" in
        --version) VERSION="${2:-}"; [ -n "$VERSION" ] || fail "--version needs a version, e.g. 1.2.0"; shift 2 ;;
        --service) WITH_SERVICE=1; shift ;;
        -h|--help) echo "Usage: install-macos.sh [--version X.Y.Z] [--service]"; exit 0 ;;
        *) fail "unknown option $1" ;;
    esac
done

[ "$(uname -s)" = "Darwin" ] || fail "this script is for macOS."
[ "$(uname -m)" = "arm64" ] || fail "the agent is built for Apple silicon (arm64) only."
[ "$(id -u)" -ne 0 ] || fail "run it as yourself, not with sudo: it asks for the password when it needs it."

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

WORK="$(mktemp -d)"
MOUNT="$WORK/mount"

cleanup() {
    hdiutil detach "$MOUNT" -quiet 2>/dev/null || true
    rm -rf "$WORK"
}
trap cleanup EXIT

echo "Downloading $URL"
curl -fL --progress-bar -o "$WORK/agent.dmg" "$URL" || fail "the download failed."

mkdir -p "$MOUNT"
hdiutil attach "$WORK/agent.dmg" -nobrowse -readonly -mountpoint "$MOUNT" -quiet || fail "the disk image cannot be opened."
[ -d "$MOUNT/$APP_NAME.app" ] || fail "the disk image holds no $APP_NAME.app."

echo "Administrator password, to install into /Applications:"
sudo -v || fail "installing needs an administrator."

# What runs now comes back after the update; a first install starts the app.
FIRST_INSTALL=0
APP_WAS_RUNNING=0
SERVICE_WAS_INSTALLED=0

[ -d "$APP" ] || FIRST_INSTALL=1

if pgrep -f "$APP_PROCESS" >/dev/null 2>&1; then
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
[ -x "$SERVICE_EXE" ] && { sudo "$SERVICE_EXE" status >/dev/null 2>&1; SERVICE_STATUS=$?; } || true

[ "$SERVICE_STATUS" -eq 2 ] || SERVICE_WAS_INSTALLED=1

if [ "$SERVICE_STATUS" -eq 0 ]; then
    echo "Stopping the background service..."
    # It runs from the app bundle: stopped before its files are replaced.
    sudo "$SERVICE_EXE" stop || fail "the background service did not stop."
fi

echo "Installing $APP"
sudo rm -rf "$APP"
sudo ditto "$MOUNT/$APP_NAME.app" "$APP"
sudo xattr -dr com.apple.quarantine "$APP" 2>/dev/null || true
# The service runs from the bundle as root: no user may change its files.
sudo chown -R root:wheel "$APP"
sudo chmod -R go-w "$APP"

# The data folder the app and the service share; idempotent, so an update
# only brings its permissions in line.
sudo "$SERVICE_EXE" setup || fail "creating the data folder failed."

if [ "$SERVICE_WAS_INSTALLED" -eq 1 ] || [ "$WITH_SERVICE" -eq 1 ]; then
    echo "Starting the background service..."
    # Rewrites the daemon's plist for this app and starts it.
    sudo "$SERVICE_EXE" install || fail "installing the background service failed."
fi

if [ "$APP_WAS_RUNNING" -eq 1 ] || [ "$FIRST_INSTALL" -eq 1 ]; then
    echo "Starting $APP_NAME"
    open "$APP"
fi

echo "Done: $(defaults read "$APP/Contents/Info" CFBundleShortVersionString 2>/dev/null || echo "$APP_NAME") is installed."
