using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Grotto.AI;
using Grotto.Audio;
using Grotto.Core;
using Grotto.DevTools;
using Grotto.Facility;
using Grotto.Player;
using Grotto.Procedural;
using Grotto.Rendering;
using Grotto.UI;

namespace Grotto.Editor
{
    /// <summary>
    /// Builds the entire playable scene from the layout, in one menu item.
    ///
    /// This is the piece that makes the rest of the architecture pay off. Because the
    /// geometry, the navigation graph, the camera placements and the map all derive
    /// from one layout asset, the scene is not a hand-assembled artefact that drifts
    /// out of step with the data — it is a *build product*. Change the layout, rebuild,
    /// and everything follows.
    ///
    /// The scene it writes contains no meshes and no materials: every piece of
    /// geometry is produced at load time by a spawner component. The .unity file
    /// therefore stays small, text-only and reviewable in a pull request, which is not
    /// something you can usually say about a level.
    /// </summary>
    public static class FacilitySceneBuilder
    {
        public const string ScenesFolder = "Assets/Game/Scenes";
        public const string ScenePath = ScenesFolder + "/Facility.unity";

        [MenuItem("Tools/Grotto/Build Facility Scene", priority = 1)]
        public static void BuildWithPrompt()
        {
            bool exists = File.Exists(ScenePath);

            bool proceed = EditorUtility.DisplayDialog(
                "Build facility scene",
                (exists
                    ? "This replaces Assets/Game/Scenes/Facility.unity.\n\n"
                    : "This creates Assets/Game/Scenes/Facility.unity.\n\n") +
                "Settings assets are rebuilt first. Anything you placed in the scene by hand " +
                "will be lost.\n\nContinue?",
                "Build", "Cancel");

            if (!proceed) return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Build();
        }

