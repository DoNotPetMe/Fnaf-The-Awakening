using UnityEngine;
using Grotto.Core;

namespace Grotto.Procedural
{
    /// <summary>
    /// Generates the project's surface textures at load time.
    ///
    /// This is what lets the repository ship with no binary art and still not look
    /// like a grey-box: limestone gets mineral banding and damp staining, shotcrete
    /// gets its pitting, the arcade carpet gets a genuinely offensive 1979 pattern.
    /// Everything is seeded, so the same texture comes out on every machine.
    ///
    /// Albedo and a packed mask only — no normal maps. A runtime-created
    /// <see cref="Texture2D"/> cannot be marked as a normal map, and the packing
    /// Unity's shaders expect differs by platform, so a generated "normal map" would
    /// be wrong somewhere. Surface relief here comes from geometry and vertex colour
    /// instead. Real tangent-space maps arrive with the CC0 packs the Asset Fetcher
    /// pulls, which import through the normal pipeline and are correct.
    /// </summary>
    public static class TextureFactory
    {
        public const int DefaultSize = 256;

        /// <summary>Pale, mineral-banded limestone. The default cave surface.</summary>
        public static Texture2D Limestone(int size = DefaultSize, int seed = 11)
        {
            return Generate("Tex_Limestone", size, (u, v) =>
            {
                var p = new Vector3(u * 7f, v * 7f, 0f);

                // Bedding planes: a strong horizontal band, broken up so it is not stripy.
                float bands = Mathf.Sin((v * 9f + ProcNoise.Fbm(p * 0.7f, 3, seed: seed) * 2.2f) * Mathf.PI);
                bands = bands * 0.5f + 0.5f;

                float grain = ProcNoise.Fbm(p * 3.5f, 5, seed: seed + 3);
                float solution = ProcNoise.Ridged(p * 1.6f, 4, seed: seed + 9);

                float value = Mathf.Lerp(0.62f, 0.86f, grain);
                value -= bands * 0.09f;
                value -= solution * 0.13f;

                // Limestone is warm, not neutral grey.
                return new Color(value * 1.03f, value * 0.99f, value * 0.91f, 1f);
            });
        }

        /// <summary>Wet, algae-stained rock for the flooded gallery and the channel.</summary>
        public static Texture2D DampLimestone(int size = DefaultSize, int seed = 23)
        {
            return Generate("Tex_LimestoneDamp", size, (u, v) =>
            {
                var p = new Vector3(u * 6f, v * 6f, 2f);

                float grain = ProcNoise.Fbm(p * 3.2f, 5, seed: seed);
                float wet = ProcNoise.Fbm(p * 1.1f, 3, seed: seed + 17);

                // Staining runs downward, so bias it by v.
                float run = Mathf.Clamp01(wet - v * 0.35f);

                float value = Mathf.Lerp(0.30f, 0.55f, grain);
                return new Color(
                    value * (1f - run * 0.35f),
                    value * (1f - run * 0.05f),
                    value * (1f - run * 0.22f),
                    1f);
            });
        }

        /// <summary>1981 shotcrete lining: pitted, patched, faintly yellow.</summary>
        public static Texture2D Shotcrete(int size = DefaultSize, int seed = 41)
        {
            return Generate("Tex_Shotcrete", size, (u, v) =>
            {
                var p = new Vector3(u * 9f, v * 9f, 5f);

                float pitting = 1f - ProcNoise.Cellular(p * 5f, seed) * 0.55f;
                float mottle = ProcNoise.Fbm(p * 2.4f, 4, seed: seed + 5);

                float value = Mathf.Lerp(0.42f, 0.58f, mottle) * pitting;
                return new Color(value * 1.02f, value, value * 0.94f, 1f);
            });
        }

        /// <summary>Painted steel gone to rust at the seams.</summary>
        public static Texture2D RustedSteel(int size = DefaultSize, int seed = 67)
        {
            var paint = new Color(0.29f, 0.33f, 0.30f);
            var rust = new Color(0.42f, 0.20f, 0.09f);

            return Generate("Tex_SteelRusted", size, (u, v) =>
            {
                var p = new Vector3(u * 8f, v * 8f, 9f);

                float corrosion = ProcNoise.Fbm(p * 2.2f, 5, seed: seed);
                float edge = Mathf.Clamp01((corrosion - 0.52f) * 4f);
                float speckle = ProcNoise.Fbm(p * 14f, 2, seed: seed + 2) * 0.12f;

                var c = Color.Lerp(paint, rust, edge);
                return new Color(c.r + speckle, c.g + speckle * 0.7f, c.b + speckle * 0.5f, 1f);
            });
        }

