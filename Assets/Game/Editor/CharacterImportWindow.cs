using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Grotto.AI;
using Grotto.Core;
using Grotto.Procedural;

namespace Grotto.Editor
{
    /// <summary>
    /// Turns a downloaded model into a playable character.
    ///
    /// <b>Tools → Grotto → Import a Character Model.</b>
    ///
    /// The job this does is unglamorous and it is the entire difference between
    /// "swapping a model takes twenty minutes and three false starts" and "swapping a
    /// model takes two minutes". Given an FBX, OBJ or glTF:
    ///
    ///  * it scales the thing to the character's authored height, whatever units the
    ///    exporter used;
    ///  * it matches the bones — however they are named — to the ones the game drives,
    ///    and <em>shows you what it matched</em>, with a dropdown per slot so a wrong
    ///    guess is one click from being right;
    ///  * it converts Standard-shader materials to URP so the model is not magenta;
    ///  * it saves a prefab where the runtime looks for it, plus a settings asset for
    ///    the two things it cannot guess — which way the model faces, and where its
    ///    eye lamps go.
    ///
    /// Nothing else in the project changes. <see cref="ImportedModelLibrary"/> picks
    /// the prefab up by the character's id, and a character with no imported model
    /// carries on being generated.
    /// </summary>
    public sealed class CharacterImportWindow : EditorWindow
    {
        private const string ModelsFolder =
            SettingsAssetBuilder.ResourcesPath + "/" + ImportedModelLibrary.ResourceFolder;

        private AnimatronicDefinition _definition;
        private GameObject _source;
        private Vector3 _rotation;
        private bool _addEyeLamps = true;
        private bool _convertMaterials = true;
        private string _attribution = "";

        private GameObject _preview;
        private AnimatronicRig _previewRig;
        private RigBinder.Report _report;
        private Vector2 _scroll;

        private AnimatronicDefinition[] _cast;
        private string[] _castNames;
        private int _castIndex;

        [MenuItem("Tools/Grotto/Import a Character Model", priority = 22)]
        public static void Open()
        {
            var window = GetWindow<CharacterImportWindow>(true, "Import a Character Model");
            window.minSize = new Vector2(540f, 620f);
            window.RefreshCast();
        }

        private void OnDisable() => ClearPreview();

        private void RefreshCast()
        {
            var guids = AssetDatabase.FindAssets("t:AnimatronicDefinition",
                new[] { SettingsAssetBuilder.CastPath });

            var found = new List<AnimatronicDefinition>(guids.Length);
            foreach (var guid in guids)
            {
                var definition = AssetDatabase.LoadAssetAtPath<AnimatronicDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null) found.Add(definition);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.id, b.id));

            _cast = found.ToArray();
            _castNames = new string[_cast.Length];

            for (int i = 0; i < _cast.Length; i++)
            {
                bool imported = File.Exists($"{ModelsFolder}/{_cast[i].id}.prefab");
                _castNames[i] = imported
                    ? $"{_cast[i].displayName}  (model imported)"
                    : $"{_cast[i].displayName}  (generated)";
            }

