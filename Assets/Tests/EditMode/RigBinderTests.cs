using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Grotto.Procedural;

namespace Grotto.Tests
{
    /// <summary>
    /// The bone matcher, against the skeleton naming conventions you actually meet.
    ///
    /// This is heuristic code — a keyword table and a scoring function — which means it
    /// is exactly the kind of code that rots the moment somebody adds an entry to fix
    /// one rig and silently breaks three others. Each convention below is a real one
    /// from a real exporter, and the assertions are what a downloaded model needs for
    /// the game to drive it.
    ///
    /// The rule the whole design rests on: <b>only the head is mandatory.</b> Everything
    /// else degrades to something still playable, and a model with no skeleton at all
    /// still gets framed correctly by the camera and the jumpscare.
    /// </summary>
    public class RigBinderTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>(8);

        [TearDown]
        public void TearDown()
        {
            foreach (var root in _roots)
                if (root != null) Object.DestroyImmediate(root);
            _roots.Clear();
        }

        /// <summary>
        /// Builds a skeleton from a flat list of names, chained parent to child in the
        /// order given, with a branch whenever a name repeats a chain position — which
        /// is close enough to a real rig for the binder, since it only looks at names
        /// and at the parent chain.
        /// </summary>
        private AnimatronicRig Build(params string[] boneNames)
        {
            var root = new GameObject("Model");
            _roots.Add(root);

            var rig = root.AddComponent<AnimatronicRig>();

            var parent = root.transform;
            foreach (var name in boneNames)
            {
                var bone = new GameObject(name).transform;
                bone.SetParent(parent, worldPositionStays: false);
                parent = bone;
            }

            return rig;
        }

        private static Transform Bone(AnimatronicRig rig, string name)
        {
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // =====================================================================
        // The conventions
        // =====================================================================

        [Test]
        public void Mixamo()
        {
            var rig = Build(
                "mixamorig:Hips", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2",
                "mixamorig:Neck", "mixamorig:Head", "mixamorig:HeadTop_End");

            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            Assert.AreEqual(Bone(rig, "mixamorig:Head"), rig.Head,
                "The namespace prefix must be stripped before matching.");
            Assert.AreEqual(Bone(rig, "mixamorig:Neck"), rig.Neck);
            Assert.AreEqual(Bone(rig, "mixamorig:Hips"), rig.Hips);

            Assert.AreNotEqual(Bone(rig, "mixamorig:HeadTop_End"), rig.Head,
                "A head *end* marker is not the head bone; the real one is nearer the root.");
        }

        [Test]
        public void Mixamo_LimbNamesThatMeanSomethingElse()
        {
            // The trap: Mixamo's "LeftArm" is the upper arm and its "LeftLeg" is the
            // shin. Taking either at face value binds the wrong bone.
            var rig = Build("mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand");
            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            Assert.AreEqual(Bone(rig, "mixamorig:LeftArm"), rig.UpperArmLeft);
            Assert.AreEqual(Bone(rig, "mixamorig:LeftForeArm"), rig.ForearmLeft);
            Assert.AreEqual(Bone(rig, "mixamorig:LeftHand"), rig.HandLeft);

            var legs = Build("mixamorig:LeftUpLeg", "mixamorig:LeftLeg", "mixamorig:LeftFoot");
            RigBinder.Bind(legs, legs.transform, overwriteExisting: true);

            Assert.AreEqual(Bone(legs, "mixamorig:LeftUpLeg"), legs.ThighLeft);
            Assert.AreEqual(Bone(legs, "mixamorig:LeftLeg"), legs.ShinLeft,
                "Mixamo's 'Leg' is the shin.");
        }

        [Test]
        public void BlenderGeneric()
        {
            var rig = Build("Hips", "Spine", "Chest", "Neck", "Head", "Jaw");
            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            Assert.AreEqual(Bone(rig, "Head"), rig.Head);
            Assert.AreEqual(Bone(rig, "Jaw"), rig.Jaw);
            Assert.AreEqual(Bone(rig, "Neck"), rig.Neck);
            Assert.AreEqual(Bone(rig, "Chest"), rig.Chest);
            Assert.AreEqual(Bone(rig, "Spine"), rig.Spine);
            Assert.AreEqual(Bone(rig, "Hips"), rig.Hips);
        }

        [Test]
        public void Rigify()
        {
            // Rigify has no bone called "hips" or "pelvis" — DEF-spine is the pelvis,
            // which only the hierarchy pass can work out.
            var rig = Build("DEF-spine", "DEF-spine.001", "DEF-spine.003", "DEF-neck", "DEF-head");
            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            Assert.AreEqual(Bone(rig, "DEF-head"), rig.Head);
            Assert.AreEqual(Bone(rig, "DEF-neck"), rig.Neck);
            Assert.IsNotNull(rig.Hips,
                "Rigify's pelvis is called DEF-spine, so Hips has to come from the parent chain.");
        }

        [Test]
        public void SourceValveBiped()
        {
            var rig = Build(
                "ValveBiped.Bip01_Pelvis", "ValveBiped.Bip01_Spine", "ValveBiped.Bip01_Spine2",
                "ValveBiped.Bip01_Neck1", "ValveBiped.Bip01_Head1");

            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            Assert.AreEqual(Bone(rig, "ValveBiped.Bip01_Head1"), rig.Head,
                "The trailing '1' and the ValveBiped prefix both have to be normalised away.");
            Assert.AreEqual(Bone(rig, "ValveBiped.Bip01_Pelvis"), rig.Hips);
        }

        [Test]
        public void SourceSideMarkers()
        {
            var rig = Build("ValveBiped.Bip01_L_Hand");
            var other = Build("ValveBiped.Bip01_R_Hand");

            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);
            RigBinder.Bind(other, other.transform, overwriteExisting: true);

            Assert.IsNotNull(rig.HandLeft, "'_L_' in the middle of a name is a side marker.");
            Assert.IsNull(rig.HandRight);

            Assert.IsNotNull(other.HandRight);
            Assert.IsNull(other.HandLeft);
        }

