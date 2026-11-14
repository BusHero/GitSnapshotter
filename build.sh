#!/usr/bin/env bash
# Runs the build pipeline (pipeline/GitSnapshotter.Pipeline). Arguments are passed through,
# e.g. ./build.sh --Build:PublishDirectory=out
set -euo pipefail
exec dotnet run --project "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/pipeline/GitSnapshotter.Pipeline" -- "$@"
