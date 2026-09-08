#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
unity_editor="${UNITY_EDITOR:-}"
target="${1:-linux}"
if [[ -z "$unity_editor" ]]; then
  echo "Set UNITY_EDITOR to the Unity 6000.2 editor executable."
  exit 1
fi
"$unity_editor" -batchmode -nographics -quit -projectPath "$repo_root" \
  -executeMethod SimulationBuild.BuildStandalone -simBuildTarget "$target" -logFile -
