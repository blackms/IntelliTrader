#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR/.."
dotnet bin/Debug/netcoreapp2.1/IntelliTrader.dll "$@"
