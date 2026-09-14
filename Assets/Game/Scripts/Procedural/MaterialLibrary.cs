using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

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
        private static Shader _triplanarShader;
        private static Shader _surfaceShader;
        private static SitePalette _palette = SitePalette.Limestone;

        /// <summary>
        /// Which family of surfaces the current site is built from.
        ///
        /// Set before any geometry is generated. Changing it clears the cache, because
        /// the palette decides the base colour, the damp tint and the tiling of every
        /// bulk surface — a hydro station wants board-marked concrete at half a metre
        /// per tile, not warm limestone at three.
        /// </summary>
        public static SitePalette Palette
        {
            get => _palette;
            set
            {
                if (_palette == value) return;
                _palette = value;
                ClearCache();
            }
        }

        /// <summary>
        /// The triplanar rock shader, or null when it did not compile.
        ///
        /// Cave shells are noise-displaced rings whose UVs stretch wherever the surface
        /// turns steeply, which in a cave is everywhere. This is the shader that fixes
        /// that; URP Lit is the fallback, and it looks visibly worse on an overhang.
        /// </summary>
        private static Shader TriplanarShader
        {
            get
            {
                if (_triplanarShader != null) return _triplanarShader;
                _triplanarShader = Shader.Find("Grotto/CaveTriplanar");

                if (_triplanarShader == null)
                    GLog.Warn(LogChannel.Procedural,
                        "Grotto/CaveTriplanar did not compile; bulk surfaces fall back to URP Lit " +
                        "and will show UV stretching on steep faces.");

                return _triplanarShader;
            }
        }

        /// <summary>
        /// The UV-mapped surface shader, or null when it did not compile.
        ///
        /// Every generated mesh carries a per-vertex tint and a baked occlusion term
        /// that URP's own Lit shader has nowhere to read. This is the shader that uses
        /// them; falling back to URP Lit loses that variation but renders correctly.
        /// </summary>
        private static Shader SurfaceShader
        {
            get
            {
                if (_surfaceShader != null) return _surfaceShader;
                _surfaceShader = Shader.Find("Grotto/SurfaceLit");

                if (_surfaceShader == null)
                    GLog.Warn(LogChannel.Procedural,
                        "Grotto/SurfaceLit did not compile; per-vertex tinting and baked " +
                        "occlusion are lost and props will render flat.");

                return _surfaceShader;
            }
        }

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
            _triplanarShader = null;
            _surfaceShader = null;
        }

        private static Material Build(SurfaceKind kind)
        {
            var material = new Material(LitShader) { name = "M_" + kind };

            switch (kind)
            {
                case SurfaceKind.CaveRock:
                    Apply(material, "CaveRock", () => TextureFactory.Limestone(), metallic: 0f, smoothness: 0.12f);
                    MakeTriplanar(material, BulkTint(), BulkTiling(), smoothness: 0.12f);
                    break;

                case SurfaceKind.CaveRockDamp:
                    Apply(material, "CaveRockDamp", () => TextureFactory.DampLimestone(), metallic: 0f, smoothness: 0.55f);
                    MakeTriplanar(material, BulkTint() * 0.86f, BulkTiling(), smoothness: 0.55f);
                    break;

                case SurfaceKind.Shotcrete:
                    Apply(material, "Shotcrete", () => TextureFactory.Shotcrete(), metallic: 0f, smoothness: 0.18f);
                    MakeTriplanar(material, LiningTint(), 0.5f, smoothness: 0.18f);
                    break;

                case SurfaceKind.SteelPainted:
                    Apply(material, "SteelPainted", () => TextureFactory.RustedSteel(), metallic: 0.75f, smoothness: 0.42f);
                    MakeSurfaceLit(material, new Color(0.62f, 0.66f, 0.64f), 1f,
                        metallic: 0.75f, smoothness: 0.42f, relief: 0.7f);
                    break;

                case SurfaceKind.SteelRusted:
                    Apply(material, "SteelRusted", () => TextureFactory.RustedSteel(), metallic: 0.55f, smoothness: 0.22f);
                    MakeSurfaceLit(material, Color.white, 1f,
                        metallic: 0.55f, smoothness: 0.22f, relief: 1.2f);
                    break;

                case SurfaceKind.ArcadeCarpet:
                    Apply(material, "ArcadeCarpet", () => TextureFactory.ArcadeCarpet(), metallic: 0f, smoothness: 0.06f);
                    MakeSurfaceLit(material, Color.white, 0.6f,
                        metallic: 0f, smoothness: 0.06f, relief: 0.5f);
                    break;

                case SurfaceKind.Decking:
                    Apply(material, "Decking", () => TextureFactory.Decking(), metallic: 0f, smoothness: 0.2f);
                    MakeSurfaceLit(material, Color.white, 0.8f,
                        metallic: 0f, smoothness: 0.2f, relief: 0.9f);
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
                    // Moulded plastic. No texture — the colour is entirely per-vertex,
                    // baked by AnimatronicFactory from the character's spec and its
                    // weathering, so this shader has to be one that reads vertex colour.
                    MakeSurfaceLit(material, new Color(0.55f, 0.34f, 0.20f), 1f,
                        metallic: 0.05f, smoothness: 0.48f, relief: 0.35f);
                    break;

                case SurfaceKind.AnimatronicMetal:
                    Apply(material, "Endoskeleton", () => TextureFactory.RustedSteel(), metallic: 0.9f, smoothness: 0.55f);
                    MakeSurfaceLit(material, new Color(0.52f, 0.54f, 0.57f), 2f,
                        metallic: 0.9f, smoothness: 0.55f, relief: 1.1f);
                    break;

                case SurfaceKind.AnimatronicFabric:
                    MakeSurfaceLit(material, new Color(0.36f, 0.28f, 0.22f), 1f,
                        metallic: 0f, smoothness: 0.08f, relief: 0.6f);
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
        // Palette
        // ---------------------------------------------------------------------

        /// <summary>Base colour of the site's bulk surface — rock, or poured concrete.</summary>
        private static Color BulkTint() => _palette switch
        {
            // Warm, slightly yellow: limestone with iron in it, lit by tungsten.
            SitePalette.Limestone => new Color(1f, 0.96f, 0.90f),
            // Board-marked concrete reads grey-green under the same lamps.
            SitePalette.Concrete => new Color(0.82f, 0.86f, 0.84f),
            // A grain terminal's bulk surface is slipformed concrete gone dusty.
            SitePalette.Steel => new Color(0.88f, 0.85f, 0.78f),
            _ => Color.white
        };

        /// <summary>Base colour of the lining — shotcrete, tile, galvanised sheet.</summary>
        private static Color LiningTint() => _palette switch
        {
            SitePalette.Limestone => new Color(0.90f, 0.89f, 0.86f),
            SitePalette.Concrete => new Color(0.76f, 0.80f, 0.79f),
            SitePalette.Steel => new Color(0.80f, 0.82f, 0.85f),
            _ => Color.white
        };

        /// <summary>
        /// Metres per texture tile for the bulk surface.
        ///
        /// Natural rock has structure at every scale, so it tiles large without the
        /// repeat becoming obvious. Poured concrete has a shutter pattern with a real
        /// size — about half a metre — and stretching that to three reads as fog.
        /// </summary>
        private static float BulkTiling() => _palette switch
        {
            SitePalette.Limestone => 0.35f,
            SitePalette.Concrete => 0.55f,
            SitePalette.Steel => 0.7f,
            _ => 0.4f
        };

        /// <summary>The colour everything below the historic waterline is stained.</summary>
        private static Color DampTint() => _palette switch
        {
            SitePalette.Limestone => new Color(0.42f, 0.48f, 0.46f),
            SitePalette.Concrete => new Color(0.34f, 0.40f, 0.44f),
            SitePalette.Steel => new Color(0.46f, 0.40f, 0.32f),   // rust, not algae
            _ => new Color(0.42f, 0.48f, 0.46f)
        };

        /// <summary>
        /// Switches a bulk surface onto the triplanar shader, keeping whatever texture
        /// <see cref="Apply"/> already resolved.
        ///
        /// Called after Apply rather than instead of it, so the downloaded-texture path
        /// still works: the fetcher's albedo is projected triplanar exactly like the
        /// generated one.
        /// </summary>
        private static void MakeTriplanar(Material material, Color tint, float unitsPerTile,
            float smoothness)
        {
            var shader = TriplanarShader;
            if (shader == null)
            {
                SetTiling(material, unitsPerTile);
                SetColor(material, tint);
                return;
            }

            var baseMap = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;

            material.shader = shader;
            SetTexture(material, "_BaseMap", baseMap);
            SetColor(material, tint);

            SetFloat(material, "_Tiling", unitsPerTile <= 0f ? 1f : 1f / unitsPerTile);
            SetFloat(material, "_BlendSharpness", 5f);
            SetFloat(material, "_Smoothness", smoothness);
            SetFloat(material, "_Metallic", 0f);

            // Detail relief runs about seven times finer than the albedo grain, which
            // is roughly the ratio between the pitting you see at arm's length and the
            // bedding you see across a room.
            SetFloat(material, "_DetailTiling", (unitsPerTile <= 0f ? 1f : 1f / unitsPerTile) * 7f);
            SetFloat(material, "_DetailStrength", _palette == SitePalette.Limestone ? 1.25f : 0.8f);

            // And a very low frequency pass for regional tone: one tile per twenty-odd
            // metres, so a big cavern is not one flat colour.
            SetFloat(material, "_MacroTiling", 0.045f);
            SetFloat(material, "_MacroContrast", _palette == SitePalette.Limestone ? 0.4f : 0.22f);

            SetFloat(material, "_OcclusionStrength", 1f);
            SetFloat(material, "_VertexColorStrength", 1f);

            if (material.HasProperty("_DampColor")) material.SetColor("_DampColor", DampTint());
            SetFloat(material, "_DampHeight", -4f);
            SetFloat(material, "_DampFalloff", 2.5f);
        }

        /// <summary>
        /// Switches a UV-mapped surface onto the vertex-aware lit shader, keeping
        /// whatever texture <see cref="Apply"/> already resolved.
        /// </summary>
        private static void MakeSurfaceLit(Material material, Color tint, float unitsPerTile,
            float metallic, float smoothness, float relief)
        {
            var shader = SurfaceShader;
            if (shader == null)
            {
                SetColor(material, tint);
                SetTiling(material, unitsPerTile);
                return;
            }

            var baseMap = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;

            material.shader = shader;
            SetTexture(material, "_BaseMap", baseMap);
            SetColor(material, tint);

            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Smoothness", smoothness);
            SetFloat(material, "_DetailStrength", relief);
            SetFloat(material, "_VertexColorStrength", 1f);
            SetFloat(material, "_OcclusionStrength", 1f);

            SetTiling(material, unitsPerTile);
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
