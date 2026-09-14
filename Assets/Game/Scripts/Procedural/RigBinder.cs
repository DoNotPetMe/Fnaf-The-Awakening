using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Procedural
{
    /// <summary>
    /// Binds an arbitrary imported model's bones to <see cref="AnimatronicRig"/>.
    ///
    /// This is the piece that makes a downloaded model usable. The game drives a
    /// character through a fixed set of named bones — head, jaw, neck, spine, arms —
    /// and every model you will ever download names them something else. A rig from
    /// Blender says <c>Head</c>, one from Mixamo says <c>mixamorig:Head</c>, one ripped
    /// from a Source game says <c>ValveBiped.Bip01_Head1</c>, one from a fan modeller
    /// says <c>b_head_01</c>, and a Rigify export says <c>DEF-spine.006</c>. All five
    /// are the same bone.
    ///
    /// So: normalise the name, score it against a keyword table, take the best match
    /// per slot. It is not clever and it does not need to be — bone naming in practice
    /// is a small set of conventions with a lot of decoration around them, and
    /// stripping the decoration gets you most of the way there in one pass.
    ///
    /// Two rules keep it honest:
    ///
    ///   * <b>Every slot is optional except the head.</b> A model with nothing but a
    ///     head bone still works — it just does not open its mouth. A model with no
    ///     bones at all still works, because <see cref="Bind"/> synthesises a head
    ///     proxy at the top of the bounds so the jumpscare has something to aim at.
    ///   * <b>The binder never renames or reparents anything.</b> It only records
    ///     references. Re-importing the model with a different rig and re-binding is
    ///     always safe.
    ///
    /// <see cref="Report"/> comes back with every decision it made, which is what the
    /// import window shows you so a wrong guess is a dropdown away from being fixed
    /// rather than a mystery.
    /// </summary>
    public static class RigBinder
    {
        /// <summary>One bone slot on the rig, and how to recognise it.</summary>
        private readonly struct Slot
        {
            public readonly string Name;
            public readonly bool Sided;

            /// <summary>Keywords, most specific first. Earlier entries score higher.</summary>
            public readonly string[] Keywords;

            /// <summary>Words that rule a bone out however well it otherwise matches.</summary>
            public readonly string[] Veto;

            public Slot(string name, bool sided, string[] keywords, string[] veto = null)
            {
                Name = name;
                Sided = sided;
                Keywords = keywords;
                Veto = veto ?? System.Array.Empty<string>();
            }
        }

        // Ordered most-specific slot first, so "upperarm" is claimed before "arm" can
        // be mistaken for it, and "upperleg" before "leg".
        private static readonly Slot[] Slots =
        {
            new Slot("Jaw", false,
                new[] { "lowerjaw", "jawlower", "mandible", "jaw", "chin", "mouth" },
                new[] { "upperjaw", "jawupper" }),

            new Slot("Head", false,
                new[] { "head", "skull", "cranium" },
                // A head*bone* is wanted; headlight, headband and forehead are not.
                new[] { "forehead", "headlight", "headband", "headset", "overhead" }),

            new Slot("Neck", false, new[] { "neck", "cervical" }),

            new Slot("Chest", false,
                new[]
                {
                    "upperchest", "torsoupper", "upperbody", "chest", "ribcage", "torso",
                    "spine3", "spine2", "spine02", "spine03"
                }),

            new Slot("Spine", false,
                new[]
                {
                    "spine1", "spine01", "torsolower", "lowerbody", "spine",
                    "abdomen", "waist", "stomach", "belly"
                }),

            new Slot("Hips", false, new[] { "hips", "pelvis", "hip", "cog", "root" }),

            new Slot("Shoulder", true, new[] { "shoulder", "clavicle", "collar" }),

            // Bare "arm" last, because Mixamo calls the upper arm simply "LeftArm" —
            // the veto is what stops it swallowing the forearm.
            new Slot("UpperArm", true,
                new[] { "upperarm", "armupper", "humerus", "bicep", "arm" },
                new[] { "forearm", "lowerarm", "armlower" }),

            new Slot("Forearm", true,
                new[] { "forearm", "lowerarm", "armlower", "elbow", "radius", "ulna" }),

            new Slot("Hand", true, new[] { "hand", "wrist", "palm" },
                new[] { "handle" }),

            new Slot("Thigh", true,
                new[] { "upleg", "upperleg", "legupper", "thigh", "femur" },
                new[] { "lowerleg", "leglower", "foreleg" }),

            // Same again: Mixamo's "LeftLeg" is the shin, not the whole leg.
            new Slot("Shin", true,
                new[] { "lowerleg", "leglower", "shin", "calf", "tibia", "knee", "leg" },
                new[] { "upleg", "upperleg", "legupper", "thigh", "foreleg" }),

            new Slot("Foot", true, new[] { "foot", "ankle", "toe" }),

            new Slot("Tail", false, new[] { "tail" })
        };

        // NOTE: keywords are matched against *normalised* names, which have had every
        // separator stripped. A keyword containing an underscore can therefore never
        // match anything — "upper_arm" is dead code, "upperarm" is the live form.

        /// <summary>Decoration that appears around a bone's real name and means nothing.</summary>
        private static readonly string[] Prefixes =
        {
            "mixamorig:", "mixamorig", "valvebiped.", "valvebiped", "bip01_", "bip01",
            "def-", "def_", "org-", "org_", "mch-", "ctrl_", "ik_", "fk_",
            "b_", "bone_", "jnt_", "joint_", "rig_"
        };

        /// <summary>What the binder decided, so the import window can show its work.</summary>
        public sealed class Report
        {
            public readonly Dictionary<string, Transform> Bound = new Dictionary<string, Transform>(24);
            public readonly List<string> Missing = new List<string>(8);
            public readonly List<string> Notes = new List<string>(8);

            public int BoundCount => Bound.Count;
            public bool HasHead => Bound.ContainsKey("Head");
        }

        // ---------------------------------------------------------------------
        // Binding
        // ---------------------------------------------------------------------

        /// <summary>
        /// Finds and assigns every bone it can on <paramref name="rig"/>, searching the
        /// hierarchy under <paramref name="root"/>.
        ///
        /// Existing non-null assignments are left alone, so a hand-corrected rig
        /// survives a re-bind — which is the whole reason the import window's dropdowns
        /// are worth anything.
        /// </summary>
        public static Report Bind(AnimatronicRig rig, Transform root, bool overwriteExisting = false)
        {
            var report = new Report();
            if (rig == null || root == null) return report;

            var bones = new List<Transform>(64);
            root.GetComponentsInChildren(true, bones);

            // The root itself is never a bone: binding Hips to the model's own
            // transform would make every hip rotation move the whole character.
            bones.Remove(root);

            // One bone, one slot. Without this a rig whose chest bone is called
            // "spine2" binds it to both Chest and Spine, and the servo animator then
            // rotates the same transform twice per frame from two different sources.
            //
            // Slots are ordered most-specific first, so first claim wins — which is
            // why Chest is declared above Spine and UpperArm above Forearm.
            var claimed = new HashSet<Transform>();

            foreach (var slot in Slots)
            {
                if (slot.Sided)
                {
                    Assign(rig, report, slot.Name + "Left",
                        Best(bones, slot, Side.Left, claimed), overwriteExisting, claimed);
                    Assign(rig, report, slot.Name + "Right",
                        Best(bones, slot, Side.Right, claimed), overwriteExisting, claimed);
                }
                else
                {
                    Assign(rig, report, slot.Name,
                        Best(bones, slot, Side.None, claimed), overwriteExisting, claimed);
                }
            }

            FillSpineChain(rig, report, claimed);
            BindEyes(rig, root, report, overwriteExisting);
            EnsureHead(rig, root, report);
            Sanity(rig, report);

            GLog.Info(LogChannel.Procedural,
                $"Rig bind on '{root.name}': {report.BoundCount} bone(s), {report.Missing.Count} unmatched.");

            return report;
        }

        private static void Assign(AnimatronicRig rig, Report report, string field,
            Transform bone, bool overwriteExisting, HashSet<Transform> claimed)
        {
            var info = typeof(AnimatronicRig).GetField(field);
            if (info == null) return;

            if (!overwriteExisting && info.GetValue(rig) is Transform existing && existing != null)
            {
                report.Bound[field] = existing;
                claimed.Add(existing);
                return;
            }

            if (bone == null)
            {
                report.Missing.Add(field);
                return;
            }

            info.SetValue(rig, bone);
            report.Bound[field] = bone;
            claimed.Add(bone);
        }

        /// <summary>
        /// Fills gaps in the spine chain from the hierarchy rather than from names.
        ///
        /// Every biped rig has hips, a spine, a chest and a neck stacked in that order,
        /// whatever the bones are called — Rigify's pelvis is "DEF-spine", and a rig
        /// with only "Spine" and "Spine1" is using one of them as the chest. Keyword
        /// matching cannot see any of that; the parent chain can.
        ///
        /// Only ever fills empty slots, and never steals a bone another slot claimed.
        /// </summary>
        private static void FillSpineChain(AnimatronicRig rig, Report report, HashSet<Transform> claimed)
        {
            Fill(ref rig.Hips, rig.Spine, "Hips", report, claimed);
            Fill(ref rig.Spine, rig.Chest, "Spine", report, claimed);
            Fill(ref rig.Chest, rig.Neck, "Chest", report, claimed);
        }

        private static void Fill(ref Transform slot, Transform below, string field,
            Report report, HashSet<Transform> claimed)
        {
            if (slot != null || below == null) return;

            var parent = below.parent;
            if (parent == null || claimed.Contains(parent)) return;

            slot = parent;
            claimed.Add(parent);

            report.Bound[field] = parent;
            report.Missing.Remove(field);
            report.Notes.Add($"{field} taken from the hierarchy: '{parent.name}' is the parent of '{below.name}'.");
        }

        // ---------------------------------------------------------------------
        // Scoring
        // ---------------------------------------------------------------------

        private enum Side { None, Left, Right }

        private static Transform Best(List<Transform> bones, Slot slot, Side side,
            HashSet<Transform> claimed)
        {
            Transform best = null;
            int bestScore = 0;
            int bestDepth = int.MaxValue;

            foreach (var bone in bones)
            {
                if (claimed.Contains(bone)) continue;

                string name = Normalise(bone.name);

                if (slot.Sided && SideOf(bone.name) != side) continue;
                if (Vetoed(name, slot.Veto)) continue;

                int score = Score(name, slot.Keywords);
                if (score == 0) continue;

                // On a tie, take the bone nearer the root. Rigs commonly carry a
                // `head` and a `head_end` or a `head_ctrl` hanging off it; the parent
                // is the one that actually drives the mesh.
                int depth = Depth(bone);
                if (score < bestScore || (score == bestScore && depth >= bestDepth)) continue;

                best = bone;
                bestScore = score;
                bestDepth = depth;
            }

            return best;
        }

        /// <summary>
        /// How well a normalised name matches a keyword list.
        ///
        /// Earlier keywords are more specific and score higher, and an exact match
        /// beats a substring — so a bone literally called "head" wins over
        /// "head_target", and "upperarm" is never mistaken for "arm".
        /// </summary>
        private static int Score(string name, string[] keywords)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                int weight = (keywords.Length - i) * 10;

                if (name == keyword) return weight + 100;
                if (name.StartsWith(keyword)) return weight + 40;
                if (name.EndsWith(keyword)) return weight + 30;
                if (name.Contains(keyword)) return weight;
            }

            return 0;
        }

        private static bool Vetoed(string name, string[] veto)
        {
            for (int i = 0; i < veto.Length; i++)
                if (name.Contains(veto[i])) return true;
            return false;
        }

        /// <summary>
        /// Strips the decoration conventions put around a bone's real name, and
        /// removes the side marker so the keyword table does not have to know about it.
        /// </summary>
        private static string Normalise(string raw)
        {
            string name = raw.ToLowerInvariant().Trim();

            // Everything before the last namespace separator is a rig prefix.
            int colon = name.LastIndexOf(':');
            if (colon >= 0) name = name.Substring(colon + 1);

            foreach (var prefix in Prefixes)
                if (name.StartsWith(prefix)) { name = name.Substring(prefix.Length); break; }

            // Side markers, in every spelling anyone uses.
            name = System.Text.RegularExpressions.Regex.Replace(
                name, @"(^|[._\s-])(l|r|left|right)([._\s-]|$)", "$1$3");

            // Separators and trailing numbering: `spine.006`, `head_01`, `Bone 3`.
            name = name.Replace(".", "").Replace("_", "").Replace("-", "").Replace(" ", "");
            name = System.Text.RegularExpressions.Regex.Replace(name, @"0+(\d)", "$1");

            return name;
        }

        /// <summary>
        /// Which side a bone is on, from every marker convention in common use:
        /// <c>Hand.L</c>, <c>Hand_L</c>, <c>LeftHand</c>, <c>hand_left</c>,
        /// <c>Bip01 L Hand</c>, <c>l_hand</c>, <c>ValveBiped.Bip01_L_Hand</c>.
        /// </summary>
        private static Side SideOf(string raw)
        {
            string name = raw.ToLowerInvariant();

            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"(^|[._\s-])(l|left)([._\s-]|$)"))
                return Side.Left;

            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"(^|[._\s-])(r|right)([._\s-]|$)"))
                return Side.Right;

            // Unseparated: "LeftArm", "righthand".
            if (name.Contains("left")) return Side.Left;
            if (name.Contains("right")) return Side.Right;

            return Side.None;
        }

        private static int Depth(Transform bone)
        {
            int depth = 0;
            for (var t = bone; t != null; t = t.parent) depth++;
            return depth;
        }

        // ---------------------------------------------------------------------
        // Eyes, the head proxy, and a sanity pass
        // ---------------------------------------------------------------------

        /// <summary>
        /// Finds renderers that look like eyes, so the game can light them.
        ///
        /// The eye glow is not decoration here — it is how the player tells a
        /// character apart at twenty metres in the dark, and the jumpscare is framed
        /// around it. A model with no identifiable eye renderer gets generated lamps
        /// instead, which is what <see cref="ImportedModelLibrary"/> does.
        /// </summary>
        private static void BindEyes(AnimatronicRig rig, Transform root, Report report, bool overwrite)
        {
            if (!overwrite && rig.EyeRenderers != null && rig.EyeRenderers.Length > 0)
            {
                report.Notes.Add($"Kept {rig.EyeRenderers.Length} existing eye renderer(s).");
                return;
            }

            var found = new List<Renderer>(4);

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string name = renderer.name.ToLowerInvariant();

                bool looksLikeAnEye =
                    name.Contains("eye") || name.Contains("pupil") ||
                    name.Contains("iris") || name.Contains("sclera");

                // "eyebrow", "eyelid" and "eyelash" are not the lamp.
                if (name.Contains("brow") || name.Contains("lid") || name.Contains("lash")) continue;

                if (looksLikeAnEye) found.Add(renderer);
            }

            if (found.Count == 0)
            {
                report.Notes.Add("No eye renderers found; generated lamps will be attached to the head.");
                return;
            }

            rig.EyeRenderers = found.ToArray();
            report.Notes.Add($"Found {found.Count} eye renderer(s).");
        }

        /// <summary>
        /// Guarantees a head transform even on a model with no skeleton at all.
        ///
        /// Plenty of downloadable models are a single static mesh. Those should still
        /// be usable — you lose the head tracking and the jaw, not the character — so
        /// a proxy is planted at the top of the bounds and the rest of the game
        /// carries on as if it were a real bone.
        /// </summary>
        private static void EnsureHead(AnimatronicRig rig, Transform root, Report report)
        {
            if (rig.Head != null) return;

            var bounds = MeasureBounds(root);
            if (bounds.size == Vector3.zero)
            {
                report.Notes.Add("No renderers and no head bone: this model cannot be framed.");
                return;
            }

            var proxy = new GameObject("Head (proxy)").transform;
            proxy.SetParent(root, worldPositionStays: false);

            // Where a head is on a body: at about nine tenths of the height, on the
            // model's own centre line.
            proxy.position = new Vector3(
                bounds.center.x,
                bounds.min.y + bounds.size.y * 0.9f,
                bounds.center.z);

            rig.Head = proxy;
            report.Bound["Head"] = proxy;
            report.Missing.Remove("Head");
            report.Notes.Add(
                "No head bone found, so a proxy was planted at 90% height. The character " +
                "will not track the player, but the camera and the jumpscare will frame it.");
        }

        /// <summary>
        /// Catches the mistakes the scorer can make that are obvious from the
        /// hierarchy, and unbinds rather than shipping something visibly wrong.
        /// </summary>
        private static void Sanity(AnimatronicRig rig, Report report)
        {
            // A jaw that is not under the head is not a jaw — it is usually a prop
            // called "mouth" somewhere else on the model.
            if (rig.Jaw != null && rig.Head != null && !rig.Jaw.IsChildOf(rig.Head))
            {
                report.Notes.Add($"'{rig.Jaw.name}' matched Jaw but is not under the head; unbound.");
                rig.Jaw = null;
                report.Bound.Remove("Jaw");
                report.Missing.Add("Jaw");
            }

            // Left and right must not be the same bone.
            CheckPair(ref rig.HandLeft, ref rig.HandRight, "Hand", report);
            CheckPair(ref rig.ForearmLeft, ref rig.ForearmRight, "Forearm", report);
            CheckPair(ref rig.UpperArmLeft, ref rig.UpperArmRight, "UpperArm", report);
            CheckPair(ref rig.ThighLeft, ref rig.ThighRight, "Thigh", report);
            CheckPair(ref rig.ShinLeft, ref rig.ShinRight, "Shin", report);
            CheckPair(ref rig.FootLeft, ref rig.FootRight, "Foot", report);
        }

        private static void CheckPair(ref Transform left, ref Transform right, string label, Report report)
        {
            if (left == null || right == null || left != right) return;

            report.Notes.Add(
                $"Both {label} slots matched '{left.name}'. The model probably has no side " +
                "markers in its bone names; the right one was unbound.");

            right = null;
            report.Bound.Remove(label + "Right");
            report.Missing.Add(label + "Right");
        }

        /// <summary>World-space bounds of every renderer under a transform.</summary>
        public static Bounds MeasureBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.position, Vector3.zero);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
