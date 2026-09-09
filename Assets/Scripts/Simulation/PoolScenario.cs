using UnityEngine;

namespace Tardigrade.Simulation
{
    /// <summary>
    /// Dimensioned competition pool.  The simple, deterministic collision shell is
    /// deliberately separate from the replaceable visual treatment.
    /// </summary>
    public sealed class PoolScenario : MonoBehaviour
    {
        public Vector3 insideSizeMeters = new Vector3(8f, 4f, 15f);
        public float wallThicknessMeters = 0.25f;

        Material floorMaterial;
        Material wallMaterial;
        Material laneMaterial;
        Material deckMaterial;

        public void Build(float waterSurfaceY, Vector3 startPosition, Quaternion heading)
        {
            // Put the vehicle near one end with the gate and long axis ahead.
            Vector3 center = startPosition + heading * new Vector3(0f, 0f, insideSizeMeters.z * .5f - .75f);
            transform.position = new Vector3(center.x, waterSurfaceY - insideSizeMeters.y, center.z);
            transform.rotation = heading;

            // Prefab/scene-authored geometry is the normal path.  Building here is
            // retained as a compatibility fallback for older or test scenes.
            if (transform.Find("pool_collisions") != null && transform.Find("pool_visuals") != null)
                return;
            BuildGeometry(null, null, null, null);
        }

        public void BuildGeometry(Material authoredFloor, Material authoredWall,
            Material authoredLane, Material authoredDeck)
        {
            Transform collisions = ChildRoot("pool_collisions");
            Transform visuals = ChildRoot("pool_visuals");
            CreateCollision(collisions, "floor_collision", new Vector3(0f, -wallThicknessMeters * .5f, 0f),
                new Vector3(insideSizeMeters.x + 2f * wallThicknessMeters,
                    wallThicknessMeters, insideSizeMeters.z + 2f * wallThicknessMeters));
            CreateCollision(collisions, "left_wall_collision", new Vector3(-insideSizeMeters.x * .5f - wallThicknessMeters * .5f,
                    insideSizeMeters.y * .5f, 0f),
                new Vector3(wallThicknessMeters, insideSizeMeters.y, insideSizeMeters.z));
            CreateCollision(collisions, "right_wall_collision", new Vector3(insideSizeMeters.x * .5f + wallThicknessMeters * .5f,
                    insideSizeMeters.y * .5f, 0f),
                new Vector3(wallThicknessMeters, insideSizeMeters.y, insideSizeMeters.z));
            CreateCollision(collisions, "near_wall_collision", new Vector3(0f, insideSizeMeters.y * .5f,
                    -insideSizeMeters.z * .5f - wallThicknessMeters * .5f),
                new Vector3(insideSizeMeters.x, insideSizeMeters.y, wallThicknessMeters));
            CreateCollision(collisions, "far_wall_collision", new Vector3(0f, insideSizeMeters.y * .5f,
                    insideSizeMeters.z * .5f + wallThicknessMeters * .5f),
                new Vector3(insideSizeMeters.x, insideSizeMeters.y, wallThicknessMeters));
            BuildVisuals(visuals, authoredFloor, authoredWall, authoredLane, authoredDeck);
        }

        Transform ChildRoot(string name)
        {
            Transform existing = transform.Find(name);
            if (existing != null)
                return existing;
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            return root.transform;
        }

        void CreateCollision(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var wall = new GameObject(name);
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = position;
            wall.AddComponent<BoxCollider>().size = size;
        }

