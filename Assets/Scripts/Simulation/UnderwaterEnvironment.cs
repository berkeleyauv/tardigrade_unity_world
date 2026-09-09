using UnityEngine;

namespace Tardigrade.Simulation
{
    /// <summary>
    /// URP-friendly underwater lighting, haze, surface motion, and suspended
    /// particulate.  Physical current and buoyancy remain owned by the plant.
    /// </summary>
    public sealed class UnderwaterEnvironment : MonoBehaviour
    {
        [SerializeField] ParticleSystem particles;
        [SerializeField] WaterSurface waterSurface;
        [SerializeField] Light[] poolLights;
        float lightAnimationPhase;
        float poolLightBaseIntensity;
        float profileStartTime;

        public void SetAuthoredReferences(WaterSurface authoredWaterSurface,
            Light[] authoredPoolLights, ParticleSystem authoredParticles)
        {
            waterSurface = authoredWaterSurface;
            poolLights = authoredPoolLights;
            particles = authoredParticles;
        }

        public void Initialize(string scenarioId, uint seed, float waterSurfaceY,
            Vector3 poolCenter, Quaternion poolRotation, Vector3 poolSize)
        {
            bool lowVisibility = scenarioId == "low_visibility";
            bool clearWater = scenarioId == "clean";
            profileStartTime = Time.time;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = lowVisibility ?
                new Color(0.012f, 0.075f, 0.065f) :
                clearWater ? new Color(0.035f, 0.20f, 0.25f) : new Color(0.022f, 0.145f, 0.18f);
            RenderSettings.fogDensity = lowVisibility ? 0.105f : clearWater ? 0.018f : 0.034f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = lowVisibility ?
                new Color(0.025f, 0.06f, 0.05f) :
                clearWater ? new Color(0.105f, 0.255f, 0.29f) : new Color(0.065f, 0.17f, 0.20f);

            ConfigureSun(lowVisibility, clearWater);
            EnsureWaterSurface(seed, waterSurfaceY, poolCenter, poolRotation, poolSize);
            EnsurePoolLights(seed, waterSurfaceY, poolCenter, poolRotation, poolSize, lowVisibility);
            if (particles == null)
                CreateParticulates(lowVisibility, clearWater);
            else
            {
                var emission = particles.emission;
                emission.rateOverTime = lowVisibility ? 230f : clearWater ? 22f : 72f;
                var main = particles.main;
                main.startColor = lowVisibility ?
                    new Color(0.58f, 0.65f, 0.53f, 0.48f) :
                    new Color(0.72f, 0.78f, 0.68f, 0.38f);
            }

            StereoCameraPublisher stereo = GetComponent<StereoCameraPublisher>();
            if (stereo != null)
                stereo.ConfigureVisualProfile(scenarioId, seed);
        }

