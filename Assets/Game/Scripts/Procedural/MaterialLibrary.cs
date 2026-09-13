using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Procedural
{
    public enum SurfaceKind
    {
        CaveRock,
        CaveRockDamp,
        Shotcrete,
        SteelPainted,
        SteelRusted,
        ArcadeCarpet,
        Decking,
        Water,
        Glass,
        AnimatronicShell,
        AnimatronicMetal,
        AnimatronicFabric,
        EmissiveWarm,
        EmissiveCold
    }

    /// <summary>
    /// Named materials for the whole project, created once and shared.
    ///
    /// Each surface resolves in three steps, in order:
    ///
    ///  1. a full PBR set under <c>Resources/Art/&lt;name&gt;_Albedo|_Normal|_MaskMap</c>,
    ///     which is where the Asset Fetcher puts downloaded CC0 packs;
    ///  2. failing that, a generated albedo from <see cref="TextureFactory"/>;
    ///  3. failing even a shader, an unlit fallback so nothing renders magenta.
    ///
    /// That ordering is the whole point: the game is complete and coherent with no
    /// downloaded art at all, and gets materially better the moment real texture sets
    /// are dropped in, without a line of code changing.
    /// </summary>
    public static class MaterialLibrary
    {
        private static readonly Dictionary<SurfaceKind, Material> Cache = new Dictionary<SurfaceKind, Material>(16);

        private static Shader _litShader;
        private static Shader _transparentShader;

        private static Shader LitShader
        {
            get
            {
                if (_litShader != null) return _litShader;

                _litShader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Standard")
                             ?? Shader.Find("Unlit/Texture");

                if (_litShader == null)
                    GLog.Error(LogChannel.Procedural,
                        "No usable lit shader found. Is the Universal RP package installed and assigned?");

                return _litShader;
            }
        }

        public static Material Get(SurfaceKind kind)
        {
            if (Cache.TryGetValue(kind, out var cached) && cached != null) return cached;

            var material = Build(kind);
            Cache[kind] = material;
            return material;
        }

        /// <summary>A one-off instance of a surface, for something that needs its own colour.</summary>
        public static Material Instance(SurfaceKind kind, Color tint)
        {
            var material = new Material(Get(kind)) { name = $"{kind}_{ColorUtility.ToHtmlStringRGB(tint)}" };
            SetColor(material, tint);
            return material;
        }

        public static void ClearCache()
        {
            Cache.Clear();
            _litShader = null;
            _transparentShader = null;
        }

        private static Material Build(SurfaceKind kind)
        {
            var material = new Material(LitShader) { name = "M_" + kind };

            switch (kind)
            {
                case SurfaceKind.CaveRock:
                    Apply(material, "CaveRock", () => TextureFactory.Limestone(), metallic: 0f, smoothness: 0.12f);
                    SetTiling(material, 0.35f);
                    break;

                case SurfaceKind.CaveRockDamp:
                    Apply(material, "CaveRockDamp", () => TextureFactory.DampLimestone(), metallic: 0f, smoothness: 0.55f);
                    SetTiling(material, 0.35f);
                    break;

                case SurfaceKind.Shotcrete:
                    Apply(material, "Shotcrete", () => TextureFactory.Shotcrete(), metallic: 0f, smoothness: 0.18f);
                    SetTiling(material, 0.5f);
                    break;

                case SurfaceKind.SteelPainted:
                    Apply(material, "SteelPainted", () => TextureFactory.RustedSteel(), metallic: 0.75f, smoothness: 0.42f);
                    SetColor(material, new Color(0.62f, 0.66f, 0.64f));
                    SetTiling(material, 1f);
                    break;

                case SurfaceKind.SteelRusted:
                    Apply(material, "SteelRusted", () => TextureFactory.RustedSteel(), metallic: 0.55f, smoothness: 0.22f);
                    SetTiling(material, 1f);
                    break;

                case SurfaceKind.ArcadeCarpet:
                    Apply(material, "ArcadeCarpet", () => TextureFactory.ArcadeCarpet(), metallic: 0f, smoothness: 0.06f);
                    SetTiling(material, 0.6f);
                    break;

                case SurfaceKind.Decking:
                    Apply(material, "Decking", () => TextureFactory.Decking(), metallic: 0f, smoothness: 0.2f);
                    SetTiling(material, 0.8f);
                    break;

                case SurfaceKind.Water:
                    ApplyWater(material);
                    break;

                case SurfaceKind.Glass:
                    MakeTransparent(material, new Color(0.55f, 0.62f, 0.66f, 0.22f));
                    SetFloat(material, "_Metallic", 0.1f);
                    SetFloat(material, "_Smoothness", 0.92f);
                    break;

                case SurfaceKind.AnimatronicShell:
                    // Moulded plastic: no texture, high smoothness, colour set per character.
                    SetColor(material, new Color(0.55f, 0.34f, 0.20f));
                    SetFloat(material, "_Metallic", 0.05f);
                    SetFloat(material, "_Smoothness", 0.48f);
                    break;

                case SurfaceKind.AnimatronicMetal:
                    Apply(material, "Endoskeleton", () => TextureFactory.RustedSteel(), metallic: 0.9f, smoothness: 0.55f);
                    SetColor(material, new Color(0.52f, 0.54f, 0.57f));
                    SetTiling(material, 2f);
                    break;

                case SurfaceKind.AnimatronicFabric:
                    SetColor(material, new Color(0.36f, 0.28f, 0.22f));
                    SetFloat(material, "_Metallic", 0f);
                    SetFloat(material, "_Smoothness", 0.08f);
                    break;

                case SurfaceKind.EmissiveWarm:
                    MakeEmissive(material, new Color(1f, 0.72f, 0.38f), 2.4f);
                    break;

                case SurfaceKind.EmissiveCold:
                    MakeEmissive(material, new Color(0.45f, 0.85f, 1f), 2.0f);
                    break;
            }

            return material;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static void Apply(Material material, string resourceName,
            System.Func<Texture2D> generate, float metallic, float smoothness)
        {
            var albedo = Resources.Load<Texture2D>("Art/" + resourceName + "_Albedo");
            bool downloaded = albedo != null;

            if (!downloaded) albedo = generate();
            SetTexture(material, "_BaseMap", albedo);
            SetTexture(material, "_MainTex", albedo);      // Standard shader fallback

            if (downloaded)
            {
                var normal = Resources.Load<Texture2D>("Art/" + resourceName + "_Normal");
                if (normal != null)
                {
                    SetTexture(material, "_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }

                var mask = Resources.Load<Texture2D>("Art/" + resourceName + "_MaskMap");
                if (mask != null)
                {
                    SetTexture(material, "_MetallicGlossMap", mask);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }

                GLog.Info(LogChannel.Procedural, $"Surface '{resourceName}' is using downloaded textures.");
            }

            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Smoothness", smoothness);
            SetFloat(material, "_Glossiness", smoothness);
        }

        private static void ApplyWater(Material material)
        {
            var waterShader = Shader.Find("Grotto/WaterSurface");
            if (waterShader != null)
            {
                material.shader = waterShader;
                return;
            }

            // No custom shader compiled yet — a plausible transparent stand-in.
            MakeTransparent(material, new Color(0.06f, 0.16f, 0.18f, 0.82f));
            SetFloat(material, "_Metallic", 0.05f);
            SetFloat(material, "_Smoothness", 0.95f);
        }

        private static void MakeTransparent(Material material, Color color)
        {
            SetColor(material, color);

            // URP Lit surface-type switch. Guarded, because the Standard fallback
            // has none of these and would just log missing-property warnings.
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);                  // Transparent
                material.SetFloat("_Blend", 0f);                    // Alpha
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
        }

        private static void MakeEmissive(Material material, Color color, float intensity)
        {
            SetColor(material, color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", color * intensity);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            SetFloat(material, "_Smoothness", 0.3f);
        }

        private static void SetTiling(Material material, float unitsPerTile)
        {
            float scale = unitsPerTile <= 0f ? 1f : 1f / unitsPerTile;
            var tiling = new Vector2(scale, scale);

            if (material.HasProperty("_BaseMap")) material.SetTextureScale("_BaseMap", tiling);
            if (material.HasProperty("_MainTex")) material.SetTextureScale("_MainTex", tiling);
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (texture != null && material.HasProperty(property)) material.SetTexture(property, texture);
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode() => ClearCache();
#endif
    }
}
