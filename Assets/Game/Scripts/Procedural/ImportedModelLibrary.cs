using UnityEngine;
using Grotto.Core;

namespace Grotto.Procedural
{
    /// <summary>
    /// Loads a downloaded model in place of a generated one.
    ///
    /// Drop a model at <c>Assets/Game/Resources/Cast/Models/&lt;animatronic id&gt;</c>
    /// and the game uses it. Nothing else is required: no prefab to wire, no component
    /// to add, no code to change. If the file is absent the character is generated as
    /// before, so a project with three imported characters and two generated ones is a
    /// perfectly ordinary state to be in.
    ///
    /// Four things happen on load, and each one is a problem that would otherwise cost
    /// you twenty minutes per character:
    ///
    ///  1. <b>Scale.</b> Downloaded models arrive at every scale imaginable — Blender
    ///     metres, Source units at 1:52, centimetre exports at 100:1. The model is
    ///     measured and scaled so its height matches the character's authored height,
    ///     so a two-metre bear is two metres whatever the exporter thought.
    ///  2. <b>Grounding.</b> The lowest point is put on the floor. A model authored
    ///     around its hips sinks to the waist otherwise.
    ///  3. <b>Rigging.</b> <see cref="RigBinder"/> matches whatever the bones are
    ///     called to the ones the game drives.
    ///  4. <b>Eyes.</b> If the model has no identifiable eye renderer, generated lamps
    ///     are attached to the head — because the glow is how the player identifies a
    ///     character in the dark, and it is what the jumpscare is framed around.
    ///
    /// Rotation is the one thing not guessed. Models face -Z about as often as +Z and
    /// there is no reliable way to tell which from geometry, so it is a number on the
    /// import settings that you set once by looking at it.
    /// </summary>
    public static class ImportedModelLibrary
    {
        /// <summary>Resources sub-folder holding downloaded character models.</summary>
        public const string ResourceFolder = "Cast/Models";

        /// <summary>Per-character import settings, stored beside the model.</summary>
        public const string SettingsSuffix = "_import";

        /// <summary>True when a model has been dropped in for this character.</summary>
        public static bool Has(string animatronicId) => Load(animatronicId) != null;

        public static GameObject Load(string animatronicId)
        {
            if (string.IsNullOrWhiteSpace(animatronicId)) return null;
            return Resources.Load<GameObject>($"{ResourceFolder}/{animatronicId}");
        }

        public static ModelImportSettings LoadSettings(string animatronicId)
        {
            if (string.IsNullOrWhiteSpace(animatronicId)) return null;
            return Resources.Load<ModelImportSettings>(
                $"{ResourceFolder}/{animatronicId}{SettingsSuffix}");
        }

        /// <summary>
        /// Instantiates the imported model under <paramref name="parent"/>, fits it,
        /// binds its rig and returns the result — or null when there is nothing to
        /// import, which is the caller's signal to generate instead.
        /// </summary>
        public static GameObject Build(string animatronicId, AnimatronicModelSpec spec,
            string objectName, Transform parent)
        {
            var prefab = Load(animatronicId);
            if (prefab == null) return null;

            var settings = LoadSettings(animatronicId);

            var instance = Object.Instantiate(prefab, parent);
            instance.name = objectName;

            // Rotation first: fitting measures world bounds, and a model lying on its
            // side measures as wide rather than tall.
            instance.transform.localRotation = settings != null
                ? Quaternion.Euler(settings.rotationEuler)
                : Quaternion.identity;

            var rig = instance.GetComponent<AnimatronicRig>();
            if (rig == null) rig = instance.AddComponent<AnimatronicRig>();

            // A prefab saved by the import window already carries a bound rig, so this
            // only fills in what is still empty — a hand-corrected binding survives.
            RigBinder.Bind(rig, instance.transform, overwriteExisting: false);

            Fit(instance.transform, spec, settings);
            EnsureEyes(rig, spec, settings);

            int layer = SafeLayer("Animatronic");
            foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            GLog.Info(LogChannel.Procedural,
                $"Imported model for '{animatronicId}': {instance.name}, " +
                $"fitted to {spec.height:0.00}m.");

            return instance;
        }

        // ---------------------------------------------------------------------
        // Fitting
        // ---------------------------------------------------------------------