        void ConfigureSun(bool lowVisibility, bool clearWater)
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (Light candidate in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (candidate.type == LightType.Directional)
                    {
                        sun = candidate;
                        break;
                    }
                }
            }
            if (sun == null)
            {
                var sunObject = new GameObject("Pool sunlight");
                sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sunObject.transform.rotation = Quaternion.Euler(58f, -28f, 0f);
            }
            RenderSettings.sun = sun;
            sun.color = lowVisibility ? new Color(0.56f, 0.72f, 0.68f) : new Color(0.76f, 0.91f, 0.90f);
            sun.intensity = lowVisibility ? .38f : clearWater ? 1.05f : .78f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .55f;
        }

        void EnsureWaterSurface(uint seed, float waterSurfaceY, Vector3 poolCenter,
            Quaternion poolRotation, Vector3 poolSize)
        {
            if (waterSurface == null)
            {
                GameObject existing = GameObject.Find("TardigradeWaterSurface");
                if (existing == null)
                    existing = new GameObject("TardigradeWaterSurface");
                waterSurface = existing.GetComponent<WaterSurface>();
                if (waterSurface == null)
                    waterSurface = existing.AddComponent<WaterSurface>();
            }
            waterSurface.transform.position = new Vector3(poolCenter.x, waterSurfaceY, poolCenter.z);
            waterSurface.transform.rotation = poolRotation;
            waterSurface.transform.localScale = Vector3.one;
            if (!waterSurface.IsInitialized)
                waterSurface.Initialize(new Vector2(poolSize.x + .12f, poolSize.z + .12f), seed);
            else
                waterSurface.Reseed(seed);
        }

        void EnsurePoolLights(uint seed, float waterSurfaceY, Vector3 poolCenter,
            Quaternion poolRotation, Vector3 poolSize, bool lowVisibility)
        {
            lightAnimationPhase = (seed % 541u) * .021f;
            if (poolLights == null || poolLights.Length != 4 ||
                System.Array.Exists(poolLights, light => light == null))
            {
                poolLights = new Light[4];
                var holder = new GameObject("Pool underwater lighting");
                holder.transform.position = poolCenter;
                holder.transform.rotation = poolRotation;
                float x = poolSize.x * .32f;
                float z = poolSize.z * .28f;
                Vector3[] positions = {
                    new Vector3(-x, waterSurfaceY - poolCenter.y - .12f, -z),
                    new Vector3(x, waterSurfaceY - poolCenter.y - .12f, -z),
                    new Vector3(-x, waterSurfaceY - poolCenter.y - .12f, z),
                    new Vector3(x, waterSurfaceY - poolCenter.y - .12f, z),
                };
                for (int index = 0; index < poolLights.Length; ++index)
                {
                    var lightObject = new GameObject($"subsurface_light_{index + 1}");
                    lightObject.transform.SetParent(holder.transform, false);
                    lightObject.transform.localPosition = positions[index];
                    lightObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Light light = lightObject.AddComponent<Light>();
                    light.type = LightType.Spot;
                    light.color = new Color(0.53f, 0.83f, 0.88f);
                    light.range = poolSize.y * 1.8f;
                    light.spotAngle = 84f;
                    light.innerSpotAngle = 52f;
                    light.shadows = index < 2 ? LightShadows.Soft : LightShadows.None;
                    poolLights[index] = light;
                }
            }
            else
            {
                Transform holder = poolLights[0].transform.parent;
                holder.position = poolCenter;
                holder.rotation = poolRotation;
                float x = poolSize.x * .32f;
                float z = poolSize.z * .28f;
                Vector3[] positions = {
                    new Vector3(-x, waterSurfaceY - poolCenter.y - .12f, -z),
                    new Vector3(x, waterSurfaceY - poolCenter.y - .12f, -z),
                    new Vector3(-x, waterSurfaceY - poolCenter.y - .12f, z),
                    new Vector3(x, waterSurfaceY - poolCenter.y - .12f, z),
                };
                for (int index = 0; index < poolLights.Length; ++index)
                    poolLights[index].transform.localPosition = positions[index];
            }
            poolLightBaseIntensity = lowVisibility ? 1.2f : 2.4f;
            for (int index = 0; index < poolLights.Length; ++index)
                poolLights[index].intensity = poolLightBaseIntensity;
        }

        void CreateParticulates(bool lowVisibility, bool clearWater)
        {
            var holder = new GameObject("underwater_particulates");
            holder.transform.SetParent(transform, false);
            particles = holder.AddComponent<ParticleSystem>();
            ParticleSystemRenderer particleRenderer = holder.GetComponent<ParticleSystemRenderer>();
            Material particleMaterial = Resources.Load<Material>("UnderwaterParticulate");
            if (particleMaterial == null)
            {
                Debug.LogError("Missing Resources/UnderwaterParticulate material; disabling particulates to avoid magenta fallback rendering.");
                particleRenderer.enabled = false;
            }
            else
            {
                particleRenderer.sharedMaterial = particleMaterial;
                particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                particleRenderer.alignment = ParticleSystemRenderSpace.View;
            }
            var main = particles.main;
            main.loop = true;
            main.startLifetime = 12f;
            main.startSpeed = 0.025f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.012f);
            main.startColor = new Color(0.72f, 0.78f, 0.68f, 0.38f);
            main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = particles.emission;
            emission.rateOverTime = lowVisibility ? 230f : clearWater ? 22f : 72f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(8f, 5f, 12f);
        }

        void Update()
        {
            if (poolLights == null)
                return;
            float profileTime = Time.time - profileStartTime;
            // Slow, low-amplitude variation approximates moving surface caustics
            // without adding a heavyweight render feature to every ROS camera.
            for (int index = 0; index < poolLights.Length; ++index)
            {
                Light light = poolLights[index];
                if (light == null)
                    continue;
                float variation = 1f + .08f * Mathf.Sin(profileTime * .65f + lightAnimationPhase + index * 1.7f);
                light.transform.localEulerAngles = new Vector3(90f,
                    2.2f * Mathf.Sin(profileTime * .21f + index), 0f);
                light.intensity = poolLightBaseIntensity * variation;
            }
        }
    }
}