        public static void Build()
        {
            SettingsAssetBuilder.Rebuild();

            var layout = AssetDatabase.LoadAssetAtPath<FacilityLayout>(
                $"{SettingsAssetBuilder.ResourcesPath}/FacilityLayout_GrottoSprings.asset");
            var tuning = AssetDatabase.LoadAssetAtPath<FacilityTuning>(
                $"{SettingsAssetBuilder.ResourcesPath}/FacilityTuning.asset");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(
                $"{SettingsAssetBuilder.ResourcesPath}/GameConfig.asset");

            if (layout == null || tuning == null || config == null)
            {
                EditorUtility.DisplayDialog("Build failed",
                    "Settings assets are missing. Run Tools > Grotto > Rebuild Settings Assets first.", "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var graph = layout.BuildGraph();

            var systems = BuildSystems(layout, tuning, config);
            var station = BuildStation(graph, layout, tuning);
            BuildFixtures(graph, station.transform);
            BuildCameras(graph);
            var cast = BuildCast(graph);
            WireDirector(systems, cast);
            BuildInterface();
            BuildDevTools();

            SetupRenderSettings();

            Directory.CreateDirectory(ScenesFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();

            AssetDatabase.Refresh();

            GLog.Info(LogChannel.Core,
                $"Facility scene built: {graph.NodeCount} nodes, {cast.Count} characters. " +
                "Press Play — the geometry generates on load.");

            EditorUtility.DisplayDialog("Facility scene built",
                $"Grotto Springs is ready.\n\n" +
                $"{graph.NodeCount} nodes, {graph.Links.Count} links, {cast.Count} characters.\n\n" +
                "Press Play. The cave generates on load, which takes about a second.\n\n" +
                "Backtick opens the dev console; F3 opens the overlay.", "OK");
        }

        // =====================================================================
        // Systems
        // =====================================================================

        private static GameObject BuildSystems(FacilityLayout layout, FacilityTuning tuning, GameConfig config)
        {
            var root = new GameObject("[Systems]");

            var bootstrap = root.AddComponent<GameBootstrap>();
            Set(bootstrap, "config", config);
            Set(bootstrap, "dontDestroyOnLoad", false);   // single-scene build; nothing to survive

            var night = root.AddComponent<NightController>();
            Set(night, "config", config);
            Set(night, "overrideStartNight", 0);

            var facility = root.AddComponent<FacilityRuntime>();
            Set(facility, "layout", layout);
            Set(facility, "tuning", tuning);
            Set(facility, "standaloneSecondsPerHour", 60f);

            var geometry = new GameObject("Geometry");
            geometry.transform.SetParent(root.transform, worldPositionStays: false);

            var spawner = geometry.AddComponent<FacilityGeometrySpawner>();
            Set(spawner, "layout", layout);
            Set(spawner, "seed", 1337);

            root.AddComponent<AIDirector>();
            root.AddComponent<PhantomCotton>();

            var audio = new GameObject("Audio");
            audio.transform.SetParent(root.transform, worldPositionStays: false);
            audio.AddComponent<AudioDirector>();

            return root;
        }

        // =====================================================================
        // Station
        // =====================================================================

        private static GameObject BuildStation(FacilityGraph graph, FacilityLayout layout, FacilityTuning tuning)
        {
            var stationNode = graph.Node(graph.StationNode);
            var origin = stationNode != null ? stationNode.Position : Vector3.zero;

            var root = new GameObject("[Station]");
            root.transform.position = origin;

            // Seated eye height, a little back from the desk.
            var pivot = new GameObject("HeadPivot");
            pivot.transform.SetParent(root.transform, worldPositionStays: false);
            pivot.transform.localPosition = new Vector3(0f, 1.25f, -0.9f);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(pivot.transform, worldPositionStays: false);
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 90f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<PostFxController>();
            cameraObject.AddComponent<StationAtmosphere>();

            // Cap lamp: a wide, weak spot on the camera itself.
            var lampObject = new GameObject("Cap Lamp");
            lampObject.transform.SetParent(cameraObject.transform, worldPositionStays: false);

            var lampLight = lampObject.AddComponent<Light>();
            lampLight.type = LightType.Spot;
            lampLight.spotAngle = 78f;
            lampLight.innerSpotAngle = 30f;
            lampLight.range = 16f;
            lampLight.intensity = 3.2f;
            lampLight.color = new Color(1f, 0.94f, 0.82f);
            lampLight.shadows = LightShadows.Soft;
            lampLight.enabled = false;

            var headlamp = lampObject.AddComponent<Headlamp>();
            Set(headlamp, "lampLight", lampLight);

            var station = root.AddComponent<StationController>();
            Set(station, "headPivot", pivot.transform);
            Set(station, "headlamp", headlamp);

            return root;
        }

        // =====================================================================
        // Fixtures
        // =====================================================================

        private static void BuildFixtures(FacilityGraph graph, Transform stationRoot)
        {
            var station = graph.Node(graph.StationNode);
            if (station == null) return;

            var origin = station.Position;

            BuildDoor(stationRoot, "Door_North", GrottoSpringsLayout.DoorNorth, "ADIT_N",
                "North blast door", origin + new Vector3(0f, 0f, station.Size.z * 0.5f), 0f);

            BuildDoor(stationRoot, "Door_South", GrottoSpringsLayout.DoorSouth, "ADIT_S",
                "South blast door", origin + new Vector3(0f, 0f, -station.Size.z * 0.5f), 180f);

            BuildGrate(stationRoot, origin + new Vector3(0f, -station.Size.y * 0.35f + 0.05f, -1f));

            BuildFloodlight(stationRoot, "CHASE", "Chase floodlight",
                origin + new Vector3(0f, station.Size.y * 0.5f, 2.4f), Vector3.up, 46f, 2.6f);

            BuildFloodlight(stationRoot, "ADIT_N", "North adit floodlight",
                origin + new Vector3(0f, 1.9f, station.Size.z * 0.5f + 1.2f), Vector3.forward, 62f, 2.2f);

            BuildFloodlight(stationRoot, "ADIT_S", "South adit floodlight",
                origin + new Vector3(0f, 1.9f, -station.Size.z * 0.5f - 1.2f), Vector3.back, 62f, 2.2f);
        }

        private static void BuildDoor(Transform parent, string objectName, string barrierId,
            string noiseNode, string displayName, Vector3 position, float yaw)
        {
            var root = new GameObject(objectName);
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            root.tag = "BlastDoor";

            var panelSize = new Vector3(3.3f, 3.1f, 0.24f);

            // The leaf starts up in its housing and travels down to seal.
            var panel = new GameObject("Panel");
            panel.transform.SetParent(root.transform, worldPositionStays: false);
            panel.transform.localPosition = new Vector3(0f, 3.35f, 0f);

            var meshSpawner = panel.AddComponent<ProceduralMeshSpawner>();
            meshSpawner.Configure(FixtureMesh.BlastDoorPanel, panelSize,
                SurfaceKind.SteelPainted, new Color(0.42f, 0.40f, 0.36f));
            EditorUtility.SetDirty(meshSpawner);

            var door = root.AddComponent<BlastDoor>();
            Set(door, "barrierId", barrierId);
            Set(door, "noiseNodeId", noiseNode);
            Set(door, "displayName", displayName);
            Set(door, "panel", panel.transform);
            Set(door, "closedLocalOffset", new Vector3(0f, -3.25f, 0f));
            Set(door, "startClosed", false);
        }

        private static void BuildGrate(Transform parent, Vector3 position)
        {
            var root = new GameObject("SumpGrate");
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.position = position;

            var lattice = new GameObject("Lattice");
            lattice.transform.SetParent(root.transform, worldPositionStays: false);

            var latticeMesh = lattice.AddComponent<ProceduralMeshSpawner>();
            latticeMesh.Configure(FixtureMesh.GrateLattice, new Vector3(1.6f, 0.12f, 1.6f),
                SurfaceKind.SteelRusted, new Color(0.40f, 0.31f, 0.24f));
            EditorUtility.SetDirty(latticeMesh);

            var bolts = new GameObject("Bolts");
            bolts.transform.SetParent(root.transform, worldPositionStays: false);
            bolts.transform.localPosition = new Vector3(0f, -0.08f, 0f);

            var boltMesh = bolts.AddComponent<ProceduralMeshSpawner>();
            boltMesh.Configure(FixtureMesh.GrateBolts, new Vector3(1.5f, 0.22f, 0.09f),
                SurfaceKind.SteelPainted, new Color(0.55f, 0.52f, 0.48f));
            EditorUtility.SetDirty(boltMesh);

            var grate = root.AddComponent<SumpGrate>();
            Set(grate, "barrierId", GrottoSpringsLayout.SumpGrate);
            Set(grate, "noiseNodeId", "SUMP");
            Set(grate, "displayName", "Sump grate bolts");
            Set(grate, "boltsTransform", bolts.transform);
            Set(grate, "lockedLocalOffset", new Vector3(0f, 0.1f, 0f));
            Set(grate, "swimmerResistance", 0.35f);
        }

        private static void BuildFloodlight(Transform parent, string nodeId, string displayName,
            Vector3 position, Vector3 direction, float angle, float intensity)
        {
            var root = new GameObject("Floodlight_" + nodeId);
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

            var housing = new GameObject("Housing");
            housing.transform.SetParent(root.transform, worldPositionStays: false);

            var housingMesh = housing.AddComponent<ProceduralMeshSpawner>();
            housingMesh.Configure(FixtureMesh.FloodlightHousing, new Vector3(0.32f, 0.32f, 0.26f),
                SurfaceKind.SteelRusted, new Color(0.38f, 0.34f, 0.30f));
            EditorUtility.SetDirty(housingMesh);

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

            var floodlight = root.AddComponent<Floodlight>();
            Set(floodlight, "nodeId", nodeId);
            Set(floodlight, "displayName", displayName);
            SetArray(floodlight, "lights", new Object[] { light });
            Set(floodlight, "flicker", true);
            Set(floodlight, "flickerAmount", 0.08f);
        }

        // =====================================================================
        // Cameras
        // =====================================================================

        private static void BuildCameras(FacilityGraph graph)
        {
            var root = new GameObject("[Cameras]");

            foreach (var node in graph.Nodes)
            {
                if (!node.HasCamera) continue;

                var go = new GameObject("Cam_" + node.Id);
                go.transform.SetParent(root.transform, worldPositionStays: false);

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
                camera.fieldOfView = 72f;
                camera.nearClipPlane = 0.08f;
                camera.farClipPlane = 60f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.depth = -10;
                camera.enabled = false;          // SurveillanceSystem switches these on

                var cameraNode = go.AddComponent<CameraNode>();
                Set(cameraNode, "nodeId", node.Id.Key);
                Set(cameraNode, "width", 480);
                Set(cameraNode, "height", 360);
                Set(cameraNode, "sweep", node.Kind == NodeKind.Cavern);
                Set(cameraNode, "sweepDegrees", 22f);
                Set(cameraNode, "sweepSeconds", 14f);
            }
        }

        // =====================================================================
        // Cast
        // =====================================================================

        private static List<AnimatronicController> BuildCast(FacilityGraph graph)
        {
            var root = new GameObject("[Cast]");
            var controllers = new List<AnimatronicController>(8);

            var definitions = AssetDatabase.FindAssets("t:AnimatronicDefinition",
                new[] { SettingsAssetBuilder.CastPath });

            foreach (var guid in definitions)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<AnimatronicDefinition>(path);
                if (definition == null) continue;

                var go = new GameObject(definition.displayName);
                go.transform.SetParent(root.transform, worldPositionStays: false);
                go.transform.position = graph.PositionOf(new NodeId(definition.homeNode));
                go.tag = "Animatronic";

                var controller = go.AddComponent<AnimatronicController>();
                Set(controller, "definition", definition);
                Set(controller, "groundOffset", 0f);

                var spawner = go.AddComponent<AnimatronicModelSpawner>();
                Set(spawner, "definition", definition);
                Set(spawner, "addServoAnimator", true);

                controllers.Add(controller);
            }

            // Deterministic order, so a seeded night ticks the cast identically.
            controllers.Sort((a, b) => string.CompareOrdinal(a.Definition.id, b.Definition.id));
            return controllers;
        }

        private static void WireDirector(GameObject systems, List<AnimatronicController> cast)
        {
            var director = systems.GetComponent<AIDirector>();
            if (director == null) return;

            var serialized = new SerializedObject(director);
            var list = serialized.FindProperty("cast");
            list.arraySize = cast.Count;

            for (int i = 0; i < cast.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = cast[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // =====================================================================
        // Interface and dev tools
        // =====================================================================

        private static void BuildInterface()
        {
            var root = new GameObject("[Interface]");

            root.AddComponent<StationHud>();
            root.AddComponent<OverlayController>();
            root.AddComponent<MenuController>();

            // uGUI needs an event system for the camera buttons and the menus.
            var events = new GameObject("EventSystem");
            events.transform.SetParent(root.transform, worldPositionStays: false);
            events.AddComponent<UnityEngine.EventSystems.EventSystem>();
            events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private static void BuildDevTools()
        {
            var root = new GameObject("[Dev]");

            root.AddComponent<DevConsole>();
            root.AddComponent<DebugOverlay>();
            root.AddComponent<FreeCamera>();

            var gizmos = root.AddComponent<AiDebugGizmos>();
            Set(gizmos, "inspectAs", (int)TraversalMask.Walk);
            Set(gizmos, "nodeRadius", 0.8f);
        }

        private static void SetupRenderSettings()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.048f, 0.052f, 0.062f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.045f, 0.05f, 0.058f);
            RenderSettings.fogDensity = 0.022f;
            RenderSettings.skybox = null;
        }

        private static void AddToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (var entry in scenes)
                if (entry.path == ScenePath) return;

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // =====================================================================
        // Serialised field helpers
        // =====================================================================

        /// <summary>
        /// Writes a private [SerializeField] through SerializedObject.
        ///
        /// Reflection would set the field but not mark the object dirty, and the value
        /// would silently vanish on save. This is the boring, correct way.
        /// </summary>
        private static void Set(Object target, string fieldName, object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                GLog.Warn(LogChannel.Core,
                    $"{target.GetType().Name} has no serialised field '{fieldName}'. " +
                    "The scene builder and the component have drifted apart.");
                return;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = value as Object;
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = System.Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                    property.intValue = System.Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Enum:
                    property.enumValueIndex = System.Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = System.Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = value as string;
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = (Vector3)value;
                    break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = (Vector2)value;
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = (Color)value;
                    break;
                default:
                    GLog.Warn(LogChannel.Core,
                        $"Cannot set '{fieldName}' of type {property.propertyType} from the scene builder.");
                    return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string fieldName, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);

            if (property == null || !property.isArray)
            {
                GLog.Warn(LogChannel.Core, $"{target.GetType().Name} has no serialised array '{fieldName}'.");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
