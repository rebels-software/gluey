#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/src/Gluey.Cli/Gluey.Cli.csproj"
PUBLISH_DIR="$SCRIPT_DIR/publish"
INSTALL_DIR="/usr/local/bin"
EXECUTABLE_NAME="gluey"

# Detect runtime
ARCH=$(uname -m)
OS=$(uname -s)

case "$OS" in
    Darwin)
        case "$ARCH" in
            arm64) RID="osx-arm64" ;;
            x86_64) RID="osx-x64" ;;
            *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
        esac
        ;;
    Linux)
        case "$ARCH" in
            aarch64) RID="linux-arm64" ;;
            x86_64) RID="linux-x64" ;;
            *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
        esac
        ;;
    *)
        echo "Unsupported OS: $OS"
        exit 1
        ;;
esac

echo "Building Gluey CLI for $RID..."
rm -rf "$PUBLISH_DIR"
dotnet publish "$PROJECT" -c Release -r "$RID" -o "$PUBLISH_DIR"

echo "Installing to $INSTALL_DIR..."
sudo cp "$PUBLISH_DIR/Gluey.Cli" "$INSTALL_DIR/$EXECUTABLE_NAME"
sudo chmod +x "$INSTALL_DIR/$EXECUTABLE_NAME"

echo "Done! Run 'gluey --help' to verify installation."
