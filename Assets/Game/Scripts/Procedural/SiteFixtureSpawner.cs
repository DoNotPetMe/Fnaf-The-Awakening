using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Procedural
{
    /// <summary>
    /// Builds the control room's fixtures and the surveillance cameras at load, from
    /// whichever site is actually being played.
    ///
    /// These used to be baked into the scene by the editor's scene builder, which was
    /// fine while there was one map. It stops working the moment the player can pick a
    /// site from the menu: a blast door placed at the grotto's station is in the wrong
    /// building, and a camera per node is the wrong *number* of cameras.
    ///
    /// So the scene keeps only what every site shares — the seat, the head pivot, the
    /// camera rig, the interface — and everything that depends on the map is built
    /// here, in the same spirit as the geometry. The .unity file gets smaller and more
    /// site-agnostic in the process, which is the right direction for it anyway.
    ///
    /// Runs at -820: after <see cref="FacilityRuntime"/> has a graph (-850), before the
    /// geometry spawner (-800) and well before anything that looks barriers up.
    /// </summary>
    [DefaultExecutionOrder(-820)]
    [DisallowMultipleComponent]
    public sealed class SiteFixtureSpawner : MonoBehaviour
    {
        private const string FixtureRootName = "[Fixtures]";
        private const string CameraRootName = "[Cameras]";

        [Header("Doors")]
        [SerializeField] private Vector3 doorPanelSize = new Vector3(3.3f, 3.1f, 0.24f);
        [SerializeField] private float doorTravel = 3.25f;

        [Header("Cameras")]
        [SerializeField] private int cameraWidth = 480;
        [SerializeField] private int cameraHeight = 360;
        [SerializeField] private float cameraFov = 72f;

        private GameObject _fixtureRoot;
        private GameObject _cameraRoot;

        private void Awake() => Build();

        private void OnDestroy() => Clear();

        public void Build()
        {
            Clear();

            var runtime = FacilityRuntime.Instance;
            var layout = runtime != null ? runtime.Layout : FacilityLayout.LoadDefault();
            if (layout == null)
            {
                GLog.Error(LogChannel.Procedural, "No layout; the station has no fixtures.");
                return;
            }

            var graph = runtime != null ? runtime.Graph : layout.BuildGraph();

            // Built inactive and switched on at the end. A component added to a live
            // GameObject runs Awake inside AddComponent, which would be *before* the
            // Configure call that tells it which barrier it is — so nothing here is
            // allowed to wake until the whole subtree is wired.
            _fixtureRoot = new GameObject(FixtureRootName);
            _fixtureRoot.SetActive(false);
            _fixtureRoot.transform.SetParent(transform, worldPositionStays: false);

            _cameraRoot = new GameObject(CameraRootName);
            _cameraRoot.SetActive(false);
            _cameraRoot.transform.SetParent(transform, worldPositionStays: false);

            BuildFixtures(graph, layout, _fixtureRoot.transform);
            BuildCameras(graph, _cameraRoot.transform);

            _fixtureRoot.SetActive(true);
            _cameraRoot.SetActive(true);
        }

        public void Clear()
        {
            DestroyRoot(ref _fixtureRoot, FixtureRootName);
            DestroyRoot(ref _cameraRoot, CameraRootName);
        }

        private void DestroyRoot(ref GameObject root, string name)
        {
            if (root == null)
            {
                var existing = transform.Find(name);
                if (existing != null) root = existing.gameObject;
            }
            if (root == null) return;

            if (Application.isPlaying) Destroy(root);
            else DestroyImmediate(root);

            root = null;
        }

        // =====================================================================
        // Fixtures
        // =====================================================================

        private void BuildFixtures(FacilityGraph graph, FacilityLayout layout, Transform parent)
        {
            var station = graph.Node(graph.StationNode);
            if (station == null)
            {
                GLog.Error(LogChannel.Procedural, $"Site '{layout.siteId}' has no station node.");
                return;
            }

            var origin = station.Position;
            var wiring = layout.wiring;
            var tint = PaletteTint(layout.palette);

            // Placed by role rather than by node id, so any site that wires the four
            // approaches gets a correctly built control room with no code change.
            BuildDoor(parent, "Door_North", FacilityBarriers.DoorNorth, wiring.northApproach,
                "North blast door", origin + new Vector3(0f, 0f, station.Size.z * 0.5f), 0f, tint);

            BuildDoor(parent, "Door_South", FacilityBarriers.DoorSouth, wiring.southApproach,
                "South blast door", origin + new Vector3(0f, 0f, -station.Size.z * 0.5f), 180f, tint);

            BuildGrate(parent, wiring.sump,
                origin + new Vector3(0f, -station.Size.y * 0.35f + 0.05f, -1f));

            BuildFloodlight(parent, wiring.chase, "Chase floodlight",
                origin + new Vector3(0f, station.Size.y * 0.5f, 2.4f), Vector3.up, 46f, 2.6f);

            BuildFloodlight(parent, wiring.northApproach, "North approach floodlight",
                origin + new Vector3(0f, 1.9f, station.Size.z * 0.5f + 1.2f), Vector3.forward, 62f, 2.2f);

            BuildFloodlight(parent, wiring.southApproach, "South approach floodlight",
                origin + new Vector3(0f, 1.9f, -station.Size.z * 0.5f - 1.2f), Vector3.back, 62f, 2.2f);
        }

        /// <summary>
        /// The door and housing colour for a site. A limestone show cave got whatever
        /// paint the county had; a hydro station is institutional grey-green; a grain
        /// terminal is galvanised and was never painted at all.
        /// </summary>
        private static Color PaletteTint(SitePalette palette)
        {
            switch (palette)
            {
                case SitePalette.Concrete: return new Color(0.36f, 0.40f, 0.38f);
                case SitePalette.Steel: return new Color(0.50f, 0.50f, 0.52f);
                default: return new Color(0.42f, 0.40f, 0.36f);
            }
        }

        private void BuildDoor(Transform parent, string objectName, string barrierId,
            string noiseNode, string displayName, Vector3 position, float yaw, Color tint)
        {
            if (string.IsNullOrWhiteSpace(noiseNode)) return;

            var root = new GameObject(objectName);
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            // The leaf starts up in its housing and travels down to seal.
            var panel = new GameObject("Panel");
            panel.transform.SetParent(root.transform, worldPositionStays: false);
            panel.transform.localPosition = new Vector3(0f, doorTravel + 0.1f, 0f);

            var mesh = panel.AddComponent<ProceduralMeshSpawner>();
            mesh.Configure(FixtureMesh.BlastDoorPanel, doorPanelSize, SurfaceKind.SteelPainted, tint);

            var door = root.AddComponent<BlastDoor>();
            door.Configure(barrierId, noiseNode, displayName, panel.transform,
                new Vector3(0f, -doorTravel, 0f), startClosed: false);
        }

        private void BuildGrate(Transform parent, string sumpNode, Vector3 position)
        {
            if (string.IsNullOrWhiteSpace(sumpNode)) return;

            var root = new GameObject("SumpGrate");
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.position = position;

            var lattice = new GameObject("Lattice");
            lattice.transform.SetParent(root.transform, worldPositionStays: false);
            lattice.AddComponent<ProceduralMeshSpawner>().Configure(
                FixtureMesh.GrateLattice, new Vector3(1.6f, 0.12f, 1.6f),
                SurfaceKind.SteelRusted, new Color(0.40f, 0.31f, 0.24f));

            var bolts = new GameObject("Bolts");
            bolts.transform.SetParent(root.transform, worldPositionStays: false);
            bolts.transform.localPosition = new Vector3(0f, -0.08f, 0f);
            bolts.AddComponent<ProceduralMeshSpawner>().Configure(
                FixtureMesh.GrateBolts, new Vector3(1.5f, 0.22f, 0.09f),
                SurfaceKind.SteelPainted, new Color(0.55f, 0.52f, 0.48f));

            var grate = root.AddComponent<SumpGrate>();
            grate.Configure(FacilityBarriers.SumpGrate, sumpNode, "Sump grate bolts",
                bolts.transform, new Vector3(0f, 0.1f, 0f), swimmerResistance: 0.35f);
        }

        private void BuildFloodlight(Transform parent, string nodeId, string displayName,
            Vector3 position, Vector3 direction, float angle, float intensity)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return;

            var root = new GameObject("Floodlight_" + nodeId);
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(direction.normalized, Vector3.up));

            var housing = new GameObject("Housing");
            housing.transform.SetParent(root.transform, worldPositionStays: false);
            housing.AddComponent<ProceduralMeshSpawner>().Configure(
                FixtureMesh.FloodlightHousing, new Vector3(0.32f, 0.32f, 0.26f),
                SurfaceKind.SteelRusted, new Color(0.38f, 0.34f, 0.30f));

            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(root.transform, worldPositionStays: false);
            lightObject.transform.localPosition = new Vector3(0f, 0f, 0.25f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.5f;
            light.range = 22f;
            light.intensity = intensity;
            light.color = new Color(1f, 0.92f, 0.78f);
            light.shadows = LightShadows.Soft;
            light.enabled = false;

            root.AddComponent<Floodlight>().Configure(nodeId, displayName, new[] { light },
                flicker: true, flickerAmount: 0.08f);
        }

        // =====================================================================
        // Cameras
        // =====================================================================

        private void BuildCameras(FacilityGraph graph, Transform parent)
        {
            int built = 0;

            foreach (var node in graph.Nodes)
            {
                if (!node.HasCamera) continue;

                var go = new GameObject("Cam_" + node.Id);
                go.transform.SetParent(parent, worldPositionStays: false);

                // High in a corner, looking back across the space — the angle a real
                // installer would pick, and the one that shows the most floor.
                var offset = new Vector3(
                    node.Size.x * 0.34f,
                    node.Size.y * 0.30f,
                    -node.Size.z * 0.38f);

                go.transform.position = node.Position + offset;
                go.transform.rotation = Quaternion.LookRotation(
                    (node.Position - go.transform.position).normalized, Vector3.up);

                var camera = go.AddComponent<Camera>();
                camera.fieldOfView = cameraFov;
                camera.nearClipPlane = 0.08f;
                camera.farClipPlane = 60f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.depth = -10;
                camera.enabled = false;          // SurveillanceSystem switches these on

                go.AddComponent<CameraNode>().Configure(node.Id.Key, cameraWidth, cameraHeight,
                    sweep: node.Kind == NodeKind.Cavern, sweepDegrees: 22f, sweepSeconds: 14f);

                built++;
            }

            GLog.Info(LogChannel.Procedural, $"Surveillance built: {built} camera(s).");
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild fixtures (not saved)")]
        public void RebuildInEditor()
        {
            Build();
            MarkPreview(_fixtureRoot);
            MarkPreview(_cameraRoot);
        }

        private static void MarkPreview(GameObject root)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave;
        }
#endif
    }
}
