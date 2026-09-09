using UnityEngine;

namespace Tardigrade.Simulation
{
    /// <summary>Dimensioned gate with independent visual and collision objects.</summary>
    public sealed class GateScenario : MonoBehaviour
    {
        public float distanceMeters = 4f;
        public float apertureWidthMeters = 2.4f;
        public float apertureHeightMeters = 1.6f;
        public float postThicknessMeters = 0.12f;

        public void Build(Vector3 origin, Quaternion heading)
        {
            transform.position = origin + heading * new Vector3(0f, 0f, distanceMeters);
            transform.rotation = heading;
            // Authored prefab geometry is preferred. Keep construction as a
            // compatibility fallback for lightweight and legacy scenes.
            if (transform.Find("gate_visuals") != null && transform.Find("gate_collisions") != null)
                return;
            BuildGeometry(null, null);
        }

        public void BuildGeometry(Material authoredGateMaterial, Material authoredBaseMaterial)
        {
            var visuals = new GameObject("gate_visuals");
            visuals.transform.SetParent(transform, false);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            Material material = authoredGateMaterial != null ? authoredGateMaterial :
                new Material(shader) { name = "Competition gate orange" };
            Color gateOrange = new Color(1f, 0.22f, 0.012f);
            material.color = gateOrange;
            material.SetColor("_BaseColor", gateOrange);
            material.SetFloat("_Smoothness", .34f);
            Material baseMaterial = authoredBaseMaterial != null ? authoredBaseMaterial :
                new Material(shader) { name = "Gate weighted bases" };
            Color baseColor = new Color(.055f, .07f, .075f);
            baseMaterial.color = baseColor;
            baseMaterial.SetColor("_BaseColor", baseColor);
            baseMaterial.SetFloat("_Metallic", .18f);
            baseMaterial.SetFloat("_Smoothness", .28f);
            float side = apertureWidthMeters * 0.5f + postThicknessMeters * 0.5f;
            CreateVisual(visuals.transform, "left_post", new Vector3(-side, 0f, 0f),
                new Vector3(postThicknessMeters, apertureHeightMeters, postThicknessMeters), material);
            CreateVisual(visuals.transform, "right_post", new Vector3(side, 0f, 0f),
                new Vector3(postThicknessMeters, apertureHeightMeters, postThicknessMeters), material);
            CreateVisual(visuals.transform, "top_bar", new Vector3(0f, apertureHeightMeters * .5f, 0f),
                new Vector3(apertureWidthMeters + 2f * postThicknessMeters, postThicknessMeters, postThicknessMeters), material);
            CreateVisual(visuals.transform, "left_weighted_foot",
                new Vector3(-side, -apertureHeightMeters * .5f + .055f, 0f),
                new Vector3(.48f, .11f, .42f), baseMaterial);
            CreateVisual(visuals.transform, "right_weighted_foot",
                new Vector3(side, -apertureHeightMeters * .5f + .055f, 0f),
                new Vector3(.48f, .11f, .42f), baseMaterial);
            var collisions = new GameObject("gate_collisions");
            collisions.transform.SetParent(transform, false);
            CreateCollider(collisions.transform, new Vector3(-side, 0f, 0f),
                new Vector3(postThicknessMeters, apertureHeightMeters, postThicknessMeters));
            CreateCollider(collisions.transform, new Vector3(side, 0f, 0f),
                new Vector3(postThicknessMeters, apertureHeightMeters, postThicknessMeters));
            CreateCollider(collisions.transform, new Vector3(0f, apertureHeightMeters * .5f, 0f),
                new Vector3(apertureWidthMeters + 2f * postThicknessMeters, postThicknessMeters, postThicknessMeters));
        }

        static void CreateVisual(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Collider primitiveCollider = part.GetComponent<Collider>();
            if (Application.isPlaying)
                Destroy(primitiveCollider);
            else
                DestroyImmediate(primitiveCollider);
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        static void CreateCollider(Transform parent, Vector3 position, Vector3 size)
        {
            var part = new GameObject("collision");
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.AddComponent<BoxCollider>().size = size;
        }
    }
}
