#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
unity_editor="${UNITY_EDITOR:-}"
if [[ -z "$unity_editor" ]]; then
  echo "Set UNITY_EDITOR to the Unity 6000.2 editor executable."
  exit 1
fi
"$unity_editor" -batchmode -nographics -projectPath "$repo_root" \
  -executeMethod SimulationPlayModeValidation.Run -logFile -
