param(
    [ValidateSet("windows", "linux")]
    [string]$Target = "windows",
    [string]$UnityEditor = $env:UNITY_EDITOR
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($UnityEditor)) {
    throw "Set UNITY_EDITOR to the Unity 6000.2 editor executable."
}
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
& $UnityEditor -batchmode -nographics -quit -projectPath $RepoRoot `
    -executeMethod SimulationBuild.BuildStandalone -simBuildTarget $Target -logFile -
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
