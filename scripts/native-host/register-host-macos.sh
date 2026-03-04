#!/usr/bin/env bash
set -euo pipefail

HOST_NAME="com.authorized.downloader.host"
APP_DIR="${1:-$HOME/.authorized-downloader}"
HOST_DIR="$HOME/Library/Application Support/Google/Chrome/NativeMessagingHosts"
EDGE_HOST_DIR="$HOME/Library/Application Support/Microsoft Edge/NativeMessagingHosts"
PUBLISH_DIR="$(pwd)/Downloader.Host/bin/Release/net8.0/publish"

mkdir -p "$APP_DIR" "$HOST_DIR" "$EDGE_HOST_DIR"
if [[ ! -f "$PUBLISH_DIR/Downloader.Host" ]]; then
  echo "Publish output not found at: $PUBLISH_DIR"
  echo "Run: dotnet publish ./Downloader.Host -c Release"
  exit 1
fi

cp -R "$PUBLISH_DIR/"* "$APP_DIR/"
chmod +x "$APP_DIR/Downloader.Host"

cat > "$HOST_DIR/$HOST_NAME.json" <<JSON
{
  "name": "$HOST_NAME",
  "description": "Authorized downloader native host",
  "path": "$APP_DIR/Downloader.Host",
  "type": "stdio",
  "allowed_origins": ["chrome-extension://__EXTENSION_ID__/"]
}
JSON

cp "$HOST_DIR/$HOST_NAME.json" "$EDGE_HOST_DIR/$HOST_NAME.json"
echo "Native host manifests written. Replace __EXTENSION_ID__ with your extension id."
