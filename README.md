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

## How The Unity Scene Is Split

`SampleScene` uses a hybrid authored/runtime layout. Physical arrangement and
appearance are visible in Edit Mode; scripts configure changing scenario state
when Play Mode begins.

```text
SampleScene
├── SimulationEnvironment
│   ├── AcceptancePoolCollisions  (PoolEnvironment prefab)
│   ├── GateAcceptance            (Gate prefab)
│   └── UnderwaterEffects         (water, pool lights, particulate)
└── TardigradeRoot
    ├── Rigidbody + collider
    ├── RosRobotBridge + AuvPhysicsPlant
    ├── UnderwaterEnvironment + StereoCameraPublisher
    ├── Sensors                    (inspectable sensor mounting frames)
    └── tardigrade                 (visual FBX model)
```

The imported lowercase `tardigrade` is presentation only. `TardigradeRoot`
owns the one Rigidbody and the vehicle transform, preventing individual FBX
mesh pieces from becoming separate physical bodies.

The pool, gate, water, lighting, particle system, robot collider, and sensor
mounts are saved in the scene or under `Assets/Prefabs/Simulation`. Runtime
scripts still own:

- scenario selection and reset pose,
- water current and visual profile,
- sensor noise, delay, drift, and dropout,
- thruster state and failures,
- ROS transport and simulation clock,
- temporary left/right camera renderers.

`RosRobotBridge` is the composition and ROS lifecycle component.
`AuvPhysicsPlant` applies buoyancy, six-axis damping, and distributed thruster
forces to the serialized Rigidbody at 100 Hz. Canonical physical values come
from `Assets/StreamingAssets/tardigrade_vehicle.json`, rather than being copied
into scene objects.

The Scene view is the free editor camera. The Game view is the output of the
vehicle-mounted camera, so the two views are expected to look different.

To rebuild the authored assets after changing their procedural templates, use
**Tardigrade → Rebuild Authored Simulation Scene**. This recreates the three
simulation prefabs and their materials, reconnects `SampleScene`, and is safe to
run repeatedly.

Mission paths and control remain on the ROS side. Unity consumes normalized
per-thruster commands and publishes raw simulated sensors and simulation-only
ground truth.

## `Assets/Editor/` CLI Helpers

Headless-automation scripts, useful for CI or re-running setup without
opening the Editor UI (`Unity.exe -batchmode -nographics -quit -projectPath
<proj> -executeMethod <Class>.<Method>`):

- `CliMessageGen.GenerateTardigradeInterfaces`: same as **Robotics →
  Generate ROS Messages**, scripted against
  `tardigrade_ws/src/tardigrade_interfaces`.
- `CliAttachBridge.ListSceneObjects` / `CliAttachBridge.AttachToRobot`:
  lists the scene hierarchy, or finds the GameObject named `tardigrade` and
  attaches `RosRobotBridge` to it, saving the scene. Useful after rebuilding
  the scene from a new robot model import.
- `SimulationSceneAuthoring.Run`: rebuilds the simulator prefabs, materials,
  sensor mounts, and serialized scene references.
- `SimulationBatchValidation.Run`: validates configuration and coordinate/frame
  assumptions.
- `SimulationPlayModeValidation.Run`: runs the acceptance-scene smoke test.
