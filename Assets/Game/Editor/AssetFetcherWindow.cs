using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Grotto.Core;

namespace Grotto.Editor
{
    /// <summary>
    /// Downloads the curated open-licence art and audio listed in
    /// <c>Assets/Game/AssetManifest.json</c>.
    ///
    /// The project deliberately ships no binary art: every surface and sound is
    /// generated at runtime, so a fresh clone is playable and coherent immediately.
    /// This window is the upgrade path — it pulls real CC0 texture sets into
    /// <c>Assets/Game/Art/Downloaded/</c>, and once they are renamed into
    /// <c>Resources/Art/</c> the material library picks them up with no code change.
    ///
    /// Two things it does that a naive downloader would not:
    ///
    /// <b>It refuses to extract outside its target directory.</b> Zip entries can
    /// contain <c>../</c> paths; extracting them blindly ("zip slip") lets a malicious
    /// archive write anywhere the editor can. Every entry path is resolved and checked
    /// before a byte is written.
    ///
    /// <b>It records attribution as it goes.</b> An ATTRIBUTION.md is written beside
    /// the downloads with the source, licence and URL of everything pulled, because
    /// the moment to record that is at download time, not the week before release.
    /// </summary>
    public sealed class AssetFetcherWindow : EditorWindow
    {
        private const string ManifestPath = "Assets/Game/AssetManifest.json";
        private const string DownloadFolder = "Assets/Game/Art/Downloaded";
        private const string ResourcesArtFolder = "Assets/Game/Resources/Art";
        private const string ResourcesAudioFolder = "Assets/Game/Resources/Audio";

        [Serializable]
        private class Entry
        {
            public string id;
            public string displayName;
            public string mapsTo;
            public string source;
            public string licence;
            public string sourceUrl;
            public string downloadUrl;
            public string polyhavenId;
            public string polyhavenType;
            public string polyhavenResolution;
            public bool verified;
            public string notes;
        }

        [Serializable]
        private class Manifest
        {
            public int version;
            public Entry[] surfaces;
            public Entry[] environment;
            public Entry[] audio;
            public Entry[] models;
        }

        private Manifest _manifest;
        private Vector2 _scroll;
        private string _status = "";
        private bool _busy;

        [MenuItem("Tools/Grotto/Asset Fetcher", priority = 40)]
        public static void Open()
        {
            var window = GetWindow<AssetFetcherWindow>(utility: false, title: "Grotto Asset Fetcher");
            window.minSize = new Vector2(620f, 480f);
            window.LoadManifest();
        }

        private void LoadManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                _status = $"No manifest at {ManifestPath}.";
                return;
            }