        void BuildVisuals(Transform visuals, Material authoredFloor, Material authoredWall,
            Material authoredLane, Material authoredDeck)
        {
            floorMaterial = authoredFloor != null ? authoredFloor : TiledMaterial(
                "Pool floor tiles",
                new Color(0.62f, 0.78f, 0.77f),
                new Color(0.29f, 0.49f, 0.51f),
                0.72f);
            wallMaterial = authoredWall != null ? authoredWall : TiledMaterial(
                "Pool wall tiles",
                new Color(0.48f, 0.69f, 0.72f),
                new Color(0.20f, 0.42f, 0.46f),
                0.78f);
            laneMaterial = authoredLane != null ? authoredLane :
                LitMaterial("Pool lane marking", new Color(0.025f, 0.045f, 0.055f), 0.48f);
            deckMaterial = authoredDeck != null ? authoredDeck : TiledMaterial(
                "Wet pool deck",
                new Color(0.51f, 0.54f, 0.52f),
                new Color(0.34f, 0.37f, 0.36f),
                0.38f);

            // Thin visual skins keep the collision geometry simple and stable.
            CreateVisualBox(visuals, "tiled_floor", new Vector3(0f, 0.01f, 0f),
                new Vector3(insideSizeMeters.x, 0.02f, insideSizeMeters.z), floorMaterial);
            CreateVisualBox(visuals, "left_tiled_wall",
                new Vector3(-insideSizeMeters.x * .5f - 0.01f, insideSizeMeters.y * .5f, 0f),
                new Vector3(0.02f, insideSizeMeters.y, insideSizeMeters.z), wallMaterial);
            CreateVisualBox(visuals, "right_tiled_wall",
                new Vector3(insideSizeMeters.x * .5f + 0.01f, insideSizeMeters.y * .5f, 0f),
                new Vector3(0.02f, insideSizeMeters.y, insideSizeMeters.z), wallMaterial);
            CreateVisualBox(visuals, "near_tiled_wall",
                new Vector3(0f, insideSizeMeters.y * .5f, -insideSizeMeters.z * .5f - 0.01f),
                new Vector3(insideSizeMeters.x, insideSizeMeters.y, 0.02f), wallMaterial);
            CreateVisualBox(visuals, "far_tiled_wall",
                new Vector3(0f, insideSizeMeters.y * .5f, insideSizeMeters.z * .5f + 0.01f),
                new Vector3(insideSizeMeters.x, insideSizeMeters.y, 0.02f), wallMaterial);

            // Competition-pool lane stripes give both people and vision algorithms
            // strong scale and heading cues without changing collision behavior.
            float laneY = 0.028f;
            for (int lane = -1; lane <= 1; ++lane)
            {
                float x = lane * insideSizeMeters.x / 3f;
                CreateVisualBox(visuals, $"lane_{lane + 2}_stripe",
                    new Vector3(x, laneY, 0f), new Vector3(0.10f, 0.012f, insideSizeMeters.z - 0.5f),
                    laneMaterial);
                CreateVisualBox(visuals, $"lane_{lane + 2}_near_T",
                    new Vector3(x, laneY + 0.001f, -insideSizeMeters.z * .5f + 1.15f),
                    new Vector3(0.85f, 0.013f, 0.10f), laneMaterial);
                CreateVisualBox(visuals, $"lane_{lane + 2}_far_T",
                    new Vector3(x, laneY + 0.001f, insideSizeMeters.z * .5f - 1.15f),
                    new Vector3(0.85f, 0.013f, 0.10f), laneMaterial);
            }

            BuildDeck(visuals);
        }

        void BuildDeck(Transform parent)
        {
            float deckWidth = 1.4f;
            float y = insideSizeMeters.y + 0.03f;
            CreateVisualBox(parent, "left_deck",
                new Vector3(-insideSizeMeters.x * .5f - deckWidth * .5f, y, 0f),
                new Vector3(deckWidth, 0.06f, insideSizeMeters.z + 2f * deckWidth), deckMaterial);
            CreateVisualBox(parent, "right_deck",
                new Vector3(insideSizeMeters.x * .5f + deckWidth * .5f, y, 0f),
                new Vector3(deckWidth, 0.06f, insideSizeMeters.z + 2f * deckWidth), deckMaterial);
            CreateVisualBox(parent, "near_deck",
                new Vector3(0f, y, -insideSizeMeters.z * .5f - deckWidth * .5f),
                new Vector3(insideSizeMeters.x, 0.06f, deckWidth), deckMaterial);
            CreateVisualBox(parent, "far_deck",
                new Vector3(0f, y, insideSizeMeters.z * .5f + deckWidth * .5f),
                new Vector3(insideSizeMeters.x, 0.06f, deckWidth), deckMaterial);
        }

        static void CreateVisualBox(Transform parent, string name, Vector3 position,
            Vector3 size, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = position;
            visual.transform.localScale = size;
            Collider primitiveCollider = visual.GetComponent<Collider>();
            if (Application.isPlaying)
                Destroy(primitiveCollider);
            else
                DestroyImmediate(primitiveCollider);
            visual.GetComponent<Renderer>().sharedMaterial = material;
        }

        static Material LitMaterial(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            var material = new Material(shader) { name = name, color = color };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            return material;
        }

        static Material TiledMaterial(string name, Color tile, Color grout, float smoothness)
        {
            Material material = LitMaterial(name, Color.white, smoothness);
            const int resolution = 128;
            const int tilePixels = 32;
            const int groutPixels = 2;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGB24, false, true)
            {
                name = name + " texture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            for (int y = 0; y < resolution; ++y)
            {
                for (int x = 0; x < resolution; ++x)
                {
                    bool seam = x % tilePixels < groutPixels || y % tilePixels < groutPixels;
                    float mottling = (((x * 17 + y * 31) & 15) - 7.5f) / 900f;
                    Color color = seam ? grout : tile + new Color(mottling, mottling, mottling, 0f);
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply(false, true);
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_BaseMap", new Vector2(12f, 24f));
            material.SetTextureScale("_MainTex", new Vector2(12f, 24f));
            return material;
        }
    }
}
