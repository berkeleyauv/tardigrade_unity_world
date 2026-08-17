# Tardigrade Unity Sim

Unity project for the Tardigrade pool scene. This is the ROS-TCP-Connector
based simulator backend described in
[`tardigrade_ws/docs/unity_simulation_plan.md`](../tardigrade_ws/docs/unity_simulation_plan.md) —
see that doc for the full architecture. This README covers what actually
lives in this project and how to change robot movement/paths.

## Setup

1. Install the same Editor version as `ProjectSettings/ProjectVersion.txt`.
2. Open the project once so packages resolve (`com.unity.robotics.ros-tcp-connector`
   and `com.unity.robotics.visualizations` are pulled from GitHub via
   `Packages/manifest.json`).
3. **Robotics → Generate ROS Messages**, pointed at
   `tardigrade_ws/src/tardigrade_interfaces`, generates the C# types under
   `Assets/RosMessages/TardigradeInterfaces/`. Re-run this whenever a `.msg`
   or `.srv` file changes in `tardigrade_interfaces`.
4. **Robotics → ROS Settings** → Protocol `ROS2`, IP `127.0.0.1`, Port
   `10000` (matches `ros_tcp_endpoint`'s default in the ROS workspace).

## Running The Simulator

Start `ros_tcp_endpoint` in the `tardigrade_ws` Docker container:

```bash
cd /ws && source install/setup.bash
ros2 run ros_tcp_endpoint default_server_endpoint --ros-args \
  -p ROS_IP:=0.0.0.0 -p ROS_TCP_PORT:=10000
```

Press **Play** in the Unity Editor, then drive the robot from ROS — see
[`tardigrade_ws/docs/local_sim_backend.md`](../tardigrade_ws/docs/local_sim_backend.md#driving-a-custom-path)
for `path_follower` and mission usage. Nothing on the ROS side needs to know
whether it's talking to Unity or the fake backend; the topic contract is
identical.

## `RosRobotBridge.cs` — How Movement Works

`Assets/Scripts/RosRobotBridge.cs`, attached to the `tardigrade` root
GameObject in `SampleScene`, is the whole simulator backend on the Unity
side. It:

- subscribes `/tardigrade/cmd_vel` and stores the latest command,
- integrates a **kinematic** pose each `FixedUpdate` (no Rigidbody/physics
  yet — see `IntegrateMotion`), the same model as
  `tardigrade_sim/fake_unity_backend.py`,
- applies that pose to the GameObject's transform (`ApplyPoseToTransform`,
  which handles the ROS ↔ Unity axis conversion),
- publishes `/tardigrade/state/odometry` and `/tardigrade/status`,
- answers the `/tardigrade/set_armed` and `/tardigrade/set_external_control`
  services.

To change how the robot moves in response to `cmd_vel`, edit the Inspector
fields on the `RosRobotBridge` component (no code changes needed for basic
tuning):

| Field | Effect |
|---|---|
| `Linear Speed` | m/s at a full (`±1.0`) linear `cmd_vel` command. |
| `Yaw Speed` | rad/s at a full (`±1.0`) angular.z command. |
| `Cmd Timeout Sec` | Robot stops if `cmd_vel` goes stale for this long. |
| `Publish Rate Hz` | How often odometry/status are published. |

To change the *shape* of motion (e.g. add drag, acceleration limits, or
switch to real Rigidbody/buoyancy physics), edit `IntegrateMotion()` — it is
the single method that turns a `cmd_vel` into a pose delta each physics
tick. Everything downstream (`ApplyPoseToTransform`, odometry publishing)
stays the same regardless of how the pose is computed.

**This script does not define paths.** Paths (waypoint sequences, gate
missions, etc.) live entirely on the ROS side in `tardigrade_ws` and drive
the robot purely through `/tardigrade/cmd_vel` — see the `path_follower`
docs linked above to build or customize a path. Nothing in Unity needs to
change to run a different path.

## `Assets/Editor/` CLI Helpers

Two headless-automation scripts, useful for CI or re-running setup without
opening the Editor UI (`Unity.exe -batchmode -nographics -quit -projectPath
<proj> -executeMethod <Class>.<Method>`):

- `CliMessageGen.GenerateTardigradeInterfaces`: same as **Robotics →
  Generate ROS Messages**, scripted against
  `tardigrade_ws/src/tardigrade_interfaces`.
- `CliAttachBridge.ListSceneObjects` / `CliAttachBridge.AttachToRobot`:
  lists the scene hierarchy, or finds the GameObject named `tardigrade` and
  attaches `RosRobotBridge` to it, saving the scene. Useful after rebuilding
  the scene from a new robot model import.
