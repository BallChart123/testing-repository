#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet publish "$ROOT/src/PosReports.Functions/PosReports.Functions.csproj" -c Release -o "$ROOT/artifacts/lambda"
echo "Lambda package ready: $ROOT/artifacts/lambda"
