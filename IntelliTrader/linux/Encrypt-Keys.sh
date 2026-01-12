#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR/.."

if [ -z "$1" ] || [ -z "$2" ]; then
    echo "Usage: $0 <api_key> <api_secret>"
    exit 1
fi

dotnet bin/Debug/netcoreapp2.1/IntelliTrader.dll --encrypt --path=keys.bin --publickey="$1" --privatekey="$2"