        /// <summary>
        /// The Midway carpet. Teal, orange and magenta confetti on near-black — the
        /// pattern every arcade in North America had, and the single fastest way to
        /// place a room in 1979.
        /// </summary>
        public static Texture2D ArcadeCarpet(int size = DefaultSize, int seed = 83)
        {
            var ground = new Color(0.055f, 0.06f, 0.10f);
            var teal = new Color(0.09f, 0.55f, 0.52f);
            var orange = new Color(0.80f, 0.35f, 0.10f);
            var magenta = new Color(0.60f, 0.12f, 0.42f);

            return Generate("Tex_ArcadeCarpet", size, (u, v) =>
            {
                var p = new Vector3(u * 26f, v * 26f, 1f);

                float fibre = ProcNoise.Fbm(p * 18f, 2, seed: seed) * 0.10f;
                var colour = ground;

                // Three independent confetti layers, each a thresholded cellular field.
                float a = ProcNoise.Cellular(p * 1.9f, seed + 1);
                float b = ProcNoise.Cellular(p * 2.3f, seed + 2);
                float c = ProcNoise.Cellular(p * 2.7f, seed + 3);

                if (a < 0.20f) colour = teal;
                else if (b < 0.16f) colour = orange;
                else if (c < 0.13f) colour = magenta;

                // Twenty years of foot traffic.
                float wear = ProcNoise.Fbm(p * 0.35f, 3, seed: seed + 40);
                colour = Color.Lerp(colour, ground * 1.4f, wear * 0.3f);

                return new Color(colour.r + fibre, colour.g + fibre, colour.b + fibre, 1f);
            });
        }

        /// <summary>Timber decking for the crossing bridge.</summary>
        public static Texture2D Decking(int size = DefaultSize, int seed = 97)
        {
            return Generate("Tex_Decking", size, (u, v) =>
            {
                var p = new Vector3(u * 10f, v * 10f, 3f);

                // Plank seams every eighth of the tile.
                float plank = Mathf.Abs(Mathf.Repeat(v * 8f, 1f) - 0.5f) * 2f;
                float seam = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((plank - 0.86f) * 7f));

                float grainNoise = ProcNoise.Fbm(new Vector3(p.x * 0.6f, p.y * 11f, p.z), 3, seed: seed);
                float value = Mathf.Lerp(0.20f, 0.34f, grainNoise) * (1f - seam * 0.7f);

                return new Color(value * 1.25f, value * 1.02f, value * 0.74f, 1f);
            });
        }

        /// <summary>
        /// Packs a URP metallic/smoothness map. R = metallic, A = smoothness; G and B
        /// are left at the values URP's Lit shader ignores for this slot.
        /// </summary>
        public static Texture2D MetallicSmoothness(float metallic, float smoothness, float variation,
            int size = 64, int seed = 5)
        {
            return Generate($"Tex_MS_{metallic:0.00}_{smoothness:0.00}", size, (u, v) =>
            {
                float n = (ProcNoise.Fbm(new Vector3(u * 5f, v * 5f, 7f), 3, seed: seed) - 0.5f) * variation;
                return new Color(Mathf.Clamp01(metallic + n), 0f, 0f, Mathf.Clamp01(smoothness + n));
            }, linear: true);
        }

        // ---------------------------------------------------------------------

        private static Texture2D Generate(string textureName, int size,
            System.Func<float, float, Color> sample, bool linear = false)
        {
            size = Mathf.Clamp(Mathf.ClosestPowerOfTwo(size), 16, 1024);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true, linear: linear)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };

            var pixels = new Color[size * size];
            float inverse = 1f / size;

            for (int y = 0; y < size; y++)
            {
                float v = y * inverse;
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = sample(x * inverse, v);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);

            GLog.Verbose(LogChannel.Procedural, $"Generated {textureName} at {size}x{size}.");
            return texture;
        }
    }
}
