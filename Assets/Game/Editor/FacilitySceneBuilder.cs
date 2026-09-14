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
    /// Builds the playable scene, in one menu item.
    ///
    /// The scene is deliberately *site-agnostic*: it holds the seat, the camera rig,
    /// the interface, the dev tools and a handful of spawner components, and nothing
    /// that names a room. Everything that depends on which map is being played — the
    /// cavern geometry, the blast doors, the surveillance cameras, the cast — is built
    /// at load by those spawners from whichever site the player picked.
    ///
    /// That is what makes three maps possible without three scenes, and it has a happy
    /// side effect: the .unity file contains no meshes, no materials and no character
    /// references, so it stays small, text-only and reviewable in a pull request, which
    /// is not something you can usually say about a level.
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
            // The pipeline first: URP-targeted shaders render magenta without one, and
            // a scene built into the built-in pipeline looks broken for a reason that
            // has nothing to do with the scene.
            RenderPipelineBuilder.Rebuild();
            SettingsAssetBuilder.Rebuild();

            var tuning = AssetDatabase.LoadAssetAtPath<FacilityTuning>(
                $"{SettingsAssetBuilder.ResourcesPath}/FacilityTuning.asset");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(
                $"{SettingsAssetBuilder.ResourcesPath}/GameConfig.asset");

            if (tuning == null || config == null)
            {
                EditorUtility.DisplayDialog("Build failed",
                    "Settings assets are missing. Run Tools > Grotto > Rebuild Settings Assets first.", "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildSystems(tuning, config);
            BuildStation();
            BuildInterface();
            BuildDevTools();

            SetupRenderSettings();

            Directory.CreateDirectory(ScenesFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();

            AssetDatabase.Refresh();

            int siteCount = SiteCatalog.Count;

            GLog.Info(LogChannel.Core,
                $"Facility scene built. {siteCount} site(s) available; the chosen one is " +
                "assembled at load.");

            EditorUtility.DisplayDialog("Facility scene built",
                $"The scene is ready and carries no map of its own.\n\n" +
                $"{siteCount} sites are available; geometry, fixtures, cameras and cast are " +
                "all built on load from whichever one is selected.\n\n" +
                "Press Play. Generation takes about a second.\n\n" +
                "Backtick opens the dev console; F3 opens the overlay.", "OK");
        }

        // =====================================================================
        // Systems
        // =====================================================================

        private static GameObject BuildSystems(FacilityTuning tuning, GameConfig config)
        {
            var root = new GameObject("[Systems]");

            var bootstrap = root.AddComponent<GameBootstrap>();
            Set(bootstrap, "config", config);
            Set(bootstrap, "dontDestroyOnLoad", false);   // single-scene build; nothing to survive

            var night = root.AddComponent<NightController>();
            Set(night, "config", config);
            Set(night, "overrideStartNight", 0);

            // No layout reference: the runtime resolves the player's chosen site at
            // Awake, and everything downstream asks it rather than the scene.
            var facility = root.AddComponent<FacilityRuntime>();
            Set(facility, "useSelectedSite", true);
            Set(facility, "tuning", tuning);
            Set(facility, "standaloneSecondsPerHour", 60f);

            var geometry = new GameObject("Geometry");
            geometry.transform.SetParent(root.transform, worldPositionStays: false);
            Set(geometry.AddComponent<FacilityGeometrySpawner>(), "seed", 1337);

            var fixtures = new GameObject("Fixtures");
            fixtures.transform.SetParent(root.transform, worldPositionStays: false);
            fixtures.AddComponent<SiteFixtureSpawner>();

            root.AddComponent<AIDirector>();
            root.AddComponent<CastSpawner>();
            root.AddComponent<PhantomCotton>();

            var audio = new GameObject("Audio");
            audio.transform.SetParent(root.transform, worldPositionStays: false);
            audio.AddComponent<AudioDirector>();

            return root;
        }

        // =====================================================================
        // Station
        // =====================================================================

        private static GameObject BuildStation()
        {
            // Left at the world origin. StationController seats the head pivot from the
            // live graph in Start, so the desk ends up in the right room whichever site
            // was chosen.
            var root = new GameObject("[Station]");

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
        // Interface and dev tools
        // =====================================================================

        private static void BuildInterface()
        {
            var root = new GameObject("[Interface]");

            root.AddComponent<StationHud>();
            root.AddComponent<OverlayController>();
            root.AddComponent<MenuController>();
            root.AddComponent<TitleStage>();

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

            // inspectAs already defaults to Walk; change it live with 'show.as'.
            var gizmos = root.AddComponent<AiDebugGizmos>();
            Set(gizmos, "nodeRadius", 0.8f);
        }

        private static void SetupRenderSettings()
        {
            // Trilight rather than flat.
            //
            // A single flat ambient term lights the top and the underside of every
            // surface identically, which is the fastest way to make a generated
            // interior look like untextured grey boxes. A gradient costs nothing and
            // gives the one thing ambient light can give for free: a direction. Cold
            // from above, because what little light reaches these places comes down
            // shafts; warmer and dimmer from below, because the floor is wet rock and
            // bounces almost nothing.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.052f, 0.062f, 0.082f);
            RenderSettings.ambientEquatorColor = new Color(0.040f, 0.042f, 0.048f);
            RenderSettings.ambientGroundColor = new Color(0.030f, 0.026f, 0.022f);
            RenderSettings.ambientIntensity = 1f;

            // Exponential squared, tuned so a forty-metre cavern fades to about half
            // at the far wall and a corridor stays legible end to end. The colour is
            // deliberately slightly blue: warm fog under warm lamps flattens the image.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.042f, 0.048f, 0.060f);
            RenderSettings.fogDensity = 0.020f;

            RenderSettings.skybox = null;

            // Reflections have nowhere to come from — there is no skybox and no probe —
            // so anything smooth would otherwise reflect Unity's default grey sky and
            // read as chrome. A near-black custom reflection keeps wet rock and painted
            // steel dark, which is what they should be.
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.25f;
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
                    // intValue, not enumValueIndex: the latter is an index into the
                    // enum's name list, which is wrong for any [Flags] enum and only
                    // coincidentally right for a contiguous one.
                    property.intValue = System.Convert.ToInt32(value);
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
    }
}
