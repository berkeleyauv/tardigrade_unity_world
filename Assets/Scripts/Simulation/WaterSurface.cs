using UnityEngine;

namespace Tardigrade.Simulation
{
    /// <summary>
    /// Lightweight deterministic water surface for the URP simulator.  This is
    /// visual only: buoyancy continues to use the configured flat water plane.
    /// </summary>
    public sealed class WaterSurface : MonoBehaviour
    {
        [SerializeField] Material surfaceMaterial;
        public bool IsInitialized => mesh != null;
        Mesh mesh;
        Vector3[] restVertices;
        Vector3[] deformedVertices;
        float phase;
        float animationStartTime;

        public void Initialize(Vector2 sizeMeters, uint seed)
        {
            phase = (seed % 997u) * 0.017f;
            animationStartTime = Time.time;
            BuildMesh(sizeMeters, 32, 56);
            MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = gameObject.AddComponent<MeshRenderer>();
            MeshFilter filter = gameObject.GetComponent<MeshFilter>();
            if (filter == null)
                filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = surfaceMaterial != null ? surfaceMaterial : CreateWaterMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        public void SetAuthoredMaterial(Material material)
        {
            surfaceMaterial = material;
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        public void Reseed(uint seed)
        {
            phase = (seed % 997u) * 0.017f;
            animationStartTime = Time.time;
        }

        void BuildMesh(Vector2 size, int columns, int rows)
        {
            mesh = new Mesh { name = "Deterministic pool water" };
            restVertices = new Vector3[(columns + 1) * (rows + 1)];
            deformedVertices = new Vector3[restVertices.Length];
            var uv = new Vector2[restVertices.Length];
            var triangles = new int[columns * rows * 6];
            int vertex = 0;
            for (int row = 0; row <= rows; ++row)
            {
                float v = row / (float)rows;
                for (int column = 0; column <= columns; ++column)
                {
                    float u = column / (float)columns;
                    restVertices[vertex] = new Vector3((u - .5f) * size.x, 0f, (v - .5f) * size.y);
                    uv[vertex] = new Vector2(u, v);
                    ++vertex;
                }
            }
            int triangle = 0;
            for (int row = 0; row < rows; ++row)
            {
                for (int column = 0; column < columns; ++column)
                {
                    int lower = row * (columns + 1) + column;
                    int upper = lower + columns + 1;
                    triangles[triangle++] = lower;
                    triangles[triangle++] = upper;
                    triangles[triangle++] = lower + 1;
                    triangles[triangle++] = lower + 1;
                    triangles[triangle++] = upper;
                    triangles[triangle++] = upper + 1;
                }
            }
            mesh.vertices = restVertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.MarkDynamic();
        }

        void Update()
        {
            if (mesh == null)
                return;
            float time = Time.time - animationStartTime;
            for (int index = 0; index < restVertices.Length; ++index)
            {
                Vector3 vertex = restVertices[index];
                float broadWave = Mathf.Sin(vertex.x * 1.4f + time * .72f + phase) * .018f;
                float crossingWave = Mathf.Sin(vertex.z * 1.9f - time * .53f + phase * 1.7f) * .012f;
                float ripple = Mathf.Sin((vertex.x + vertex.z) * 4.6f + time * 1.35f) * .004f;
                vertex.y = broadWave + crossingWave + ripple;
                deformedVertices[index] = vertex;
            }
            mesh.vertices = deformedVertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        static Material CreateWaterMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            var material = new Material(shader) { name = "Pool water surface" };
            Color water = new Color(0.08f, 0.46f, 0.55f, 0.24f);
            material.color = water;
            material.SetColor("_BaseColor", water);
            material.SetFloat("_Smoothness", .94f);
            material.SetFloat("_Metallic", .08f);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
