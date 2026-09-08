using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Tardigrade.Simulation;

/// <summary>
/// Builds the inspectable simulator prefabs and wires SampleScene to them. Runtime
/// scripts still configure scenario state, but no longer need to invent the world.
/// </summary>
public static class SimulationSceneAuthoring
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string PrefabDirectory = "Assets/Prefabs/Simulation";
    const string MaterialDirectory = "Assets/Materials/Simulation";

    [MenuItem("Tardigrade/Rebuild Authored Simulation Scene")]
    public static void Run()
    {
        EnsureFolder("Assets/Prefabs");
        EnsureFolder(PrefabDirectory);
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialDirectory);

        Material floor = TiledMaterial("PoolFloorTiles", new Color(.62f, .78f, .77f),
            new Color(.29f, .49f, .51f), .72f, 12f, 24f);
        Material wall = TiledMaterial("PoolWallTiles", new Color(.48f, .69f, .72f),
            new Color(.20f, .42f, .46f), .78f, 12f, 24f);
        Material lane = LitMaterial("PoolLaneMarking", new Color(.025f, .045f, .055f), .48f);
        Material deck = TiledMaterial("WetPoolDeck", new Color(.51f, .54f, .52f),
            new Color(.34f, .37f, .36f), .38f, 10f, 18f);
        Material gateOrange = LitMaterial("CompetitionGateOrange", new Color(1f, .22f, .012f), .34f);
        Material gateBase = LitMaterial("GateWeightedBase", new Color(.055f, .07f, .075f), .28f, .18f);
        Material water = WaterMaterial();

        GameObject poolPrefab = BuildPoolPrefab(floor, wall, lane, deck);
        GameObject gatePrefab = BuildGatePrefab(gateOrange, gateBase);
        GameObject effectsPrefab = BuildEffectsPrefab(water);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RosRobotBridge bridge = Object.FindFirstObjectByType<RosRobotBridge>();
        if (bridge == null)
            throw new InvalidDataException("SampleScene is missing TardigradeRoot/RosRobotBridge");

        GameObject robot = bridge.gameObject;
        Rigidbody body = GetOrAdd<Rigidbody>(robot);
        BoxCollider collider = GetOrAdd<BoxCollider>(robot);
        AuvPhysicsPlant plant = GetOrAdd<AuvPhysicsPlant>(robot);
        UnderwaterEnvironment underwater = GetOrAdd<UnderwaterEnvironment>(robot);
        StereoCameraPublisher cameras = GetOrAdd<StereoCameraPublisher>(robot);

        SimulationConfig config = SimulationConfig.Load();
        float[] collisionSize = config.rigid_body.collision_size_m;
        collider.size = new Vector3(collisionSize[1], collisionSize[2], collisionSize[0]);
        body.useGravity = false;
        body.linearDamping = 0f;
        body.angularDamping = 0f;

        Transform sensors = Child(robot.transform, "Sensors");
        Transform imu = SensorMount(sensors, "imu_link", config.Sensor("imu"));
        Transform pressure = SensorMount(sensors, "pressure_link", config.Sensor("pressure"));
        Transform vio = SensorMount(sensors, "visual_odometry_link", config.Sensor("visual_odometry"));
        Transform stereoRig = SensorMount(sensors, "front_stereo_link", config.Sensor("front_stereo"));
        Camera previewCamera = robot.GetComponentInChildren<Camera>(true);
        cameras.SetAuthoredRig(stereoRig, previewCamera);

        GameObject previousEnvironment = GameObject.Find("SimulationEnvironment");
        if (previousEnvironment != null)
            Object.DestroyImmediate(previousEnvironment);
        var environment = new GameObject("SimulationEnvironment");
        SceneManager.MoveGameObjectToScene(environment, scene);

        GameObject poolObject = Instantiate(poolPrefab, environment.transform, "AcceptancePoolCollisions");
        GameObject gateObject = Instantiate(gatePrefab, environment.transform, "GateAcceptance");
        GameObject effectsObject = Instantiate(effectsPrefab, environment.transform, "UnderwaterEffects");
        PoolScenario pool = poolObject.GetComponent<PoolScenario>();
        GateScenario gate = gateObject.GetComponent<GateScenario>();
        pool.Build(config.water.surface_unity_y_m, robot.transform.position, robot.transform.rotation);
        gate.Build(robot.transform.position, robot.transform.rotation);

        WaterSurface waterSurface = effectsObject.GetComponentInChildren<WaterSurface>(true);
        Light[] lights = effectsObject.GetComponentsInChildren<Light>(true);
        ParticleSystem particles = effectsObject.GetComponentInChildren<ParticleSystem>(true);
        PositionEffects(pool, config.water.surface_unity_y_m, waterSurface, lights, particles);
        underwater.SetAuthoredReferences(waterSurface, lights, particles);
        bridge.SetAuthoredReferences(body, collider, plant, underwater, pool, gate, cameras);

        // Keep the imported model available as a reference without rendering a
        // second pool over the dimensioned simulator prefab.
        GameObject legacyPool = GameObject.Find("PoolV2");
        if (legacyPool != null)
            legacyPool.SetActive(false);

        EditorUtility.SetDirty(robot);
        EditorUtility.SetDirty(environment);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Authored simulator scene and prefabs rebuilt successfully.");
        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static GameObject BuildPoolPrefab(Material floor, Material wall, Material lane, Material deck)
    {
        var root = new GameObject("AcceptancePoolCollisions");
        root.AddComponent<PoolScenario>().BuildGeometry(floor, wall, lane, deck);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDirectory + "/PoolEnvironment.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject BuildGatePrefab(Material orange, Material weightedBase)
    {
        var root = new GameObject("GateAcceptance");
        root.AddComponent<GateScenario>().BuildGeometry(orange, weightedBase);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDirectory + "/Gate.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject BuildEffectsPrefab(Material waterMaterial)
    {
        var root = new GameObject("UnderwaterEffects");
        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "TardigradeWaterSurface";
        water.transform.SetParent(root.transform, false);
        water.transform.localScale = new Vector3(.812f, 1f, 1.512f);
        Object.DestroyImmediate(water.GetComponent<Collider>());
        WaterSurface surface = water.AddComponent<WaterSurface>();
        surface.SetAuthoredMaterial(waterMaterial);

        var lighting = new GameObject("Pool underwater lighting");
        lighting.transform.SetParent(root.transform, false);
        for (int index = 0; index < 4; ++index)
        {
            var lightObject = new GameObject($"subsurface_light_{index + 1}");
            lightObject.transform.SetParent(lighting.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(.53f, .83f, .88f);
            light.range = 7.2f;
            light.spotAngle = 84f;
            light.innerSpotAngle = 52f;
            light.shadows = index < 2 ? LightShadows.Soft : LightShadows.None;
        }

        var particleObject = new GameObject("underwater_particulates");
        particleObject.transform.SetParent(root.transform, false);
        ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
        ParticleSystemRenderer particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.sharedMaterial = Resources.Load<Material>("UnderwaterParticulate");
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.alignment = ParticleSystemRenderSpace.View;
        var main = particles.main;
        main.loop = true;
        main.startLifetime = 12f;
        main.startSpeed = .025f;
        main.startSize = new ParticleSystem.MinMaxCurve(.003f, .012f);
        main.startColor = new Color(.72f, .78f, .68f, .38f);
        main.maxParticles = 3000;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = particles.emission;
        emission.rateOverTime = 72f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(8f, 5f, 12f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDirectory + "/UnderwaterEffects.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void PositionEffects(PoolScenario pool, float surfaceY, WaterSurface water,
        Light[] lights, ParticleSystem particles)
    {
        water.transform.position = new Vector3(pool.transform.position.x, surfaceY, pool.transform.position.z);
        water.transform.rotation = pool.transform.rotation;
        Transform lightRoot = lights[0].transform.parent;
        lightRoot.position = pool.transform.position;
        lightRoot.rotation = pool.transform.rotation;
        float x = pool.insideSizeMeters.x * .32f;
        float z = pool.insideSizeMeters.z * .28f;
        Vector3[] positions = {
            new Vector3(-x, surfaceY - pool.transform.position.y - .12f, -z),
            new Vector3(x, surfaceY - pool.transform.position.y - .12f, -z),
            new Vector3(-x, surfaceY - pool.transform.position.y - .12f, z),
            new Vector3(x, surfaceY - pool.transform.position.y - .12f, z),
        };
        for (int index = 0; index < lights.Length; ++index)
            lights[index].transform.localPosition = positions[index];
        particles.transform.position = pool.transform.position + Vector3.up * pool.insideSizeMeters.y * .5f;
        particles.transform.rotation = pool.transform.rotation;
    }

    static Transform SensorMount(Transform parent, string name, SensorConfig sensor)
    {
        Transform mount = Child(parent, name);
        mount.localPosition = SimulationConfig.RosPolarToUnity(sensor.position_m);
        mount.localRotation = Quaternion.identity;
        return mount;
    }

    static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        return gameObject.transform;
    }

    static GameObject Instantiate(GameObject prefab, Transform parent, string instanceName)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.name = instanceName;
        return instance;
    }

    static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    static Material LitMaterial(string name, Color color, float smoothness, float metallic = 0f)
    {
        string path = MaterialDirectory + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material TiledMaterial(string name, Color tile, Color grout, float smoothness,
        float scaleX, float scaleY)
    {
        Material material = LitMaterial(name, Color.white, smoothness);
        string texturePath = MaterialDirectory + "/" + name + "Texture.asset";
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            const int resolution = 128;
            const int tilePixels = 32;
            const int groutPixels = 2;
            texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false, true)
            {
                name = name + "Texture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            for (int y = 0; y < resolution; ++y)
            for (int x = 0; x < resolution; ++x)
            {
                bool seam = x % tilePixels < groutPixels || y % tilePixels < groutPixels;
                float mottling = (((x * 17 + y * 31) & 15) - 7.5f) / 900f;
                texture.SetPixel(x, y, seam ? grout : tile + new Color(mottling, mottling, mottling, 0f));
            }
            texture.Apply(false, true);
            AssetDatabase.CreateAsset(texture, texturePath);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        material.SetTextureScale("_BaseMap", new Vector2(scaleX, scaleY));
        material.SetTextureScale("_MainTex", new Vector2(scaleX, scaleY));
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material WaterMaterial()
    {
        Material material = LitMaterial("PoolWaterSurface", new Color(.08f, .46f, .55f, .24f), .94f, .08f);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