        [Test]
        public void HandNamedFanRig()
        {
            var rig = Build("root", "torso_lower", "torso_upper", "neck_bone", "head_main", "jaw_lower");
            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            Assert.AreEqual(Bone(rig, "head_main"), rig.Head);
            Assert.AreEqual(Bone(rig, "jaw_lower"), rig.Jaw);
            Assert.AreEqual(Bone(rig, "neck_bone"), rig.Neck);
        }

        [Test]
        public void DotAndUnderscoreSideMarkers()
        {
            foreach (var naming in new[]
                     {
                         new[] { "Hand.L", "Hand.R" },
                         new[] { "Hand_L", "Hand_R" },
                         new[] { "LeftHand", "RightHand" },
                         new[] { "hand_left", "hand_right" },
                         new[] { "l_hand", "r_hand" }
                     })
            {
                var root = new GameObject("Model");
                _roots.Add(root);

                var rig = root.AddComponent<AnimatronicRig>();
                foreach (var name in naming)
                    new GameObject(name).transform.SetParent(root.transform, false);

                RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

                Assert.IsNotNull(rig.HandLeft, $"'{naming[0]}' should read as a left hand.");
                Assert.IsNotNull(rig.HandRight, $"'{naming[1]}' should read as a right hand.");
                Assert.AreNotEqual(rig.HandLeft, rig.HandRight);
            }
        }

        // =====================================================================
        // Degrading cleanly
        // =====================================================================

        [Test]
        public void AModelWithNoSkeletonStillGetsAHead()
        {
            var root = new GameObject("StaticModel");
            _roots.Add(root);

            var rig = root.AddComponent<AnimatronicRig>();

            // One cube, two metres tall, standing on the origin.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, worldPositionStays: false);
            body.transform.localScale = new Vector3(0.6f, 2f, 0.4f);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);

            var report = RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            Assert.IsNotNull(rig.Head,
                "An unrigged model must still be framable — the jumpscare aims at the head.");
            Assert.Greater(rig.Head.position.y, 1.5f,
                "The proxy belongs near the top of the model, not at its origin.");
            Assert.IsTrue(report.Notes.Count > 0, "It should say what it did.");
        }

        [Test]
        public void NothingIsEverBoundTwice()
        {
            // A rig whose chest bone is called "spine2" is the classic way to end up
            // with one transform rotated from two directions every frame.
            var rig = Build("Hips", "Spine", "Spine1", "Spine2", "Neck", "Head");
            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            var seen = new HashSet<Transform>();

            foreach (var bone in rig.AllBones())
            {
                if (bone == null) continue;
                Assert.IsTrue(seen.Add(bone),
                    $"'{bone.name}' is bound to two slots at once.");
            }
        }

        [Test]
        public void TheModelRootIsNeverABone()
        {
            var rig = Build("Spine", "Neck", "Head");
            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            foreach (var bone in rig.AllBones())
            {
                Assert.AreNotEqual(rig.transform, bone,
                    "Binding a slot to the model's own transform makes every rotation " +
                    "move the whole character.");
            }
        }

        [Test]
        public void AJawSomewhereElseOnTheModelIsNotAJaw()
        {
            var root = new GameObject("Model");
            _roots.Add(root);

            var rig = root.AddComponent<AnimatronicRig>();

            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, worldPositionStays: false);

            // A prop the modeller called "mouth", parented to the body rather than the
            // head — so it is not the jaw, whatever it is called.
            var stray = new GameObject("mouth_decal").transform;
            stray.SetParent(root.transform, worldPositionStays: false);

            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            Assert.AreEqual(head, rig.Head);
            Assert.IsNull(rig.Jaw, "A jaw that is not under the head was matched by name alone.");
        }

        [Test]
        public void HandCorrectionsSurviveARebind()
        {
            var rig = Build("Hips", "Spine", "Neck", "Head", "Jaw");
            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            // The kind of correction somebody makes in the import window.
            var chosen = Bone(rig, "Spine");
            rig.Chest = chosen;

            RigBinder.Bind(rig, rig.transform, overwriteExisting: false);

            Assert.AreEqual(chosen, rig.Chest,
                "A non-overwriting bind must leave hand-picked bones alone, or the " +
                "import window's dropdowns are worthless.");
        }

        [Test]
        public void EyeRenderersAreFoundButEyelidsAreNot()
        {
            var root = new GameObject("Model");
            _roots.Add(root);

            var rig = root.AddComponent<AnimatronicRig>();
            new GameObject("Head").transform.SetParent(root.transform, worldPositionStays: false);

            foreach (var name in new[] { "Eye_L", "Eye_R", "Eyelid_L", "eyebrow" })
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = name;
                go.transform.SetParent(root.transform, worldPositionStays: false);
            }

            RigBinder.Bind(rig, rig.transform, overwriteExisting: true);

            Assert.IsNotNull(rig.EyeRenderers);
            Assert.AreEqual(2, rig.EyeRenderers.Length,
                "Eyelids and eyebrows are not lamps.");
        }
    }
}