        /// <summary>
        /// Scales the model to the character's authored height and stands it on the
        /// floor.
        ///
        /// Height rather than a scale factor, because height is the thing the game
        /// actually cares about: the jumpscare frames on the head, the collider is
        /// sized from it, and a character that is nominally 2.05m has to be 2.05m for
        /// the camera move to land. What the exporter's units were is not interesting.
        /// </summary>
        private static void Fit(Transform root, AnimatronicModelSpec spec, ModelImportSettings settings)
        {
            if (settings != null && !settings.autoFit)
            {
                root.localScale = Vector3.one * Mathf.Max(0.0001f, settings.manualScale);
                root.localPosition = settings.positionOffset;
                return;
            }

            var bounds = RigBinder.MeasureBounds(root);
            if (bounds.size.y < 1e-4f)
            {
                GLog.Warn(LogChannel.Procedural,
                    $"'{root.name}' has no measurable height; leaving it at scale 1.");
                return;
            }

            float wanted = Mathf.Max(0.2f, spec.height);
            float scale = wanted / bounds.size.y;

            root.localScale = Vector3.one * scale;

            // Re-measure after scaling: the offset has to be in the new units.
            bounds = RigBinder.MeasureBounds(root);

            // Stand it on the parent's origin, which is the node position the
            // controller places the character at.
            float lift = root.position.y - bounds.min.y;
            root.localPosition = new Vector3(0f, lift, 0f) +
                                 (settings != null ? settings.positionOffset : Vector3.zero);
        }

        // ---------------------------------------------------------------------
        // Eyes
        // ---------------------------------------------------------------------

        /// <summary>
        /// Gives an imported model the game's glowing eyes when it has none of its own.
        ///
        /// Two lamps at the front of the head, sized from the head's own bounds so they
        /// suit the model rather than a number picked for the generated characters.
        /// A model that *does* have eye geometry keeps it, and the emissive material is
        /// applied to that instead.
        /// </summary>
        private static void EnsureEyes(AnimatronicRig rig, AnimatronicModelSpec spec,
            ModelImportSettings settings)
        {
            if (settings != null && !settings.addEyeLamps) return;

            var material = MaterialLibrary.Instance(SurfaceKind.EmissiveWarm, spec.eyeGlow);
            material.SetColor("_EmissionColor", spec.eyeGlow * 3.2f);
            rig.EyeMaterial = material;

            // The model brought its own eyes: light those and stop.
            if (rig.EyeRenderers != null && rig.EyeRenderers.Length > 0)
            {
                foreach (var renderer in rig.EyeRenderers)
                    if (renderer != null) renderer.sharedMaterial = material;
                return;
            }

            if (rig.Head == null) return;

            // Size the lamps from the head, not from a constant: a model with a huge
            // mascot head needs bigger eyes than one with a human skull.
            var headBounds = RigBinder.MeasureBounds(rig.Head);
            float headSize = headBounds.size.magnitude;

            // Fall back to a share of the character's height when the head bone has no
            // geometry of its own — common on models where everything is one mesh.
            if (headSize < 1e-3f) headSize = spec.height * 0.25f;

            float radius = headSize * 0.055f;
            float spread = headSize * 0.11f;
            float forward = headSize * 0.26f;

            var builder = new MeshBuilder(128);
            builder.CurrentColor = Color.white;
            builder.AddSphere(Vector3.zero, radius, 12, 10);
            var mesh = builder.ToMesh("EyeLamp");

            var renderers = new Renderer[2];

            for (int side = 0; side < 2; side++)
            {
                var go = new GameObject(side == 0 ? "Eye.L (added)" : "Eye.R (added)");
                go.transform.SetParent(rig.Head, worldPositionStays: false);
                go.transform.localPosition = new Vector3(
                    (side == 0 ? -1f : 1f) * spread, headSize * 0.04f, forward);

                go.AddComponent<MeshFilter>().sharedMesh = mesh;

                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                renderers[side] = renderer;
            }

            rig.EyeRenderers = renderers;

            GLog.Info(LogChannel.Procedural,
                "Imported model had no eye geometry; two lamps were added to the head. " +
                "Nudge them with the import settings if they are in the wrong place.");
        }

        private static int SafeLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? layer : 0;
        }
    }
}