            if (_cast.Length > 0)
            {
                _castIndex = Mathf.Clamp(_castIndex, 0, _cast.Length - 1);
                _definition = _cast[_castIndex];
            }
        }

        // =====================================================================
        // GUI
        // =====================================================================

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Import a character model", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drop in an FBX, OBJ or glTF and it replaces the generated character.\n\n" +
                "The model is scaled to the character's authored height and its bones are " +
                "matched to the ones the game drives. Check the matches below before you save — " +
                "the only one that matters is Head.",
                MessageType.None);

            EditorGUILayout.Space(6);

            if (_cast == null || _cast.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No character assets found. Run Tools > Grotto > Rebuild Settings Assets first.",
                    MessageType.Warning);

                if (GUILayout.Button("Refresh")) RefreshCast();
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawCharacter();
            EditorGUILayout.Space(8);
            DrawSource();

            if (_preview != null)
            {
                EditorGUILayout.Space(8);
                DrawOrientation();
                EditorGUILayout.Space(8);
                DrawBones();
                EditorGUILayout.Space(8);
                DrawAttribution();
                EditorGUILayout.Space(10);
                DrawSaveRow();
            }

            EditorGUILayout.Space(12);
            DrawWhereToGetModels();

            EditorGUILayout.EndScrollView();
        }

        private void DrawCharacter()
        {
            EditorGUILayout.LabelField("1 — Which character", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _castIndex = EditorGUILayout.Popup("Replacing", _castIndex, _castNames);
            if (EditorGUI.EndChangeCheck()) _definition = _cast[_castIndex];

            if (_definition == null) return;

            EditorGUILayout.LabelField(
                $"    id '{_definition.id}'   ·   authored height {_definition.model.height:0.00} m",
                EditorStyles.miniLabel);

            string existing = $"{ModelsFolder}/{_definition.id}.prefab";
            if (!File.Exists(existing)) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("    A model is already imported for this character.",
                EditorStyles.miniLabel);

            if (GUILayout.Button("Remove it", GUILayout.Width(90f)))
                RemoveImported(_definition.id);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSource()
        {
            EditorGUILayout.LabelField("2 — The model", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _source = (GameObject)EditorGUILayout.ObjectField(
                "Model file", _source, typeof(GameObject), allowSceneObjects: false);

            if (EditorGUI.EndChangeCheck()) BuildPreview();

            if (_source == null)
            {
                EditorGUILayout.HelpBox(
                    "Drag the imported model asset here — the FBX/OBJ/glTF itself, not a " +
                    "prefab you made from it.",
                    MessageType.Info);
                return;
            }

            if (_preview == null) return;

            var bounds = RigBinder.MeasureBounds(_preview.transform);
            EditorGUILayout.LabelField(
                $"    {CountRenderers(_preview)} renderer(s), {CountBones(_preview)} transform(s), " +
                $"source height {bounds.size.y:0.00} m",
                EditorStyles.miniLabel);
        }

        private void DrawOrientation()
        {
            EditorGUILayout.LabelField("3 — Which way it faces", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The one thing that cannot be guessed from the file. Look at the preview in " +
                "the scene view: the character should face +Z, which is away from you in a " +
                "default scene view. If it has its back to the camera, set Y to 180.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            _rotation = EditorGUILayout.Vector3Field("Rotation", _rotation);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (GUILayout.Button("0°")) _rotation = Vector3.zero;
            if (GUILayout.Button("90°")) _rotation = new Vector3(0f, 90f, 0f);
            if (GUILayout.Button("180°")) _rotation = new Vector3(0f, 180f, 0f);
            if (GUILayout.Button("270°")) _rotation = new Vector3(0f, 270f, 0f);
            // Z-up exporters — Blender's default FBX, and most CAD.
            if (GUILayout.Button("Z-up → Y-up")) _rotation = new Vector3(-90f, 0f, 0f);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck() && _preview != null)
            {
                _preview.transform.localRotation = Quaternion.Euler(_rotation);
                FitPreview();
            }
        }

        private void DrawBones()
        {
            EditorGUILayout.LabelField("4 — Bones", EditorStyles.boldLabel);

            if (_report == null || _previewRig == null) return;

            var head = _previewRig.Head;
            if (head == null)
            {
                EditorGUILayout.HelpBox(
                    "No head. The jumpscare needs one to frame on — pick it below, or let " +
                    "the importer plant a proxy at the top of the model.",
                    MessageType.Error);
            }
            else
            {
                string extra = _report.Bound.ContainsKey("Jaw") ? "" :
                    "  No jaw, so the character will not open its mouth — which is fine.";

                EditorGUILayout.HelpBox(
                    $"Matched {_report.BoundCount} bone(s).{extra}",
                    MessageType.Info);
            }

            foreach (var note in _report.Notes)
                EditorGUILayout.LabelField("    " + note, EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            // Head first and on its own, because it is the only one that matters.
            DrawBoneField("Head", important: true);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("    Drives the mouth and the head tracking",
                EditorStyles.miniLabel);

            foreach (var field in new[] { "Jaw", "Neck", "Chest", "Spine", "Hips" })
                DrawBoneField(field);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("    Drives the idle servo jitter — optional",
                EditorStyles.miniLabel);

            foreach (var field in new[]
                     {
                         "ShoulderLeft", "ShoulderRight",
                         "UpperArmLeft", "UpperArmRight",
                         "ForearmLeft", "ForearmRight",
                         "HandLeft", "HandRight",
                         "ThighLeft", "ThighRight",
                         "ShinLeft", "ShinRight",
                         "FootLeft", "FootRight",
                         "Tail"
                     })
            {
                DrawBoneField(field);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Re-match everything"))
            {
                ClearRig(_previewRig);
                _report = RigBinder.Bind(_previewRig, _preview.transform, overwriteExisting: true);
            }

            if (GUILayout.Button("Clear all")) ClearRig(_previewRig);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBoneField(string field, bool important = false)
        {
            var info = typeof(AnimatronicRig).GetField(field);
            if (info == null) return;

            var current = info.GetValue(_previewRig) as Transform;

            // The head is the one binding that matters, so it is bold; an unmatched
            // optional slot is greyed, so the eye skips it.
            var label = new GUIContent(field);
            var previous = GUI.color;

            if (important) GUI.color = new Color(1f, 0.92f, 0.7f);
            else if (current == null) GUI.color = new Color(1f, 1f, 1f, 0.55f);

            EditorGUI.BeginChangeCheck();

            var picked = (Transform)EditorGUILayout.ObjectField(
                label, current, typeof(Transform), allowSceneObjects: true);

            GUI.color = previous;

            if (EditorGUI.EndChangeCheck()) info.SetValue(_previewRig, picked);
        }

        private void DrawAttribution()
        {
            EditorGUILayout.LabelField("5 — Credit", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Where this model came from and under what licence. Fill it in now, while you " +
                "still remember — this is what a credits screen needs, and what you would have " +
                "to produce if anyone ever asked.",
                MessageType.None);

            _attribution = EditorGUILayout.TextArea(_attribution, GUILayout.Height(46f));

            _addEyeLamps = EditorGUILayout.Toggle(
                new GUIContent("Add glowing eyes",
                    "When the model has no eye geometry of its own. The glow is how a " +
                    "character is identified in the dark."),
                _addEyeLamps);

            _convertMaterials = EditorGUILayout.Toggle(
                new GUIContent("Convert materials to URP",
                    "Most downloaded models arrive with Standard-shader materials, which " +
                    "render magenta under URP."),
                _convertMaterials);
        }

        private void DrawSaveRow()
        {
            using (new EditorGUI.DisabledScope(_definition == null || _preview == null))
            {
                if (GUILayout.Button($"Save as {_definition?.id}", GUILayout.Height(32f)))
                    Save();
            }

            EditorGUILayout.LabelField(
                $"    Writes {ModelsFolder}/{_definition?.id}.prefab", EditorStyles.miniLabel);
        }

        private static void DrawWhereToGetModels()
        {
            EditorGUILayout.LabelField("Where to get models", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Sketchfab has thousands of animatronic and mascot-robot models — filter by " +
                "Downloadable and by licence. The Unity Asset Store, itch.io and the FNAF " +
                "fan-model community all have more.\n\n" +
                "Two practical notes. Prefer FBX or glTF over OBJ: OBJ carries no skeleton, so " +
                "you lose the head tracking and the jaw. And whatever you download, put its " +
                "licence in the credit box above — a project full of models whose origin nobody " +
                "recorded is a project that can never be released.",
                MessageType.None);

            if (GUILayout.Button("Open docs/MODELS.md"))
            {
                string path = "docs/MODELS.md";
                if (File.Exists(path)) Application.OpenURL("file://" + Path.GetFullPath(path));
                else EditorUtility.DisplayDialog("Not found", $"{path} is missing.", "OK");
            }
        }

        // =====================================================================
        // Preview
        // =====================================================================

        private void BuildPreview()
        {
            ClearPreview();
            if (_source == null || _definition == null) return;

            _preview = Instantiate(_source);
            _preview.name = $"[Import preview] {_definition.id}";
            _preview.hideFlags = HideFlags.DontSave;
            _preview.transform.localRotation = Quaternion.Euler(_rotation);

            _previewRig = _preview.GetComponent<AnimatronicRig>();
            if (_previewRig == null) _previewRig = _preview.AddComponent<AnimatronicRig>();

            _report = RigBinder.Bind(_previewRig, _preview.transform, overwriteExisting: true);

            FitPreview();
            Selection.activeGameObject = _preview;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        /// <summary>
        /// Scales the preview to the authored height, so what you are looking at in the
        /// scene view is the size it will actually be in the game.
        /// </summary>
        private void FitPreview()
        {
            if (_preview == null || _definition == null) return;

            _preview.transform.localScale = Vector3.one;

            var bounds = RigBinder.MeasureBounds(_preview.transform);
            if (bounds.size.y < 1e-4f) return;

            float scale = Mathf.Max(0.2f, _definition.model.height) / bounds.size.y;
            _preview.transform.localScale = Vector3.one * scale;

            bounds = RigBinder.MeasureBounds(_preview.transform);
            _preview.transform.position += Vector3.up * (_preview.transform.position.y - bounds.min.y);
        }

        private void ClearPreview()
        {
            if (_preview != null) DestroyImmediate(_preview);
            _preview = null;
            _previewRig = null;
            _report = null;
        }

        private static void ClearRig(AnimatronicRig rig)
        {
            foreach (var field in typeof(AnimatronicRig).GetFields(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType == typeof(Transform)) field.SetValue(rig, null);
            }
        }

        // =====================================================================
        // Saving
        // =====================================================================

        private void Save()
        {
            Directory.CreateDirectory(ModelsFolder);
            AssetDatabase.Refresh();

            if (_convertMaterials) ConvertMaterials(_preview);

            // Scale and position are the importer's job at load — the prefab is saved
            // unfitted so a later change to the character's authored height takes
            // effect without re-importing.
            _preview.transform.localScale = Vector3.one;
            _preview.transform.localPosition = Vector3.zero;
            _preview.transform.localRotation = Quaternion.identity;

            string prefabPath = $"{ModelsFolder}/{_definition.id}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(_preview, prefabPath, out bool success);

            if (!success || saved == null)
            {
                EditorUtility.DisplayDialog("Import failed",
                    $"Unity would not write a prefab to {prefabPath}.", "OK");
                return;
            }

            string settingsPath =
                $"{ModelsFolder}/{_definition.id}{ImportedModelLibrary.SettingsSuffix}.asset";

            var settings = AssetDatabase.LoadAssetAtPath<ModelImportSettings>(settingsPath);
            if (settings == null)
            {
                settings = CreateInstance<ModelImportSettings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }

            settings.rotationEuler = _rotation;
            settings.autoFit = true;
            settings.addEyeLamps = _addEyeLamps;
            settings.attribution = _attribution;
            EditorUtility.SetDirty(settings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            WriteAttribution();
            RefreshCast();
            ClearPreview();
            _source = null;

            GLog.Info(LogChannel.Procedural,
                $"Imported a model for '{_definition.id}'. It will be used from the next Play.");

            EditorUtility.DisplayDialog("Imported",
                $"{_definition.displayName} now uses your model.\n\n" +
                "Press Play to see it. If it faces the wrong way or the eyes are in the wrong " +
                "place, the settings asset beside the prefab fixes both without re-importing.",
                "OK");
        }

        /// <summary>
        /// Re-shades a downloaded model's materials for URP.
        ///
        /// Almost every model on the internet ships with Standard-shader materials,
        /// which under URP render as flat magenta. This moves the base colour and the
        /// albedo map across to URP Lit, which is enough to make the model look like
        /// itself — and it is the single most common reason an import "does not work".
        /// </summary>
        private static void ConvertMaterials(GameObject root)
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp == null) return;

            var seen = new HashSet<Material>();
            int converted = 0;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !seen.Add(material)) continue;
                    if (material.shader == urp) continue;

                    // Only touch shaders URP cannot render. A model that already ships
                    // URP or a custom shader is left exactly as its author made it.
                    string shaderName = material.shader != null ? material.shader.name : "";
                    bool isLegacy = shaderName == "Standard" ||
                                    shaderName.StartsWith("Legacy Shaders/") ||
                                    shaderName.StartsWith("Mobile/") ||
                                    shaderName == "Autodesk Interactive";

                    if (!isLegacy) continue;

                    var colour = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                    var albedo = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;

                    material.shader = urp;

                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
                    if (albedo != null && material.HasProperty("_BaseMap"))
                        material.SetTexture("_BaseMap", albedo);

                    EditorUtility.SetDirty(material);
                    converted++;
                }
            }

            if (converted > 0)
                GLog.Info(LogChannel.Procedural, $"Converted {converted} material(s) to URP Lit.");
        }

        /// <summary>
        /// Keeps a running credits file beside the models.
        ///
        /// One file listing every imported model and where it came from, rebuilt from
        /// the settings assets each time. Which means it cannot drift out of date, and
        /// there is exactly one place to look when you need to credit everybody.
        /// </summary>
        private static void WriteAttribution()
        {
            var lines = new List<string>
            {
                "# Imported model credits",
                "",
                "Generated by Tools > Grotto > Import a Character Model. Do not edit by hand —",
                "the text comes from each model's import settings asset.",
                ""
            };

            var guids = AssetDatabase.FindAssets("t:ModelImportSettings", new[] { ModelsFolder });

            foreach (var guid in guids)
            {
                var settings = AssetDatabase.LoadAssetAtPath<ModelImportSettings>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (settings == null) continue;

                string id = settings.name.Replace(ImportedModelLibrary.SettingsSuffix, "");

                lines.Add($"## {id}");
                lines.Add("");
                lines.Add(string.IsNullOrWhiteSpace(settings.attribution)
                    ? "> **No attribution recorded.** Add it in the settings asset before releasing."
                    : settings.attribution.Trim());
                lines.Add("");
            }

            File.WriteAllText($"{ModelsFolder}/CREDITS.md", string.Join("\n", lines));
            AssetDatabase.Refresh();
        }

        private static void RemoveImported(string id)
        {
            if (!EditorUtility.DisplayDialog("Remove the imported model?",
                    $"'{id}' goes back to the generated character. The model file itself is " +
                    "not deleted — only the prefab the game loads.",
                    "Remove", "Cancel"))
            {
                return;
            }

            AssetDatabase.DeleteAsset($"{ModelsFolder}/{id}.prefab");
            AssetDatabase.DeleteAsset($"{ModelsFolder}/{id}{ImportedModelLibrary.SettingsSuffix}.asset");
            AssetDatabase.Refresh();

            WriteAttribution();
            GetWindow<CharacterImportWindow>().RefreshCast();
        }

        // =====================================================================

        private static int CountRenderers(GameObject root)
            => root.GetComponentsInChildren<Renderer>(true).Length;

        private static int CountBones(GameObject root)
            => root.GetComponentsInChildren<Transform>(true).Length;
    }
}
