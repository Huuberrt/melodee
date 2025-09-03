#!/usr/bin/env bash
set -euo pipefail

echo "[INFO] Container resource monitoring (manual)"
echo "Use 'docker stats' to observe CPU/memory/IO for melodee services."
docker stats --no-stream || true

