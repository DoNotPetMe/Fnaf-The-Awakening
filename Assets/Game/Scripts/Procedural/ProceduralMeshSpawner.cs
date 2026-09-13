using UnityEngine;

namespace Grotto.Procedural
{
    /// <summary>Small generated pieces that individual fixtures need.</summary>
    public enum FixtureMesh
    {
        /// <summary>A blast door leaf: ribbed steel plate on a frame.</summary>
        BlastDoorPanel,
        /// <summary>The bolt block that slides when the sump grate locks.</summary>
        GrateBolts,
        /// <summary>The grate itself: a welded bar lattice.</summary>
        GrateLattice,
        /// <summary>A floodlight housing.</summary>
        FloodlightHousing
    }

    /// <summary>
    /// Generates one small mesh for a fixture at load time.
    ///
    /// Same reasoning as <see cref="FacilityGeometrySpawner"/>: keeping generated
    /// geometry out of the scene asset. These pieces are small, but there are enough
    /// of them that baking would still push binary data into a file meant to be read
    /// in a diff.
    /// </summary>
    [DefaultExecutionOrder(-790)]
    [DisallowMultipleComponent]
    public sealed class ProceduralMeshSpawner : MonoBehaviour
    {
        [SerializeField] private FixtureMesh mesh = FixtureMesh.BlastDoorPanel;
        [SerializeField] private Vector3 size = new Vector3(3.2f, 3f, 0.22f);
        [SerializeField] private SurfaceKind surface = SurfaceKind.SteelPainted;
        [SerializeField] private Color tint = new Color(0.45f, 0.42f, 0.38f);
        [SerializeField] private bool addCollider = true;

        private void Awake() => Generate();

        public void Generate()
        {
            var builder = new MeshBuilder(512);
            builder.CurrentColor = tint;
            builder.UvScale = 1f;

            switch (mesh)
            {
                case FixtureMesh.BlastDoorPanel: BuildDoorPanel(builder); break;
                case FixtureMesh.GrateBolts: BuildBolts(builder); break;
                case FixtureMesh.GrateLattice: BuildLattice(builder); break;
                case FixtureMesh.FloodlightHousing: BuildHousing(builder); break;
            }

            var filter = gameObject.GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = builder.ToMesh(mesh.ToString());

            var renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialLibrary.Get(surface);

            if (!addCollider || gameObject.GetComponent<Collider>() != null) return;

            var box = gameObject.AddComponent<BoxCollider>();
            box.size = size;
        }

        private void BuildDoorPanel(MeshBuilder builder)
        {
            // Plate.
            builder.AddBox(Vector3.zero, size);

            // Horizontal stiffening ribs — the detail that makes it read as a blast
            // door rather than a slab.
            builder.CurrentColor = tint * 0.82f;
            int ribs = Mathf.Max(2, Mathf.RoundToInt(size.y / 0.55f));
            for (int i = 0; i < ribs; i++)
            {
                float t = (i + 0.5f) / ribs;
                float y = Mathf.Lerp(-size.y * 0.5f, size.y * 0.5f, t);
                builder.AddBox(new Vector3(0f, y, -size.z * 0.5f - 0.03f),
                    new Vector3(size.x * 0.94f, 0.1f, 0.07f));
            }

            // Edge seal and a vision slit nobody should have looked through.
            builder.CurrentColor = tint * 0.6f;
            builder.AddBox(new Vector3(0f, -size.y * 0.5f, 0f), new Vector3(size.x, 0.08f, size.z * 1.1f));
            builder.AddBox(new Vector3(0f, size.y * 0.22f, -size.z * 0.5f - 0.02f),
                new Vector3(size.x * 0.3f, 0.14f, 0.05f));
        }

        private void BuildBolts(MeshBuilder builder)
        {
            for (int i = -1; i <= 1; i += 2)
            {
                builder.AddCylinder(new Vector3(i * size.x * 0.35f, -size.y * 0.5f, 0f),
                    size.z * 0.5f, size.y, 8);
            }
        }

        private void BuildLattice(MeshBuilder builder)
        {
            const int bars = 7;
            float barThickness = 0.05f;

            for (int i = 0; i < bars; i++)
            {
                float t = bars == 1 ? 0.5f : i / (float)(bars - 1);

                builder.AddBox(new Vector3(Mathf.Lerp(-size.x * 0.5f, size.x * 0.5f, t), 0f, 0f),
                    new Vector3(barThickness, size.y * 0.2f, size.z));

                builder.AddBox(new Vector3(0f, 0f, Mathf.Lerp(-size.z * 0.5f, size.z * 0.5f, t)),
                    new Vector3(size.x, size.y * 0.18f, barThickness));
            }

            // Frame.
            builder.CurrentColor = tint * 0.75f;
            builder.AddBox(new Vector3(0f, 0f, size.z * 0.5f), new Vector3(size.x, size.y * 0.25f, 0.09f));
            builder.AddBox(new Vector3(0f, 0f, -size.z * 0.5f), new Vector3(size.x, size.y * 0.25f, 0.09f));
            builder.AddBox(new Vector3(size.x * 0.5f, 0f, 0f), new Vector3(0.09f, size.y * 0.25f, size.z));
            builder.AddBox(new Vector3(-size.x * 0.5f, 0f, 0f), new Vector3(0.09f, size.y * 0.25f, size.z));
        }

        private void BuildHousing(MeshBuilder builder)
        {
            builder.AddCylinder(new Vector3(0f, 0f, 0f), size.x * 0.5f, size.z, 12,
                Quaternion.Euler(90f, 0f, 0f), topRadiusScale: 1.15f);

            // Wire guard over the lens.
            builder.CurrentColor = tint * 0.7f;
            for (int i = 0; i < 4; i++)
            {
                builder.AddCylinder(new Vector3(0f, 0f, size.z),
                    0.012f, size.x, 5, Quaternion.Euler(0f, 0f, i * 45f) * Quaternion.Euler(0f, 0f, 90f));
            }
        }

        /// <summary>Sets the panel dimensions before generation. Used by the scene builder.</summary>
        public void Configure(FixtureMesh kind, Vector3 dimensions, SurfaceKind surfaceKind, Color colour)
        {
            mesh = kind;
            size = dimensions;
            surface = surfaceKind;
            tint = colour;
        }
    }
}
