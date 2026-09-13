using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Procedural
{
    /// <summary>
    /// Builds an animatronic from an <see cref="AnimatronicModelSpec"/>.
    ///
    /// Each character is a rest-pose bone hierarchy with rigid shell parts attached to
    /// the bones. Per-species passes change the silhouette rather than recolouring one
    /// template: the bat is hunched with folded wing membranes, the mole is squat with
    /// digging claws, the salamander is low and tailed, and the Chorus is deliberately
    /// assembled out of the wrong parts.
    ///
    /// Wear is applied through vertex colour and by omitting shell panels, so a worn
    /// character is genuinely missing pieces with frame showing through, rather than
    /// just being darker.
    /// </summary>
    public static class AnimatronicFactory
    {
        private const float ReferenceHeight = 1.95f;

        public static GameObject Build(AnimatronicModelSpec spec, string objectName, Transform parent = null)
        {
            if (spec == null) spec = new AnimatronicModelSpec();

            var root = new GameObject(objectName);
            if (parent != null) root.transform.SetParent(parent, worldPositionStays: false);

            var rig = root.AddComponent<AnimatronicRig>();
            float scale = spec.height / ReferenceHeight;

            BuildSkeleton(rig, spec, scale);

            var batches = new Dictionary<Transform, SurfaceBatch>();
            SurfaceBatch Batch(Transform bone)
            {
                if (batches.TryGetValue(bone, out var existing)) return existing;
                var created = new SurfaceBatch();
                batches[bone] = created;
                return created;
            }

            BuildTorso(rig, spec, scale, Batch);
            BuildArms(rig, spec, scale, Batch);
            BuildLegs(rig, spec, scale, Batch);
            BuildHead(rig, spec, scale, Batch);

            switch (spec.species)
            {
                case Species.Bear: DressBear(rig, spec, scale, Batch); break;
                case Species.Bat: DressBat(rig, spec, scale, Batch); break;
                case Species.Mole: DressMole(rig, spec, scale, Batch); break;
                case Species.Salamander: DressSalamander(rig, spec, scale, Batch); break;
                case Species.Composite: DressComposite(rig, spec, scale, Batch); break;
            }

            int triangles = 0;
            foreach (var pair in batches)
                // Recalculated, because AddRing gives a sphere's rings cylindrical
                // normals — fine for a pillar, wrong for a skull.
                triangles += pair.Value.Flush(pair.Key, pair.Key.name, addColliders: false,
                    layer: SafeLayer("Animatronic"), recalculateNormals: true,
                    staticGeometry: false);

            AttachEyes(rig, spec, scale);
            AttachCollider(root, spec, scale);

            GLog.Info(LogChannel.Procedural,
                $"Built '{objectName}' ({spec.species}, {spec.height:0.00}m, {triangles} triangles).");

            return root;
        }

        // =====================================================================
        // Skeleton
        // =====================================================================

        private static void BuildSkeleton(AnimatronicRig rig, AnimatronicModelSpec spec, float scale)
        {
            var root = rig.transform;

            // Species change the stance before anything is attached to it.
            float hipHeight = spec.species switch
            {
                Species.Salamander => 0.58f,
                Species.Mole => 0.72f,
                Species.Bat => 0.88f,
                _ => 0.95f
            };

            float spineLean = spec.species switch
            {
                Species.Salamander => 62f,   // near-horizontal
                Species.Bat => 26f,
                Species.Mole => 16f,
                Species.Composite => 20f,
                _ => 4f
            };

            rig.Hips = Bone("Hips", root, new Vector3(0f, hipHeight * scale, 0f), Quaternion.identity);
            rig.Spine = Bone("Spine", rig.Hips, new Vector3(0f, 0.18f * scale, 0f), Quaternion.Euler(spineLean * 0.4f, 0f, 0f));
            rig.Chest = Bone("Chest", rig.Spine, new Vector3(0f, 0.22f * scale, 0f), Quaternion.Euler(spineLean * 0.6f, 0f, 0f));
            rig.Neck = Bone("Neck", rig.Chest, new Vector3(0f, 0.24f * scale, 0f), Quaternion.Euler(-spineLean * 0.8f, 0f, 0f));
            rig.Head = Bone("Head", rig.Neck, new Vector3(0f, 0.12f * scale, 0f), Quaternion.identity);
            rig.Jaw = Bone("Jaw", rig.Head, new Vector3(0f, -0.04f * scale, 0.06f * scale), Quaternion.identity);

            float shoulderWidth = 0.22f * spec.bulk;

            rig.ShoulderLeft = Bone("Shoulder.L", rig.Chest, new Vector3(-shoulderWidth * scale, 0.12f * scale, 0f), Quaternion.identity);
            rig.ShoulderRight = Bone("Shoulder.R", rig.Chest, new Vector3(shoulderWidth * scale, 0.12f * scale, 0f), Quaternion.identity);

            rig.UpperArmLeft = Bone("UpperArm.L", rig.ShoulderLeft, new Vector3(-0.06f * scale, -0.04f * scale, 0f), Quaternion.Euler(6f, 0f, 8f));
            rig.UpperArmRight = Bone("UpperArm.R", rig.ShoulderRight, new Vector3(0.06f * scale, -0.04f * scale, 0f), Quaternion.Euler(6f, 0f, -8f));

            rig.ForearmLeft = Bone("Forearm.L", rig.UpperArmLeft, new Vector3(0f, -0.32f * scale, 0f), Quaternion.Euler(8f, 0f, 0f));
            rig.ForearmRight = Bone("Forearm.R", rig.UpperArmRight, new Vector3(0f, -0.32f * scale, 0f), Quaternion.Euler(8f, 0f, 0f));

            rig.HandLeft = Bone("Hand.L", rig.ForearmLeft, new Vector3(0f, -0.30f * scale, 0f), Quaternion.identity);
            rig.HandRight = Bone("Hand.R", rig.ForearmRight, new Vector3(0f, -0.30f * scale, 0f), Quaternion.identity);

            float hipWidth = 0.13f * spec.bulk;
            rig.ThighLeft = Bone("Thigh.L", rig.Hips, new Vector3(-hipWidth * scale, -0.04f * scale, 0f), Quaternion.identity);
            rig.ThighRight = Bone("Thigh.R", rig.Hips, new Vector3(hipWidth * scale, -0.04f * scale, 0f), Quaternion.identity);

            float thighLength = hipHeight * 0.48f;
            rig.ShinLeft = Bone("Shin.L", rig.ThighLeft, new Vector3(0f, -thighLength * scale, 0f), Quaternion.identity);
            rig.ShinRight = Bone("Shin.R", rig.ThighRight, new Vector3(0f, -thighLength * scale, 0f), Quaternion.identity);

            float shinLength = hipHeight * 0.46f;
            rig.FootLeft = Bone("Foot.L", rig.ShinLeft, new Vector3(0f, -shinLength * scale, 0f), Quaternion.identity);
            rig.FootRight = Bone("Foot.R", rig.ShinRight, new Vector3(0f, -shinLength * scale, 0f), Quaternion.identity);

            if (spec.species == Species.Salamander || spec.species == Species.Composite)
                rig.Tail = Bone("Tail", rig.Hips, new Vector3(0f, 0.02f * scale, -0.16f * scale), Quaternion.Euler(-14f, 0f, 0f));
        }

        private static Transform Bone(string boneName, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var go = new GameObject(boneName);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            return go.transform;
        }

        // =====================================================================
        // Shared body
        // =====================================================================

        private static void BuildTorso(AnimatronicRig rig, AnimatronicModelSpec spec, float scale,
            System.Func<Transform, SurfaceBatch> batch)
        {
            float bulk = spec.bulk;

            // Pelvis.
            var hips = batch(rig.Hips).For(SurfaceKind.AnimatronicShell);
            hips.CurrentColor = Weathered(spec.shellPrimary, spec, 1);
            hips.AddRoundedBox(Vector3.zero, new Vector3(0.30f * bulk, 0.22f, 0.22f * bulk) * scale,
                Quaternion.identity);

            // Belly.
            var spine = batch(rig.Spine).For(SurfaceKind.AnimatronicShell);
            spine.CurrentColor = Weathered(spec.shellSecondary, spec, 2);
            spine.AddSphere(new Vector3(0f, 0.09f, 0.02f) * scale, 0.20f * scale, 12, 8,
                new Vector3(bulk * 1.05f, 0.95f, bulk * 0.92f));

            // Chest.
            var chest = batch(rig.Chest).For(SurfaceKind.AnimatronicShell);
            chest.CurrentColor = Weathered(spec.shellPrimary, spec, 3);
            chest.AddRoundedBox(new Vector3(0f, 0.10f, 0f) * scale,
                new Vector3(0.40f * bulk, 0.34f, 0.26f * bulk) * scale, Quaternion.identity, 0.22f);

            // A missing chest panel showing the frame — the single most effective
            // "this has been abandoned" detail there is.
            if (spec.exposedEndoskeleton && spec.wear > 0.4f)
            {
                var frame = batch(rig.Chest).For(SurfaceKind.AnimatronicMetal);
                frame.CurrentColor = new Color(0.6f, 0.6f, 0.62f);

                for (int i = 0; i < 3; i++)
                {
                    frame.AddCylinder(new Vector3(-0.09f + i * 0.09f, -0.02f, -0.11f) * scale,
                        0.018f * scale, 0.3f * scale, 6);
                }
                frame.AddCylinder(new Vector3(0f, 0.10f, -0.11f) * scale, 0.03f * scale, 0.2f * scale, 8,
                    Quaternion.Euler(0f, 0f, 90f));
            }

            // Neck servo.
            var neck = batch(rig.Neck).For(SurfaceKind.AnimatronicMetal);
            neck.CurrentColor = new Color(0.45f, 0.46f, 0.48f);
            neck.AddCylinder(new Vector3(0f, -0.02f, 0f) * scale, 0.055f * scale, 0.14f * scale, 8);
        }

        private static void BuildArms(AnimatronicRig rig, AnimatronicModelSpec spec, float scale,
            System.Func<Transform, SurfaceBatch> batch)
        {
            float bulk = spec.bulk;

            for (int side = 0; side < 2; side++)
            {
                var shoulder = side == 0 ? rig.ShoulderLeft : rig.ShoulderRight;
                var upper = side == 0 ? rig.UpperArmLeft : rig.UpperArmRight;
                var fore = side == 0 ? rig.ForearmLeft : rig.ForearmRight;
                var hand = side == 0 ? rig.HandLeft : rig.HandRight;

                var shoulderMesh = batch(shoulder).For(SurfaceKind.AnimatronicShell);
                shoulderMesh.CurrentColor = Weathered(spec.shellPrimary, spec, 10 + side);
                shoulderMesh.AddSphere(Vector3.zero, 0.10f * bulk * scale, 10, 6);

                // One arm loses its shell entirely on a badly worn character.
                bool stripped = spec.exposedEndoskeleton && spec.wear > 0.7f && side == 1;

                var upperMesh = batch(upper).For(stripped ? SurfaceKind.AnimatronicMetal : SurfaceKind.AnimatronicShell);
                upperMesh.CurrentColor = stripped
                    ? new Color(0.55f, 0.56f, 0.58f)
                    : Weathered(spec.shellPrimary, spec, 12 + side);
                upperMesh.AddCylinder(new Vector3(0f, -0.30f, 0f) * scale,
                    (stripped ? 0.035f : 0.072f) * bulk * scale, 0.30f * scale, 10, topRadiusScale: 1.1f);

                var foreMesh = batch(fore).For(SurfaceKind.AnimatronicShell);
                foreMesh.CurrentColor = Weathered(spec.shellSecondary, spec, 14 + side);
                foreMesh.AddCylinder(new Vector3(0f, -0.28f, 0f) * scale, 0.062f * bulk * scale,
                    0.28f * scale, 10, topRadiusScale: 1.15f);

                BuildHand(batch(hand), spec, scale, side);
            }
        }

        private static void BuildHand(SurfaceBatch batch, AnimatronicModelSpec spec, float scale, int side)
        {
            var palm = batch.For(SurfaceKind.AnimatronicShell);
            palm.CurrentColor = Weathered(spec.shellSecondary, spec, 20 + side);

            bool digger = spec.species == Species.Mole;
            float palmWidth = digger ? 0.14f : 0.09f;

            palm.AddRoundedBox(new Vector3(0f, -0.05f, 0f) * scale,
                new Vector3(palmWidth, 0.11f, 0.05f) * scale, Quaternion.identity, 0.2f);

            // Three fingers and a thumb — mascot hands never have five.
            var claw = batch.For(digger ? SurfaceKind.AnimatronicMetal : SurfaceKind.AnimatronicShell);
            claw.CurrentColor = digger ? new Color(0.62f, 0.6f, 0.55f) : Weathered(spec.shellSecondary, spec, 24);

            float fingerLength = digger ? 0.16f : 0.09f;
            for (int f = 0; f < 3; f++)
            {
                float x = (f - 1) * 0.038f;
                claw.AddCone(new Vector3(x, -0.10f, 0f) * scale, 0.020f * scale, fingerLength * scale, 6,
                    Quaternion.Euler(180f, 0f, 0f));
            }

            claw.AddCone(new Vector3((side == 0 ? 0.06f : -0.06f), -0.07f, 0.01f) * scale,
                0.018f * scale, fingerLength * 0.7f * scale, 6,
                Quaternion.Euler(150f, 0f, side == 0 ? -30f : 30f));
        }

        private static void BuildLegs(AnimatronicRig rig, AnimatronicModelSpec spec, float scale,
            System.Func<Transform, SurfaceBatch> batch)
        {
            float bulk = spec.bulk;

            for (int side = 0; side < 2; side++)
            {
                var thigh = side == 0 ? rig.ThighLeft : rig.ThighRight;
                var shin = side == 0 ? rig.ShinLeft : rig.ShinRight;
                var foot = side == 0 ? rig.FootLeft : rig.FootRight;

                float thighLength = Mathf.Abs(shin.localPosition.y);
                float shinLength = Mathf.Abs(foot.localPosition.y);

                var thighMesh = batch(thigh).For(SurfaceKind.AnimatronicShell);
                thighMesh.CurrentColor = Weathered(spec.shellPrimary, spec, 30 + side);
                thighMesh.AddCylinder(new Vector3(0f, -thighLength, 0f), 0.085f * bulk * scale,
                    thighLength, 10, topRadiusScale: 1.25f);

                var shinMesh = batch(shin).For(SurfaceKind.AnimatronicShell);
                shinMesh.CurrentColor = Weathered(spec.shellPrimary, spec, 32 + side);
                shinMesh.AddCylinder(new Vector3(0f, -shinLength, 0f), 0.068f * bulk * scale,
                    shinLength, 10, topRadiusScale: 1.2f);

                // Exposed knee actuator.
                var knee = batch(shin).For(SurfaceKind.AnimatronicMetal);
                knee.CurrentColor = new Color(0.5f, 0.51f, 0.53f);
                knee.AddCylinder(new Vector3(-0.05f, -0.02f, 0f) * scale, 0.022f * scale, 0.1f * scale, 6,
                    Quaternion.Euler(0f, 0f, 90f));

                var footMesh = batch(foot).For(SurfaceKind.AnimatronicShell);
                footMesh.CurrentColor = Weathered(spec.shellSecondary, spec, 34 + side);
                footMesh.AddRoundedBox(new Vector3(0f, 0.04f, 0.05f) * scale,
                    new Vector3(0.14f * bulk, 0.08f, 0.26f) * scale, Quaternion.identity, 0.25f);
            }
        }

        private static void BuildHead(AnimatronicRig rig, AnimatronicModelSpec spec, float scale,
            System.Func<Transform, SurfaceBatch> batch)
        {
            float head = 0.17f * spec.headScale * scale;

            var skull = batch(rig.Head).For(SurfaceKind.AnimatronicShell);
            skull.CurrentColor = Weathered(spec.shellPrimary, spec, 40);
            skull.AddSphere(new Vector3(0f, head * 0.55f, 0f), head, 14, 10,
                new Vector3(1f, 0.95f, 1.05f));

            // Upper muzzle.
            var muzzle = batch(rig.Head).For(SurfaceKind.AnimatronicShell);
            muzzle.CurrentColor = Weathered(spec.shellSecondary, spec, 41);
            muzzle.AddSphere(new Vector3(0f, head * 0.35f, head * 0.78f), head * 0.52f, 12, 8,
                new Vector3(1.15f, 0.72f, 1.25f));

            // Jaw, hinged so the servo animator can chatter it.
            var jaw = batch(rig.Jaw).For(SurfaceKind.AnimatronicShell);
            jaw.CurrentColor = Weathered(spec.shellSecondary, spec, 42);
            jaw.AddRoundedBox(new Vector3(0f, -head * 0.16f, head * 0.62f),
                new Vector3(head * 1.02f, head * 0.34f, head * 1.15f), Quaternion.identity, 0.28f);

            // Teeth: square, evenly spaced, very slightly too many.
            var teeth = batch(rig.Jaw).For(SurfaceKind.AnimatronicMetal);
            teeth.CurrentColor = new Color(0.86f, 0.84f, 0.78f);
            for (int i = 0; i < 6; i++)
            {
                float x = (i - 2.5f) * head * 0.26f;
                teeth.AddBox(new Vector3(x, head * 0.02f, head * 1.08f),
                    new Vector3(head * 0.18f, head * 0.16f, head * 0.1f));
            }
        }

        private static void AttachEyes(AnimatronicRig rig, AnimatronicModelSpec spec, float scale)
        {
            float head = 0.17f * spec.headScale * scale;
            var eyeMaterial = MaterialLibrary.Instance(SurfaceKind.EmissiveWarm, spec.eyeGlow);
            eyeMaterial.SetColor("_EmissionColor", spec.eyeGlow * 3.2f);

            var renderers = new Renderer[2];
            int layer = SafeLayer("Animatronic");

            for (int side = 0; side < 2; side++)
            {
                var builder = new MeshBuilder(128);
                builder.CurrentColor = Color.white;

                // Socket, then the lamp inside it. The recess is what makes the glow
                // read as coming from inside the head rather than painted on.
                builder.AddSphere(Vector3.zero, head * 0.17f, 10, 8);

                var go = new GameObject(side == 0 ? "Eye.L" : "Eye.R");
                go.transform.SetParent(rig.Head, worldPositionStays: false);
                go.transform.localPosition = new Vector3(
                    (side == 0 ? -1f : 1f) * head * 0.36f, head * 0.62f, head * 0.66f);
                go.layer = layer;

                go.AddComponent<MeshFilter>().sharedMesh = builder.ToMesh("EyeLamp");
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = eyeMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                renderers[side] = renderer;
            }

            rig.EyeRenderers = renderers;
            rig.EyeMaterial = eyeMaterial;
        }

        private static void AttachCollider(GameObject root, AnimatronicModelSpec spec, float scale)
        {
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = spec.height;
            capsule.radius = 0.32f * spec.bulk * scale;
            capsule.center = new Vector3(0f, spec.height * 0.5f, 0f);
            capsule.isTrigger = true;     // the cast never uses physics to move
            root.layer = SafeLayer("Animatronic");
        }

        // =====================================================================
        // Species dressing
        // =====================================================================

        private static void DressBear(AnimatronicRig rig, AnimatronicModelSpec spec, float scale,
            System.Func<Transform, SurfaceBatch> batch)
        {
            float head = 0.17f * spec.headScale * scale;

            // Round ears.
            var ears = batch(rig.Head).For(SurfaceKind.AnimatronicShell);
            ears.CurrentColor = Weathered(spec.shellPrimary, spec, 50);
            for (int side = -1; side <= 1; side += 2)
            {
                ears.AddSphere(new Vector3(side * head * 0.72f, head * 1.25f, -head * 0.1f),
                    head * 0.34f, 10, 6, new Vector3(1f, 1f, 0.45f));
            }

            // Prospector's hat.
            var hat = batch(rig.Head).For(SurfaceKind.AnimatronicFabric);
            hat.CurrentColor = new Color(0.28f, 0.22f, 0.16f);
            hat.AddCylinder(new Vector3(0f, head * 1.32f, 0f), head * 1.35f, head * 0.08f, 14);
            hat.AddCylinder(new Vector3(0f, head * 1.38f, 0f), head * 0.78f, head * 0.62f, 14,
                topRadiusScale: 0.92f);

            // Waistcoat and bow tie — he is the host, after all.
            var vest = batch(rig.Chest).For(SurfaceKind.AnimatronicFabric);
            vest.CurrentColor = spec.fabric;
            vest.AddRoundedBox(new Vector3(0f, 0.08f, -0.12f) * scale,
                new Vector3(0.34f * spec.bulk, 0.30f, 0.06f) * scale, Quaternion.identity, 0.15f);

            var tie = batch(rig.Chest).For(SurfaceKind.AnimatronicFabric);
            tie.CurrentColor = new Color(0.55f, 0.09f, 0.12f);
            tie.AddBox(new Vector3(-0.05f, 0.24f, -0.14f) * scale, new Vector3(0.08f, 0.06f, 0.03f) * scale,
                Quaternion.Euler(0f, 0f, 18f));
            tie.AddBox(new Vector3(0.05f, 0.24f, -0.14f) * scale, new Vector3(0.08f, 0.06f, 0.03f) * scale,
                Quaternion.Euler(0f, 0f, -18f));
        }

        private static void DressBat(AnimatronicRig rig, AnimatronicModelSpec spec, float scale,
            System.Func<Transform, SurfaceBatch> batch)
        {
            float head = 0.17f * spec.headScale * scale;

            // Ears, comically oversized, which is both correct for a bat and correct
            // for a mascot.
            var ears = batch(rig.Head).For(SurfaceKind.AnimatronicShell);
            ears.CurrentColor = Weathered(spec.shellSecondary, spec, 60);
            for (int side = -1; side <= 1; side += 2)
            {
                ears.AddCone(new Vector3(side * head * 0.5f, head * 1.0f, -head * 0.05f),
                    head * 0.34f, head * 1.5f, 8,
                    Quaternion.Euler(-12f, 0f, side * 16f));
            }

            // Folded wing membranes, forearm to flank.
            for (int side = 0; side < 2; side++)
            {
                var arm = side == 0 ? rig.ForearmLeft : rig.ForearmRight;
                var membrane = batch(arm).For(SurfaceKind.AnimatronicFabric);
                membrane.CurrentColor = new Color(spec.fabric.r, spec.fabric.g, spec.fabric.b) * 0.8f;

                float dir = side == 0 ? 1f : -1f;
                membrane.AddBox(new Vector3(dir * 0.08f, -0.14f, -0.02f) * scale,
                    new Vector3(0.02f, 0.42f, 0.30f) * scale,
                    Quaternion.Euler(0f, 0f, dir * 14f));

                // Wing finger struts.
                var struts = batch(arm).For(SurfaceKind.AnimatronicMetal);
                struts.CurrentColor = new Color(0.4f, 0.4f, 0.42f);
                for (int f = 0; f < 3; f++)
                {
                    struts.AddCylinder(new Vector3(dir * 0.06f, -0.02f - f * 0.02f, -0.1f) * scale,
                        0.008f * scale, 0.34f * scale, 5,
                        Quaternion.Euler(0f, 0f, 90f + f * 11f));
                }
            }
        }

        private static void DressMole(AnimatronicRig rig, AnimatronicModelSpec spec, float scale,
            System.Func<Transform, SurfaceBatch> batch)
        {
            float head = 0.17f * spec.headScale * scale;

            // Snout, long and pink.
            var snout = batch(rig.Head).For(SurfaceKind.AnimatronicShell);
            snout.CurrentColor = new Color(0.62f, 0.4f, 0.4f);
            snout.AddCone(new Vector3(0f, head * 0.42f, head * 0.8f), head * 0.3f, head * 0.65f, 10,
                Quaternion.Euler(90f, 0f, 0f));

            // Welding goggles over the eyes: he was the one who "dug the tunnels".
            var goggles = batch(rig.Head).For(SurfaceKind.AnimatronicMetal);
            goggles.CurrentColor = new Color(0.35f, 0.33f, 0.3f);
            goggles.AddCylinder(new Vector3(-head * 0.36f, head * 0.62f, head * 0.62f),
                head * 0.26f, head * 0.16f, 10, Quaternion.Euler(90f, 0f, 0f));
            goggles.AddCylinder(new Vector3(head * 0.36f, head * 0.62f, head * 0.62f),
                head * 0.26f, head * 0.16f, 10, Quaternion.Euler(90f, 0f, 0f));
            goggles.AddBox(new Vector3(0f, head * 0.62f, head * 0.2f),
                new Vector3(head * 1.5f, head * 0.12f, head * 0.1f));

            // Hard hat with a dead lamp.
            var helmet = batch(rig.Head).For(SurfaceKind.AnimatronicShell);
            helmet.CurrentColor = new Color(0.72f, 0.52f, 0.12f);
            helmet.AddSphere(new Vector3(0f, head * 0.95f, 0f), head * 1.02f, 12, 6,
                new Vector3(1f, 0.62f, 1f));
        }

        private static void DressSalamander(AnimatronicRig rig, AnimatronicModelSpec spec, float scale,
            System.Func<Transform, SurfaceBatch> batch)
        {
            float head = 0.17f * spec.headScale * scale;

            // External gill frills.
            var frills = batch(rig.Head).For(SurfaceKind.AnimatronicFabric);
            frills.CurrentColor = new Color(0.68f, 0.24f, 0.32f);
            for (int side = -1; side <= 1; side += 2)
            for (int f = 0; f < 3; f++)
            {
                frills.AddCone(new Vector3(side * head * 0.62f, head * 0.55f + f * head * 0.16f, -head * 0.3f),
                    head * 0.11f, head * 0.5f, 6,
                    Quaternion.Euler(-30f, 0f, side * (55f + f * 12f)));
            }

            // Tail, tapering in three segments.
            if (rig.Tail != null)
            {
                var tail = batch(rig.Tail).For(SurfaceKind.AnimatronicShell);
                tail.CurrentColor = Weathered(spec.shellPrimary, spec, 70);

                float length = 0.26f * scale;
                for (int i = 0; i < 3; i++)
                {
                    float radius = Mathf.Lerp(0.09f, 0.02f, i / 2f) * scale;
                    tail.AddCylinder(new Vector3(0f, 0f, -length * i), radius, length, 8,
                        Quaternion.Euler(90f, 0f, 0f), topRadiusScale: 0.7f);
                }
            }

            // Spinal crest.
            var crest = batch(rig.Chest).For(SurfaceKind.AnimatronicFabric);
            crest.CurrentColor = new Color(0.55f, 0.2f, 0.26f);
            for (int i = 0; i < 4; i++)
            {
                crest.AddBox(new Vector3(0f, 0.02f + i * 0.07f, -0.14f) * scale,
                    new Vector3(0.02f, 0.1f, 0.08f) * scale, Quaternion.Euler(18f, 0f, 0f));
            }
        }

        private static void DressComposite(AnimatronicRig rig, AnimatronicModelSpec spec, float scale,
            System.Func<Transform, SurfaceBatch> batch)
        {
            // Deliberately wrong. One of everything, fitted by something that had only
            // seen the others from across a dark room.
            DressBat(rig, spec, scale, batch);

            float head = 0.17f * spec.headScale * scale;

            // A second jaw, bolted where a jaw does not go.
            var extra = batch(rig.Chest).For(SurfaceKind.AnimatronicShell);
            extra.CurrentColor = Weathered(spec.shellSecondary, spec, 80);
            extra.AddRoundedBox(new Vector3(0f, 0.06f, -0.13f) * scale,
                new Vector3(head * 0.9f, head * 0.3f, head * 0.5f), Quaternion.Euler(12f, 0f, 0f), 0.25f);

            var extraTeeth = batch(rig.Chest).For(SurfaceKind.AnimatronicMetal);
            extraTeeth.CurrentColor = new Color(0.8f, 0.78f, 0.72f);
            for (int i = 0; i < 5; i++)
            {
                extraTeeth.AddBox(new Vector3((i - 2f) * head * 0.2f, 0.06f * scale, -0.16f * scale),
                    new Vector3(head * 0.14f, head * 0.2f, head * 0.08f));
            }

            // One bear ear. Only one.
            var ear = batch(rig.Head).For(SurfaceKind.AnimatronicShell);
            ear.CurrentColor = Weathered(new Color(0.45f, 0.27f, 0.15f), spec, 81);
            ear.AddSphere(new Vector3(head * 0.74f, head * 1.2f, -head * 0.1f), head * 0.34f, 10, 6,
                new Vector3(1f, 1f, 0.45f));

            // Mole claws on the left hand.
            var claws = batch(rig.HandLeft).For(SurfaceKind.AnimatronicMetal);
            claws.CurrentColor = new Color(0.58f, 0.56f, 0.52f);
            for (int f = 0; f < 3; f++)
            {
                claws.AddCone(new Vector3((f - 1) * 0.04f, -0.11f, 0f) * scale,
                    0.022f * scale, 0.2f * scale, 6, Quaternion.Euler(180f, 0f, 0f));
            }
        }

        // =====================================================================

        /// <summary>
        /// Resolves a layer by name, falling back to Default. NameToLayer returns -1
        /// when the layer is missing, and assigning that to GameObject.layer throws —
        /// which would turn a forgotten TagManager import into a crash.
        /// </summary>
        private static int SafeLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 && layer < 32 ? layer : 0;
        }

        /// <summary>
        /// Applies wear to a colour: desaturated, darkened, and mottled per part so
        /// panels do not all age identically.
        /// </summary>
        private static Color Weathered(Color baseColor, AnimatronicModelSpec spec, int partSeed)
        {
            float variation = ProcNoise.Hash(partSeed, spec.seed, 3, 17);
            float grime = spec.wear * Mathf.Lerp(0.55f, 1f, variation);

            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            s *= 1f - grime * 0.45f;
            v *= 1f - grime * 0.40f;

            var aged = Color.HSVToRGB(h, s, v);

            // Limestone dust settles warm.
            return Color.Lerp(aged, new Color(0.42f, 0.40f, 0.35f), grime * 0.25f);
        }
    }
}