            try
            {
                _manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
                _status = $"Manifest v{_manifest.version} loaded.";
            }
            catch (Exception ex)
            {
                _status = "Manifest could not be parsed: " + ex.Message;
                _manifest = null;
            }
        }

        private void OnGUI()
        {
            if (_manifest == null)
            {
                EditorGUILayout.HelpBox(_status, MessageType.Warning);
                if (GUILayout.Button("Reload manifest")) LoadManifest();
                return;
            }

            EditorGUILayout.HelpBox(
                "The game is complete without any of this — every surface and sound is " +
                "generated at runtime. These packs replace the generated versions by name.\n\n" +
                "Download URLs marked UNVERIFIED use each source's documented public pattern " +
                "but were not confirmed when the manifest was written. If one fails, use " +
                "'Open source page' and drop the files in by hand.",
                MessageType.Info);

            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reload manifest")) LoadManifest();
                if (GUILayout.Button("Open download folder")) RevealFolder(DownloadFolder);
                if (GUILayout.Button("Check what is wired up")) ReportWiredSurfaces();
            }

            EditorGUILayout.Space(6f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawSection("Surfaces", _manifest.surfaces);
            DrawSection("Environment", _manifest.environment);
            DrawSection("Audio", _manifest.audio);
            DrawSection("Models", _manifest.models);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(_status, EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawSection(string title, Entry[] entries)
        {
            if (entries == null || entries.Length == 0) return;

            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            foreach (var entry in entries)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(entry.displayName, EditorStyles.boldLabel);

                        GUILayout.FlexibleSpace();

                        if (IsDownloaded(entry.id))
                            EditorGUILayout.LabelField("downloaded", EditorStyles.miniLabel, GUILayout.Width(80f));
                        else if (!entry.verified && !string.IsNullOrEmpty(entry.downloadUrl))
                            EditorGUILayout.LabelField("UNVERIFIED", EditorStyles.miniLabel, GUILayout.Width(80f));
                    }

                    EditorGUILayout.LabelField(
                        $"{entry.source}  —  {entry.licence}", EditorStyles.miniLabel);

                    if (!string.IsNullOrEmpty(entry.notes))
                        EditorGUILayout.LabelField(entry.notes, EditorStyles.wordWrappedMiniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(_busy || !CanDownload(entry)))
                        {
                            if (GUILayout.Button("Download", GUILayout.Width(100f)))
                                Download(entry);
                        }

                        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(entry.sourceUrl)))
                        {
                            if (GUILayout.Button("Open source page", GUILayout.Width(140f)))
                                Application.OpenURL(entry.sourceUrl);
                        }

                        if (IsDownloaded(entry.id) && GUILayout.Button("Reveal", GUILayout.Width(70f)))
                            RevealFolder(Path.Combine(DownloadFolder, entry.id));
                    }
                }
            }

            EditorGUILayout.Space(6f);
        }

        private static bool CanDownload(Entry entry)
            => !string.IsNullOrEmpty(entry.downloadUrl) || !string.IsNullOrEmpty(entry.polyhavenId);

        private static bool IsDownloaded(string id)
            => Directory.Exists(Path.Combine(DownloadFolder, id));

        // ---------------------------------------------------------------------
        // Download
        // ---------------------------------------------------------------------

        private void Download(Entry entry)
        {
            _busy = true;

            try
            {
                string url = entry.downloadUrl;

                if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(entry.polyhavenId))
                    url = ResolvePolyHaven(entry);

                if (string.IsNullOrEmpty(url))
                {
                    _status = $"{entry.displayName}: no download URL could be resolved.";
                    return;
                }

                string targetFolder = Path.Combine(DownloadFolder, entry.id);
                Directory.CreateDirectory(targetFolder);

                _status = $"Downloading {entry.displayName}...";
                Repaint();

                byte[] payload = Fetch(url, entry.displayName);
                if (payload == null) return;

                if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    LooksLikeZip(payload))
                {
                    ExtractZip(payload, targetFolder);
                }
                else
                {
                    string fileName = Path.GetFileName(new Uri(url).LocalPath);
                    if (string.IsNullOrEmpty(fileName)) fileName = entry.id + ".bin";
                    File.WriteAllBytes(Path.Combine(targetFolder, fileName), payload);
                }

                WriteAttribution(entry, url);
                AssetDatabase.Refresh();

                _status = $"{entry.displayName} downloaded to {targetFolder}. " +
                          "See docs/ASSETS.md for the renaming step that wires it into the materials.";
            }
            catch (Exception ex)
            {
                _status = $"{entry.displayName} failed: {ex.Message}";
                GLog.Error(LogChannel.Core, _status);
            }
            finally
            {
                _busy = false;
                EditorUtility.ClearProgressBar();
            }
        }

        private static byte[] Fetch(string url, string label)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 180;
                var operation = request.SendWebRequest();

                while (!operation.isDone)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Grotto Asset Fetcher", $"Downloading {label}...", request.downloadProgress))
                    {
                        request.Abort();
                        EditorUtility.ClearProgressBar();
                        return null;
                    }
                }

                EditorUtility.ClearProgressBar();

                if (request.result != UnityWebRequest.Result.Success)
                    throw new IOException($"{request.responseCode} {request.error}");

                return request.downloadHandler.data;
            }
        }

        /// <summary>
        /// Resolves a Poly Haven asset through its documented public files endpoint.
        /// The response is a nested map of type -> resolution -> format -> { url }.
        /// </summary>
        private string ResolvePolyHaven(Entry entry)
        {
            string api = $"https://api.polyhaven.com/files/{entry.polyhavenId}";
            byte[] payload = Fetch(api, entry.displayName + " (index)");
            if (payload == null) return null;

            string json = Encoding.UTF8.GetString(payload);

            // JsonUtility cannot express this shape, and pulling in a JSON library for
            // one editor-only lookup is not worth it. The field we need is the first
            // "url" inside the requested resolution block.
            string resolution = string.IsNullOrEmpty(entry.polyhavenResolution) ? "2k" : entry.polyhavenResolution;
            int resolutionIndex = json.IndexOf($"\"{resolution}\"", StringComparison.Ordinal);
            if (resolutionIndex < 0)
            {
                _status = $"Poly Haven has no '{resolution}' variant for {entry.polyhavenId}.";
                return null;
            }

            int urlIndex = json.IndexOf("\"url\"", resolutionIndex, StringComparison.Ordinal);
            if (urlIndex < 0) return null;

            int start = json.IndexOf('"', json.IndexOf(':', urlIndex) + 1) + 1;
            int end = json.IndexOf('"', start);
            if (start <= 0 || end <= start) return null;

            return json.Substring(start, end - start);
        }

        private static bool LooksLikeZip(byte[] data)
            => data.Length > 4 && data[0] == 0x50 && data[1] == 0x4B;

        /// <summary>
        /// Extracts an archive, refusing any entry that would land outside
        /// <paramref name="targetFolder"/>.
        /// </summary>
        private static void ExtractZip(byte[] payload, string targetFolder)
        {
            string fullTarget = Path.GetFullPath(targetFolder);

            using (var stream = new MemoryStream(payload))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var zipEntry in archive.Entries)
                {
                    // Directory entries have an empty name.
                    if (string.IsNullOrEmpty(zipEntry.Name)) continue;

                    string destination = Path.GetFullPath(Path.Combine(fullTarget, zipEntry.FullName));

                    // Zip slip guard: a crafted archive must not write outside the folder.
                    if (!destination.StartsWith(fullTarget, StringComparison.Ordinal))
                        throw new IOException($"Archive entry '{zipEntry.FullName}' escapes the target folder. Aborting.");

                    Directory.CreateDirectory(Path.GetDirectoryName(destination));

                    using (var entryStream = zipEntry.Open())
                    using (var output = File.Create(destination))
                        entryStream.CopyTo(output);
                }
            }
        }

        private static void WriteAttribution(Entry entry, string resolvedUrl)
        {
            string path = Path.Combine(DownloadFolder, "ATTRIBUTION.md");

            var builder = new StringBuilder();
            if (!File.Exists(path))
            {
                builder.AppendLine("# Downloaded asset attribution");
                builder.AppendLine();
                builder.AppendLine("Written automatically by Tools > Grotto > Asset Fetcher.");
                builder.AppendLine("Everything listed here keeps the licence of its original author.");
                builder.AppendLine("Check this file before shipping a build. See docs/ASSETS.md.");
                builder.AppendLine();
            }

            builder.AppendLine($"## {entry.displayName}");
            builder.AppendLine();
            builder.AppendLine($"- **Folder**: `{DownloadFolder}/{entry.id}`");
            builder.AppendLine($"- **Source**: {entry.source}");
            builder.AppendLine($"- **Licence**: {entry.licence}");
            builder.AppendLine($"- **Page**: {entry.sourceUrl}");
            builder.AppendLine($"- **File**: {resolvedUrl}");
            builder.AppendLine($"- **Fetched**: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
            builder.AppendLine();

            File.AppendAllText(path, builder.ToString());
        }

        // ---------------------------------------------------------------------
        // Reporting
        // ---------------------------------------------------------------------

        /// <summary>
        /// Says which surfaces are currently using downloaded textures rather than
        /// generated ones, by checking exactly what MaterialLibrary checks.
        /// </summary>
        private void ReportWiredSurfaces()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Surfaces using downloaded textures:");

            int wired = 0;
            foreach (Grotto.Procedural.SurfaceKind kind in Enum.GetValues(typeof(Grotto.Procedural.SurfaceKind)))
            {
                string albedo = $"{ResourcesArtFolder}/{kind}_Albedo";
                bool present = File.Exists(albedo + ".png") || File.Exists(albedo + ".jpg")
                               || File.Exists(albedo + ".tga") || File.Exists(albedo + ".exr");

                if (!present) continue;
                wired++;
                builder.AppendLine($"  {kind}");
            }

            if (wired == 0)
                builder.AppendLine("  (none — everything is generated at runtime)");

            builder.AppendLine();
            builder.AppendLine("Audio overrides:");

            int audioCount = 0;
            if (Directory.Exists(ResourcesAudioFolder))
            {
                foreach (var file in Directory.GetFiles(ResourcesAudioFolder))
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    builder.AppendLine("  " + Path.GetFileNameWithoutExtension(file));
                    audioCount++;
                }
            }

            if (audioCount == 0) builder.AppendLine("  (none — everything is synthesised)");

            _status = builder.ToString();
            Debug.Log(_status);
        }

        private static void RevealFolder(string path)
        {
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }
    }
}
