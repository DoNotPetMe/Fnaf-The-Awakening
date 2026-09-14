using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Grotto.Core;

namespace Grotto.Editor
{
    /// <summary>
    /// Creates and configures the render pipeline.
    ///
    /// Until now the repository shipped URP-targeted shaders and no pipeline asset,
    /// which meant a fresh clone opened in the built-in pipeline and rendered the
    /// whole cave magenta. That is the single worst first impression a project can
    /// make, and "go and make a URP asset yourself" is not an answer when every
    /// setting on it is a decision this game has an opinion about.
    ///
    /// So the pipeline is a build product like everything else here: three quality
    /// tiers, each with its own renderer, all configured for one specific kind of
    /// scene — a dark interior lit by a handful of small, moving, shadow-casting
    /// lamps, viewed from a fixed seat.
    ///
    /// The decisions that matter, and why:
    ///
    ///   * <b>HDR on, with the post stack doing the tonemapping.</b> A cap lamp at
    ///     intensity 3.2 in a room whose ambient is 0.05 is a 60:1 range. In LDR the
    ///     lamp clips to white and the rock under it loses all texture.
    ///   * <b>Depth and opaque textures on.</b> The water surface refracts what is
    ///     behind it and the fog reads depth; without these both silently no-op.
    ///   * <b>Soft shadows, one cascade, short distance.</b> Nothing in this game is
    ///     more than about forty metres from the camera, so spending cascades on
    ///     distance is spending them on nothing. One cascade at 45m puts the entire
    ///     shadow map budget on the room you are actually in.
    ///   * <b>SSAO.</b> Contact shadow is most of what makes a generated cave read as
    ///     rock rather than as grey plastic, and it is the cheapest way to get it.
    ///   * <b>Additional lights per object raised to 8.</b> The station has a cap
    ///     lamp, two approach floodlights, a chase floodlight, the monitor glow and
    ///     the generator bay; the URP default of 4 drops half of them.
    /// </summary>
    public static class RenderPipelineBuilder
    {
        public const string SettingsFolder = "Assets/Game/Settings/Rendering";

        private readonly struct Tier
        {
            public readonly string Name;
            public readonly int ShadowResolution;
            public readonly float ShadowDistance;
            public readonly bool SoftShadows;
            public readonly bool Ssao;
            public readonly float RenderScale;
            public readonly int AdditionalLights;

            public Tier(string name, int shadowResolution, float shadowDistance,
                bool softShadows, bool ssao, float renderScale, int additionalLights)
            {
                Name = name;
                ShadowResolution = shadowResolution;
                ShadowDistance = shadowDistance;
                SoftShadows = softShadows;
                Ssao = ssao;
                RenderScale = renderScale;
                AdditionalLights = additionalLights;
            }
        }

        // Ordered low to high, matching how Unity indexes quality levels.
        private static readonly Tier[] Tiers =
        {
            new Tier("Low", 1024, 28f, false, false, 0.85f, 4),
            new Tier("Medium", 2048, 40f, true, true, 1f, 8),
            new Tier("High", 4096, 55f, true, true, 1f, 8)
        };

        [MenuItem("Tools/Grotto/Rebuild Render Pipeline", priority = 21)]
        public static void RebuildWithPrompt()
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Rebuild render pipeline",
                "This creates three URP assets (Low, Medium, High) under " +
                SettingsFolder + ", assigns them to the project's quality levels, and " +
                "sets the default pipeline.\n\n" +
                "Existing assets are reconfigured in place.\n\nContinue?",
                "Rebuild", "Cancel");

            if (proceed) Rebuild();
        }

        public static void Rebuild()
        {
            Directory.CreateDirectory(SettingsFolder);
            AssetDatabase.Refresh();

            var assets = new List<UniversalRenderPipelineAsset>(Tiers.Length);

            foreach (var tier in Tiers)
            {
                var renderer = BuildRenderer(tier);
                var pipeline = BuildPipeline(tier, renderer);
                assets.Add(pipeline);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AssignToQualityLevels(assets);

            // The middle tier is the one the game is tuned against.
            GraphicsSettings.defaultRenderPipeline = assets[1];

            AssetDatabase.SaveAssets();

            GLog.Info(LogChannel.Rendering,
                $"Render pipeline rebuilt: {assets.Count} tiers, default '{assets[1].name}'.");
        }

        // =====================================================================
        // Renderer
        // =====================================================================

        private static UniversalRendererData BuildRenderer(Tier tier)
        {
            string path = $"{SettingsFolder}/Renderer_{tier.Name}.asset";

            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, path);
            }

            // Forward+ clusters lights, which is what lets a room hold eight small
            // lamps without the per-object limit deciding which ones you get.
            data.renderingMode = RenderingMode.ForwardPlus;
            data.depthPrimingMode = DepthPrimingMode.Disabled;   // costs more than it saves here
            data.shadowTransparentReceive = true;

            data.rendererFeatures.RemoveAll(f => f == null);
            SetSsao(data, tier.Ssao);

            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>
        /// Adds or removes the screen-space ambient occlusion feature.
        ///
        /// URP's ScreenSpaceAmbientOcclusion type and its settings struct are both
        /// internal, so this goes through reflection. That is unpleasant, and it is
        /// still better than the alternative — a hand-authored .asset committed to the
        /// repository, which is exactly the unreviewable YAML this project avoids
        /// everywhere else. Every field is set defensively: a rename in a future URP
        /// leaves SSAO at its own defaults rather than throwing.
        /// </summary>
        private static void SetSsao(UniversalRendererData data, bool wanted)
        {
            const string typeName = "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion";
            var ssaoType = typeof(UniversalRendererData).Assembly.GetType(typeName);

            var existing = data.rendererFeatures.Find(
                f => f != null && f.GetType().FullName == typeName);

            if (!wanted)
            {
                if (existing != null)
                {
                    data.rendererFeatures.Remove(existing);
                    AssetDatabase.RemoveObjectFromAsset(existing);
                    Object.DestroyImmediate(existing, allowDestroyingAssets: true);
                }
                return;
            }

            if (ssaoType == null)
            {
                GLog.Warn(LogChannel.Rendering,
                    "URP's screen-space ambient occlusion feature was not found; the cave " +
                    "will render without contact shadow. Add it by hand on the renderer asset.");
                return;
            }

            if (existing == null)
            {
                existing = (ScriptableRendererFeature)ScriptableObject.CreateInstance(ssaoType);
                existing.name = "Screen Space Ambient Occlusion";
                data.rendererFeatures.Add(existing);
                AssetDatabase.AddObjectToAsset(existing, data);
            }

            ConfigureSsao(existing, ssaoType);
            EditorUtility.SetDirty(existing);
        }

        private static void ConfigureSsao(ScriptableRendererFeature feature, System.Type ssaoType)
        {
            var settingsField = ssaoType.GetField("m_Settings",
                BindingFlags.Instance | BindingFlags.NonPublic);

            object settings = settingsField?.GetValue(feature);
            if (settings == null) return;

            // Tuned for rock: a wide radius so whole crevices darken rather than only
            // the last centimetre, and a strong falloff so distant geometry is not
            // smeared. Half resolution because the effect is low frequency and the
            // full-resolution version buys nothing you can see.
            TrySet(settings, "Intensity", 1.35f);
            TrySet(settings, "Radius", 0.45f);
            TrySet(settings, "Falloff", 90f);
            TrySet(settings, "Downsample", true);
            TrySet(settings, "AfterOpaque", false);
            TrySet(settings, "BlurQuality", 0);          // high
            TrySet(settings, "Samples", 1);              // medium
            TrySet(settings, "DirectLightingStrength", 0.32f);

            // The normals source enum is internal too; 1 is "depth normals", which is
            // sharper than reconstructing from depth alone.
            TrySetEnum(settings, "Source", 1);

            settingsField.SetValue(feature, settings);
        }

        private static void TrySet(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field == null) return;

            try
            {
                if (field.FieldType == typeof(float)) field.SetValue(target, System.Convert.ToSingle(value));
                else if (field.FieldType == typeof(int)) field.SetValue(target, System.Convert.ToInt32(value));
                else if (field.FieldType == typeof(bool)) field.SetValue(target, System.Convert.ToBoolean(value));
                else field.SetValue(target, value);
            }
            catch (System.Exception ex)
            {
                GLog.Warn(LogChannel.Rendering, $"Could not set SSAO '{fieldName}': {ex.Message}");
            }
        }

        private static void TrySetEnum(object target, string fieldName, int value)
        {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field == null || !field.FieldType.IsEnum) return;

            try { field.SetValue(target, System.Enum.ToObject(field.FieldType, value)); }
            catch { /* a renamed enum member is not worth failing the build over */ }
        }

        // =====================================================================
        // Pipeline asset
        // =====================================================================

        private static UniversalRenderPipelineAsset BuildPipeline(Tier tier, UniversalRendererData renderer)
        {
            string path = $"{SettingsFolder}/URP_{tier.Name}.asset";

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                // CreateInstance rather than UniversalRenderPipelineAsset.Create: the
                // renderer list is written through SerializedObject below anyway, and
                // this does not depend on a helper whose signature has moved between
                // URP versions.
                asset = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var serialized = new SerializedObject(asset);

            // The renderer list is not exposed as a property, so it goes through the
            // serialised object like everything else the scene builder writes.
            var rendererList = serialized.FindProperty("m_RendererDataList");
            if (rendererList != null)
            {
                rendererList.arraySize = 1;
                rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            }

            Set(serialized, "m_DefaultRendererIndex", 0);

            // --- Quality -------------------------------------------------------
            SetBool(serialized, "m_SupportsHDR", true);
            Set(serialized, "m_MSAA", tier.Ssao ? 4 : 2);
            SetFloat(serialized, "m_RenderScale", tier.RenderScale);

            // --- Lighting ------------------------------------------------------
            SetBool(serialized, "m_MainLightShadowsSupported", true);
            Set(serialized, "m_MainLightShadowmapResolution", tier.ShadowResolution);

            // 1 = per-pixel. Vertex lighting on a cave wall made of long thin
            // triangles produces banding you can read the topology off.
            Set(serialized, "m_AdditionalLightsRenderingMode", 1);
            Set(serialized, "m_AdditionalLightsPerObjectLimit", tier.AdditionalLights);
            SetBool(serialized, "m_AdditionalLightShadowsSupported", true);
            Set(serialized, "m_AdditionalLightsShadowmapResolution", tier.ShadowResolution / 2);

            SetBool(serialized, "m_SupportsSoftShadows", tier.SoftShadows);

            // --- Shadows -------------------------------------------------------
            SetFloat(serialized, "m_ShadowDistance", tier.ShadowDistance);
            Set(serialized, "m_ShadowCascadeCount", 1);
            SetFloat(serialized, "m_ShadowDepthBias", 1.1f);
            SetFloat(serialized, "m_ShadowNormalBias", 0.9f);

            // --- Textures the effects depend on ---------------------------------
            SetBool(serialized, "m_RequireDepthTexture", true);
            SetBool(serialized, "m_RequireOpaqueTexture", true);

            // --- Colour --------------------------------------------------------
            // 1 = high quality (Log-space) grading, which is what makes the ACES
            // tonemapper's shoulder smooth instead of banded on a dark image.
            Set(serialized, "m_ColorGradingMode", 1);
            Set(serialized, "m_ColorGradingLutSize", 32);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            return asset;
        }

        // =====================================================================
        // Quality levels
        // =====================================================================

        private static void AssignToQualityLevels(List<UniversalRenderPipelineAsset> assets)
        {
            int levels = QualitySettings.names.Length;
            if (levels == 0)
            {
                GLog.Warn(LogChannel.Rendering, "The project has no quality levels to assign to.");
                return;
            }

            int original = QualitySettings.GetQualityLevel();

            for (int i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);

                // Map however many quality levels the project has onto our three tiers.
                int tier = levels == 1 ? 1 : Mathf.RoundToInt(i / (float)(levels - 1) * (assets.Count - 1));
                QualitySettings.renderPipeline = assets[Mathf.Clamp(tier, 0, assets.Count - 1)];
            }

            QualitySettings.SetQualityLevel(original, applyExpensiveChanges: false);
        }

        // =====================================================================
        // Serialised field helpers
        // =====================================================================

        private static void Set(SerializedObject target, string path, int value)
        {
            var property = target.FindProperty(path);
            if (property == null) { Missing(path); return; }
            property.intValue = value;
        }

        private static void SetFloat(SerializedObject target, string path, float value)
        {
            var property = target.FindProperty(path);
            if (property == null) { Missing(path); return; }
            property.floatValue = value;
        }

        private static void SetBool(SerializedObject target, string path, bool value)
        {
            var property = target.FindProperty(path);
            if (property == null) { Missing(path); return; }
            property.boolValue = value;
        }

        private static void Missing(string path)
        {
            // A URP upgrade that renames a field should leave that one setting at its
            // default with a note, not abort the rebuild.
            GLog.Warn(LogChannel.Rendering,
                $"URP asset has no serialised field '{path}'; leaving it at the default. " +
                "The pipeline builder and this URP version have drifted apart.");
        }
    }
}
