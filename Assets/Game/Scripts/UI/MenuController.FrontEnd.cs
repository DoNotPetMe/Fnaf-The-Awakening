using UnityEngine;
using UnityEngine.UI;
using Grotto.AI;
using Grotto.Audio;
using Grotto.Core;
using Grotto.Facility;
using Grotto.Rendering;

namespace Grotto.UI
{
    /// <summary>
    /// The front end: title, site picker, night select, settings and the cast
    /// dossiers.
    ///
    /// Separated from the pause and summary screens in <c>MenuController.cs</c> only
    /// by file, because they share a canvas, a screen stack and a palette but are read
    /// at completely different times — one while setting up a session, the other with
    /// a night already running.
    /// </summary>
    public sealed partial class MenuController
    {
        private const float ColumnWidth = 460f;
        private const float ButtonHeight = 58f;
        private const float ButtonGap = 12f;

        // =====================================================================
        // Title
        // =====================================================================

        private void BuildTitleScreen()
        {
            var screen = BeginScreen(Screen.Title);

            // Lighter than a pause scrim: the title sits over a live view of the site,
            // and the whole point is that you can see it.
            Scrim(screen, 0.62f);
            Vignette(screen);

            var layout = FacilityRuntime.Instance != null ? FacilityRuntime.Instance.Layout : null;

            // --- Title block, left ---------------------------------------------
            var title = UIFactory.Label(screen, "Title", "THE AWAKENING", 92, TextAnchor.LowerLeft);
            UIFactory.Anchor(title.rectTransform, UIFactory.TopLeft,
                new Vector2(150f, -170f), new Vector2(900f, 110f));

            var rule = UIFactory.Rule(screen, "Rule", 420f, UIFactory.InkDim);
            UIFactory.Anchor(rule.rectTransform, UIFactory.TopLeft,
                new Vector2(154f, -186f), new Vector2(420f, 2f));

            var strap = UIFactory.Label(screen, "Strapline",
                "RECLAMATION SITE MONITOR   ·   11 PM TO 6 AM", 20, TextAnchor.UpperLeft,
                new Color(0.58f, 0.52f, 0.44f));
            UIFactory.Anchor(strap.rectTransform, UIFactory.TopLeft,
                new Vector2(154f, -198f), new Vector2(700f, 28f));

            // --- Menu column, left ---------------------------------------------
            var column = Column(screen, new Vector2(150f, -280f), UIFactory.TopLeft);

            int highest = _save != null ? _save.Data.highestNightUnlocked : 1;
            int resume = Mathf.Clamp(highest, 1, 6);
            string siteId = layout != null ? layout.siteId : SiteCatalog.DefaultSiteId;

            AddButton(column, $"CONTINUE  —  NIGHT {resume}",
                () => SessionRequest.Begin(siteId, resume));

            AddButton(column, "CHOOSE A NIGHT", () => Navigate(BuildNightSelectScreen));
            AddButton(column, "SITES", () => Navigate(BuildSitesScreen));
            AddButton(column, "THE CAST", () => Navigate(BuildCastScreen));
            AddButton(column, "SETTINGS", () => Navigate(BuildSettingsScreen));
            AddButton(column, "QUIT", Quit);

            // --- Selected site, right ------------------------------------------
            if (layout != null)
            {
                var card = UIFactory.Card(screen, "SiteCard", layout.siteName.ToUpperInvariant(),
                    new Vector2(560f, 400f), out _, UIFactory.Ink);
                UIFactory.Anchor(card, UIFactory.TopRight, new Vector2(-150f, -260f), new Vector2(560f, 400f));

                var tagline = UIFactory.Label(card, "Tagline", layout.siteTagline, 19,
                    TextAnchor.UpperLeft, UIFactory.InkDim, wrap: true);
                UIFactory.Anchor(tagline.rectTransform, UIFactory.TopLeft,
                    new Vector2(20f, -52f), new Vector2(520f, 50f));

                var blurb = UIFactory.Label(card, "Blurb", layout.siteBlurb, 16,
                    TextAnchor.UpperLeft, new Color(0.52f, 0.48f, 0.42f), wrap: true);
                UIFactory.Anchor(blurb.rectTransform, UIFactory.TopLeft,
                    new Vector2(20f, -112f), new Vector2(520f, 150f));

                SitePressureBars(card, layout, new Vector2(20f, -278f), 520f);

                var hint = UIFactory.Label(card, "Hint", "SITES  ›  change", 15,
                    TextAnchor.LowerRight, new Color(0.42f, 0.40f, 0.36f));
                UIFactory.Anchor(hint.rectTransform, UIFactory.BottomRight,
                    new Vector2(-20f, 14f), new Vector2(300f, 20f));
            }

            Footer(screen);
            Show(frontEnd: true);
        }

        /// <summary>
        /// The three pressure bars that make one site comparable with another at a
        /// glance: how fast the water rises, how fast the air goes, how fast the tank
        /// empties. Each is drawn against a 2x scale so 1.0 sits at the halfway mark
        /// and "twice as hard as the grotto" is legible as a full bar.
        /// </summary>
        private static void SitePressureBars(RectTransform parent, FacilityLayout layout,
            Vector2 origin, float width)
        {
            var header = UIFactory.Label(parent, "PressureHeader", "SITE PRESSURE", 14,
                TextAnchor.UpperLeft, new Color(0.45f, 0.42f, 0.38f));
            UIFactory.Anchor(header.rectTransform, UIFactory.TopLeft, origin, new Vector2(width, 18f));

            var rows = new (string caption, float value, Color color)[]
            {
                ("WATER", layout.waterScale, UIFactory.InkCold),
                ("AIR", layout.airScale, new Color(0.55f, 0.82f, 0.58f)),
                ("FUEL", layout.fuelScale, UIFactory.InkWarn)
            };

            for (int i = 0; i < rows.Length; i++)
            {
                var (caption, value, color) = rows[i];
                var bar = new GameObject("Bar_" + caption, typeof(RectTransform));
                bar.transform.SetParent(parent, worldPositionStays: false);
                bar.layer = parent.gameObject.layer;

                UIFactory.Anchor((RectTransform)bar.transform, UIFactory.TopLeft,
                    origin + new Vector2(0f, -24f - i * 22f), new Vector2(width, 20f));

                UIFactory.StatBar(bar.transform, "Fill", caption, value * 0.5f,
                    new Vector2(width, 20f), color, $"x{value:0.00}");
            }

            var start = UIFactory.Label(parent, "StartWater",
                $"Starts at {Mathf.RoundToInt(layout.startingWaterLevel * 100f)}% water   ·   " +
                $"safe band {layout.gates.SafeBand:0.00}",
                14, TextAnchor.UpperLeft, new Color(0.42f, 0.40f, 0.36f));
            UIFactory.Anchor(start.rectTransform, UIFactory.TopLeft,
                origin + new Vector2(0f, -96f), new Vector2(width, 18f));
        }

        // =====================================================================
        // Sites
        // =====================================================================

        private void BuildSitesScreen()
        {
            var screen = BeginScreen(Screen.Sites);
            Scrim(screen, 0.9f);

            Heading(screen, "SITES",
                "Three buildings, one job. They disagree about what the water is for.");

            var save = _save != null ? _save.Data : null;
            string selected = FacilityRuntime.Instance != null
                ? FacilityRuntime.Instance.Layout.siteId
                : SiteCatalog.SelectedSiteId(save);

            const float cardWidth = 520f;
            const float cardHeight = 500f;
            const float gap = 26f;

            int count = SiteCatalog.Count;
            float totalWidth = count * cardWidth + (count - 1) * gap;
            float x = -totalWidth * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var entry = SiteCatalog.EntryAt(i);
                var layout = SiteCatalog.Load(entry.Id);
                bool unlocked = SiteCatalog.IsUnlocked(layout, save);
                bool isSelected = string.Equals(layout.siteId, selected, System.StringComparison.OrdinalIgnoreCase);

                var accent = !unlocked
                    ? new Color(0.34f, 0.32f, 0.30f)
                    : isSelected ? UIFactory.Ink : UIFactory.InkDim;

                var card = UIFactory.Card(screen, "Site_" + entry.Id,
                    layout.siteName.ToUpperInvariant(), new Vector2(cardWidth, cardHeight), out var heading, accent);

                heading.fontSize = 22;
                UIFactory.Anchor(card, UIFactory.Centre,
                    new Vector2(x + cardWidth * 0.5f, -40f), new Vector2(cardWidth, cardHeight));

                var tagline = UIFactory.Label(card, "Tagline", layout.siteTagline, 17,
                    TextAnchor.UpperLeft, accent, wrap: true);
                UIFactory.Anchor(tagline.rectTransform, UIFactory.TopLeft,
                    new Vector2(20f, -56f), new Vector2(cardWidth - 40f, 48f));

                var emphasis = UIFactory.Label(card, "Emphasis", layout.siteEmphasis.ToUpperInvariant(), 14,
                    TextAnchor.UpperLeft, new Color(0.48f, 0.45f, 0.40f));
                UIFactory.Anchor(emphasis.rectTransform, UIFactory.TopLeft,
                    new Vector2(20f, -108f), new Vector2(cardWidth - 40f, 18f));

                var blurb = UIFactory.Label(card, "Blurb", layout.siteBlurb, 15,
                    TextAnchor.UpperLeft, new Color(0.50f, 0.47f, 0.42f), wrap: true);
                UIFactory.Anchor(blurb.rectTransform, UIFactory.TopLeft,
                    new Vector2(20f, -134f), new Vector2(cardWidth - 40f, 130f));

                var topology = UIFactory.Label(card, "Topology",
                    $"{layout.nodes.Count} rooms   ·   {CameraCount(layout)} cameras   ·   " +
                    $"{layout.links.Count} routes", 14, TextAnchor.UpperLeft,
                    new Color(0.44f, 0.42f, 0.38f));
                UIFactory.Anchor(topology.rectTransform, UIFactory.TopLeft,
                    new Vector2(20f, -262f), new Vector2(cardWidth - 40f, 18f));

                SitePressureBars(card, layout, new Vector2(20f, -290f), cardWidth - 40f);

                if (unlocked)
                {
                    string label = isSelected ? "SELECTED" : "SELECT";
                    var button = UIFactory.TextButton(card, "Select", label,
                        new Vector2(cardWidth - 40f, 48f), () => SelectSite(layout.siteId), 22);
                    UIFactory.Anchor((RectTransform)button.transform, UIFactory.BottomCentre,
                        new Vector2(0f, 18f), new Vector2(cardWidth - 40f, 48f));

                    if (isSelected) button.interactable = false;
                }
                else
                {
                    int remaining = SiteCatalog.NightsRemaining(layout, save);
                    var locked = UIFactory.Label(card, "Locked",
                        $"LOCKED  —  {remaining} more night{(remaining == 1 ? "" : "s")} to clear",
                        18, TextAnchor.MiddleCenter, new Color(0.42f, 0.40f, 0.38f));
                    UIFactory.Anchor(locked.rectTransform, UIFactory.BottomCentre,
                        new Vector2(0f, 18f), new Vector2(cardWidth - 40f, 48f));
                }

                x += cardWidth + gap;
            }

            BackButton(screen);
            Show(frontEnd: true);
        }

        private static int CameraCount(FacilityLayout layout)
        {
            int cameras = 0;
            for (int i = 0; i < layout.nodes.Count; i++)
                if (layout.nodes[i].hasCamera) cameras++;
            return cameras;
        }

        /// <summary>
        /// Records the choice and reloads, because the site decides what the scene is
        /// built out of. See <see cref="SessionRequest"/> for why that is a reload
        /// rather than a live rebuild.
        /// </summary>
        private void SelectSite(string siteId)
        {
            if (_save != null)
            {
                _save.Data.selectedSiteId = siteId;
                _save.Save();
            }

            SessionRequest.Clear();
            SessionRequest.Begin(siteId, 0);
        }

        // =====================================================================
        // Night select
        // =====================================================================

        private void BuildNightSelectScreen()
        {
            var screen = BeginScreen(Screen.NightSelect);
            Scrim(screen, 0.9f);

            var layout = FacilityRuntime.Instance != null ? FacilityRuntime.Instance.Layout : null;
            string siteId = layout != null ? layout.siteId : SiteCatalog.DefaultSiteId;

            Heading(screen, "CHOOSE A NIGHT",
                layout != null ? layout.siteName.ToUpperInvariant() : "");

            var save = _save != null ? _save.Data : null;
            int unlocked = save != null ? save.highestNightUnlocked : 1;

            const float cardWidth = 280f;
            const float cardHeight = 150f;
            const float gap = 20f;

            // Six nights in two rows of three, which reads better than one row of six
            // and leaves room for the record under each.
            for (int night = 1; night <= 6; night++)
            {
                int index = night - 1;
                int row = index / 3;
                int col = index % 3;

                float px = (col - 1) * (cardWidth + gap);
                float py = -40f - row * (cardHeight + gap);

                bool open = night <= unlocked;
                var record = save?.GetOrCreateRecord(night, siteId);
                bool cleared = record != null && record.completed;

                var accent = !open
                    ? new Color(0.32f, 0.30f, 0.28f)
                    : cleared ? new Color(0.55f, 0.82f, 0.58f) : UIFactory.Ink;

                var card = UIFactory.Card(screen, $"Night_{night}", $"NIGHT {night}",
                    new Vector2(cardWidth, cardHeight), out _, accent);
                UIFactory.Anchor(card, UIFactory.Centre, new Vector2(px, py),
                    new Vector2(cardWidth, cardHeight));

                string status;
                if (!open) status = "LOCKED";
                else if (cleared) status = "CLEARED";
                else if (record != null && record.attempts > 0)
                    status = $"{record.attempts} attempt{(record.attempts == 1 ? "" : "s")}, " +
                             $"{record.deaths} death{(record.deaths == 1 ? "" : "s")}";
                else status = "NOT ATTEMPTED";

                var statusLabel = UIFactory.Label(card, "Status", status, 15, TextAnchor.UpperLeft,
                    new Color(0.48f, 0.45f, 0.40f));
                UIFactory.Anchor(statusLabel.rectTransform, UIFactory.TopLeft,
                    new Vector2(20f, -50f), new Vector2(cardWidth - 40f, 18f));

                if (record != null && record.bestSurvivalSeconds > 0f)
                {
                    int minutes = Mathf.FloorToInt(record.bestSurvivalSeconds / 60f);
                    int seconds = Mathf.FloorToInt(record.bestSurvivalSeconds % 60f);

                    var best = UIFactory.Label(card, "Best", $"BEST  {minutes}m {seconds:00}s", 14,
                        TextAnchor.UpperLeft, new Color(0.42f, 0.40f, 0.36f));
                    UIFactory.Anchor(best.rectTransform, UIFactory.TopLeft,
                        new Vector2(20f, -70f), new Vector2(cardWidth - 40f, 18f));
                }

                if (!open) continue;

                int captured = night;
                var button = UIFactory.TextButton(card, "Start", "BEGIN",
                    new Vector2(cardWidth - 40f, 42f), () => SessionRequest.Begin(siteId, captured), 20);
                UIFactory.Anchor((RectTransform)button.transform, UIFactory.BottomCentre,
                    new Vector2(0f, 14f), new Vector2(cardWidth - 40f, 42f));
            }

            if (save != null && save.customNightUnlocked)
            {
                var custom = UIFactory.TextButton(screen, "Custom", "CUSTOM NIGHT",
                    new Vector2(ColumnWidth, ButtonHeight), () => SessionRequest.Begin(siteId, 7), 24);
                UIFactory.Anchor((RectTransform)custom.transform, UIFactory.Centre,
                    new Vector2(0f, -420f), new Vector2(ColumnWidth, ButtonHeight));
            }

            BackButton(screen);
            Show(frontEnd: true);
        }

        // =====================================================================
        // Settings
        // =====================================================================

        private void BuildSettingsScreen()
        {
            var screen = BeginScreen(Screen.Settings);
            Scrim(screen, 0.94f);

            Heading(screen, "SETTINGS", "Changes apply immediately and are saved when you leave.");

            var settings = Settings;

            const float rowWidth = 560f;
            const float rowHeight = 44f;
            const float rowGap = 8f;

            // --- Left column: difficulty, audio, input --------------------------
            float leftY = -40f;
            SectionLabel(screen, "DIFFICULTY", new Vector2(-300f, leftY), rowWidth);
            leftY -= 34f;

            AddDifficultyRow(screen, settings, new Vector2(-300f, leftY), rowWidth, rowHeight);
            leftY -= rowHeight + 4f;

            var difficultyNote = UIFactory.Label(screen, "DifficultyNote",
                SettingsData.DescribeDifficulty(settings.difficulty), 15,
                TextAnchor.UpperLeft, new Color(0.45f, 0.42f, 0.38f), wrap: true);
            UIFactory.Anchor(difficultyNote.rectTransform, UIFactory.Centre,
                new Vector2(-300f, leftY - 14f), new Vector2(rowWidth, 40f));
            _difficultyNote = difficultyNote;

            leftY -= 52f;

            SectionLabel(screen, "AUDIO", new Vector2(-300f, leftY), rowWidth);
            leftY -= 34f;

            AddSlider(screen, "Master", "Master volume", new Vector2(-300f, leftY), rowWidth, rowHeight,
                settings.masterVolume, 0f, 1f, v => { settings.masterVolume = v; ApplySettings(settings); });
            leftY -= rowHeight + rowGap;

            AddSlider(screen, "Sfx", "Effects", new Vector2(-300f, leftY), rowWidth, rowHeight,
                settings.sfxVolume, 0f, 1f, v => { settings.sfxVolume = v; ApplySettings(settings); });
            leftY -= rowHeight + rowGap;

            AddSlider(screen, "Music", "Music and stings", new Vector2(-300f, leftY), rowWidth, rowHeight,
                settings.musicVolume, 0f, 1f, v => { settings.musicVolume = v; ApplySettings(settings); });
            leftY -= rowHeight + rowGap + 18f;

            SectionLabel(screen, "LOOKING AROUND", new Vector2(-300f, leftY), rowWidth);
            leftY -= 34f;

            AddSlider(screen, "Sensitivity", "Look sensitivity", new Vector2(-300f, leftY), rowWidth, rowHeight,
                settings.lookSensitivity, 0.25f, 2.5f,
                v => { settings.lookSensitivity = v; ApplySettings(settings); },
                v => $"{v:0.00}x");
            leftY -= rowHeight + rowGap;

            AddToggle(screen, "InvertY", "Invert vertical look", new Vector2(-300f, leftY), rowWidth, rowHeight,
                settings.invertY, v => { settings.invertY = v; ApplySettings(settings); });

            // --- Right column: accessibility and display ------------------------
            float rightY = -40f;
            SectionLabel(screen, "ACCESSIBILITY", new Vector2(300f, rightY), rowWidth);
            rightY -= 34f;

            AddToggle(screen, "Photosensitive", "Reduce flashing", new Vector2(300f, rightY), rowWidth, 54f,
                settings.photosensitiveMode,
                v => { settings.photosensitiveMode = v; ApplySettings(settings); },
                "Caps strobing, flicker and jumpscare contrast.");
            rightY -= 54f + rowGap;

            AddToggle(screen, "QuietScares", "Quieter jumpscares", new Vector2(300f, rightY), rowWidth, 54f,
                settings.reducedJumpscareAudio,
                v => { settings.reducedJumpscareAudio = v; ApplySettings(settings); },
                "Replaces the sting with a softer cue.");
            rightY -= 54f + rowGap;

            AddToggle(screen, "Subtitles", "Subtitles", new Vector2(300f, rightY), rowWidth, rowHeight,
                settings.subtitles, v => { settings.subtitles = v; ApplySettings(settings); });
            rightY -= rowHeight + rowGap + 18f;

            SectionLabel(screen, "DISPLAY", new Vector2(300f, rightY), rowWidth);
            rightY -= 34f;

            AddQualityRow(screen, settings, new Vector2(300f, rightY), rowWidth, rowHeight);
            rightY -= rowHeight + rowGap;

            AddFrameRateRow(screen, settings, new Vector2(300f, rightY), rowWidth, rowHeight);

            // --- Footer ----------------------------------------------------------
            var reset = UIFactory.TextButton(screen, "ResetProfile", "RESET ALL PROGRESS",
                new Vector2(320f, 44f), ConfirmResetProfile, 18);
            UIFactory.Anchor((RectTransform)reset.transform, UIFactory.BottomLeft,
                new Vector2(150f, 60f), new Vector2(320f, 44f));

            BackButton(screen);
            Show(frontEnd: true);
        }

        /// <summary>
        /// Pushes a settings change to everything that cares, then saves.
        ///
        /// Each of these owns one aspect and already has an ApplySettings of its own —
        /// this is the fan-out, so a slider drag is heard immediately rather than at
        /// the next scene load.
        /// </summary>
        private void ApplySettings(SettingsData settings)
        {
            // GameBootstrap does not register itself — it runs before the locator has
            // anything in it — so this one is found rather than looked up.
            var bootstrap = FindAnyObjectByType<GameBootstrap>();
            if (bootstrap != null) bootstrap.ApplySettings(settings);

            if (ServiceLocator.TryGet(out AudioDirector audio)) audio.ApplySettings(settings);
            if (ServiceLocator.TryGet(out PostFxController postFx)) postFx.ApplySettings(settings);

            _save?.Save();
        }

        private void ConfirmResetProfile()
        {
            var screen = BeginScreen(Screen.Settings);
            Scrim(screen, 0.96f);

            Heading(screen, "RESET EVERYTHING?",
                "Every night record, unlock and setting is erased. This cannot be undone.",
                UIFactory.InkAlarm);

            var column = Column(screen, new Vector2(0f, -280f), UIFactory.TopCentre);

            AddButton(column, "YES, ERASE IT", () =>
            {
                _save?.ResetProfile();
                SessionRequest.ReturnToTitle();
            });

            AddButton(column, "KEEP MY PROGRESS", BuildSettingsScreen);

            Show(frontEnd: true);
        }

        private Text _difficultyNote;

        /// <summary>
        /// The difficulty chooser.
        ///
        /// Deliberately at the top of the settings screen rather than buried under
        /// "gameplay": it is the one setting here that changes what the game is, and a
        /// player who wants an easier night should not have to go looking for it under
        /// the volume sliders.
        /// </summary>
        private void AddDifficultyRow(RectTransform parent, SettingsData settings,
            Vector2 position, float width, float height)
        {
            var presets = new[]
            {
                DifficultyPreset.Survey,
                DifficultyPreset.Standard,
                DifficultyPreset.Reclamation
            };

            var labels = new[] { "SURVEY", "STANDARD", "RECLAMATION" };

            int index = System.Array.IndexOf(presets, settings.difficulty);
            if (index < 0) index = 1;

            var row = new GameObject("Difficulty", typeof(RectTransform));
            row.transform.SetParent(parent, worldPositionStays: false);
            row.layer = parent.gameObject.layer;
            UIFactory.Anchor((RectTransform)row.transform, UIFactory.Centre, position,
                new Vector2(width, height));

            UIFactory.OptionRow(row.transform, "Row", "Resource difficulty",
                new Vector2(width, height), labels, index, i =>
                {
                    settings.difficulty = presets[i];
                    if (_difficultyNote != null)
                        _difficultyNote.text = SettingsData.DescribeDifficulty(settings.difficulty);
                    ApplySettings(settings);
                });
        }

        private void AddQualityRow(RectTransform parent, SettingsData settings,
            Vector2 position, float width, float height)
        {
            var names = QualitySettings.names;
            var options = new string[names.Length + 1];
            options[0] = "PROJECT DEFAULT";
            for (int i = 0; i < names.Length; i++) options[i + 1] = names[i].ToUpperInvariant();

            int index = settings.qualityLevel < 0 ? 0 : Mathf.Clamp(settings.qualityLevel + 1, 0, options.Length - 1);

            var row = new GameObject("Quality", typeof(RectTransform));
            row.transform.SetParent(parent, worldPositionStays: false);
            row.layer = parent.gameObject.layer;
            UIFactory.Anchor((RectTransform)row.transform, UIFactory.Centre, position, new Vector2(width, height));

            UIFactory.OptionRow(row.transform, "Row", "Quality", new Vector2(width, height), options, index,
                i => { settings.qualityLevel = i - 1; ApplySettings(settings); });
        }

        private void AddFrameRateRow(RectTransform parent, SettingsData settings,
            Vector2 position, float width, float height)
        {
            var caps = new[] { -1, 30, 60, 120, 144 };
            var options = new[] { "UNCAPPED", "30", "60", "120", "144" };

            int index = 0;
            for (int i = 0; i < caps.Length; i++)
                if (caps[i] == settings.targetFrameRate) index = i;

            var row = new GameObject("FrameRate", typeof(RectTransform));
            row.transform.SetParent(parent, worldPositionStays: false);
            row.layer = parent.gameObject.layer;
            UIFactory.Anchor((RectTransform)row.transform, UIFactory.Centre, position, new Vector2(width, height));

            UIFactory.OptionRow(row.transform, "Row", "Frame rate cap", new Vector2(width, height),
                options, index, i => { settings.targetFrameRate = caps[i]; ApplySettings(settings); });
        }

        // =====================================================================
        // Cast
        // =====================================================================

        private void BuildCastScreen()
        {
            var screen = BeginScreen(Screen.Cast);
            Scrim(screen, 0.93f);

            Heading(screen, "THE CAST",
                "Four are on the 1994 inventory. The fifth is on both halves of it.");

            var definitions = Resources.LoadAll<AnimatronicDefinition>(CastSpawner.ResourceFolder);
            var layout = FacilityRuntime.Instance != null ? FacilityRuntime.Instance.Layout : null;

            if (definitions == null || definitions.Length == 0)
            {
                var missing = UIFactory.Label(screen, "Missing",
                    "No character assets found.\nRun Tools > Grotto > Rebuild Settings Assets.",
                    22, TextAnchor.MiddleCenter, UIFactory.InkAlarm, wrap: true);
                UIFactory.Anchor(missing.rectTransform, UIFactory.Centre, Vector2.zero,
                    new Vector2(800f, 120f));

                BackButton(screen);
                Show(frontEnd: true);
                return;
            }

            System.Array.Sort(definitions, (a, b) => string.CompareOrdinal(a.id, b.id));

            const float rowWidth = 1380f;
            const float rowHeight = 128f;
            const float gap = 12f;

            float y = -30f;

            foreach (var definition in definitions)
            {
                var card = UIFactory.Card(screen, "Cast_" + definition.id,
                    definition.displayName.ToUpperInvariant(), new Vector2(rowWidth, rowHeight),
                    out var heading, definition.mapColor);

                heading.fontSize = 22;
                UIFactory.Anchor(card, UIFactory.TopCentre, new Vector2(0f, y), new Vector2(rowWidth, rowHeight));

                var dossier = UIFactory.Label(card, "Dossier", definition.dossier, 15,
                    TextAnchor.UpperLeft, new Color(0.52f, 0.49f, 0.44f), wrap: true);
                UIFactory.Anchor(dossier.rectTransform, UIFactory.TopLeft,
                    new Vector2(20f, -48f), new Vector2(rowWidth * 0.62f, 74f));

                // What actually matters at the desk: how it moves, what stops it, and
                // which of your four approaches it uses tonight.
                var placement = layout != null ? layout.FindPlacement(definition.id) : null;

                string route = placement != null && placement.attackNodes.Count > 0
                    ? string.Join(", ", placement.attackNodes)
                    : string.Join(", ", definition.attackNodes);

                string home = placement != null && !string.IsNullOrWhiteSpace(placement.homeNode)
                    ? placement.homeNode
                    : definition.homeNode;

                var facts = UIFactory.Label(card, "Facts",
                    $"MOVES   {definition.traversal}\n" +
                    $"STARTS  {home}\n" +
                    $"ENTERS  {route}",
                    15, TextAnchor.UpperLeft, definition.mapColor, wrap: false);
                UIFactory.Anchor(facts.rectTransform, UIFactory.TopRight,
                    new Vector2(-260f, -48f), new Vector2(240f, 74f));

                var counters = UIFactory.Label(card, "Counters", Counters(definition), 15,
                    TextAnchor.UpperLeft, new Color(0.46f, 0.44f, 0.40f), wrap: true);
                UIFactory.Anchor(counters.rectTransform, UIFactory.TopRight,
                    new Vector2(-20f, -48f), new Vector2(230f, 74f));

                y -= rowHeight + gap;
            }

            BackButton(screen);
            Show(frontEnd: true);
        }

        /// <summary>The one line a player actually needs: what to do about it.</summary>
        private static string Counters(AnimatronicDefinition definition)
        {
            var lines = new System.Collections.Generic.List<string>(4);

            if (definition.respectsBarriers) lines.Add("· Doors hold it");
            else lines.Add("· Doors do not hold it");

            if (definition.lightAverse) lines.Add("· Light turns it back");
            if (definition.noiseAffinity > 0.25f) lines.Add("· Comes to running machinery");
            else if (definition.noiseAffinity < -0.25f) lines.Add("· Noise confuses it");

            if ((definition.traversal & TraversalMask.Swim) != 0) lines.Add("· Needs deep water");
            if ((definition.traversal & TraversalMask.Burrow) != 0) lines.Add("· Needs a dry sump");

            return string.Join("\n", lines);
        }

        // =====================================================================
        // Shared pieces
        // =====================================================================

        private static Image Scrim(RectTransform parent, float alpha)
        {
            var scrim = UIFactory.Panel(parent, "Scrim", new Color(0.008f, 0.010f, 0.013f, alpha));
            UIFactory.Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;
            return scrim;
        }

        /// <summary>
        /// Four soft edge bands. Not a real vignette — a shader would be the right tool
        /// — but over a live camera view it does the job of pulling the eye inward, and
        /// it costs four transparent quads instead of a fullscreen pass.
        /// </summary>
        private static void Vignette(RectTransform parent)
        {
            var edge = new Color(0.004f, 0.005f, 0.007f, 0.55f);

            var top = UIFactory.Panel(parent, "EdgeTop", edge);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.anchorMax = new Vector2(1f, 1f);
            top.rectTransform.pivot = new Vector2(0.5f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, 220f);

            var bottom = UIFactory.Panel(parent, "EdgeBottom", edge);
            bottom.rectTransform.anchorMin = new Vector2(0f, 0f);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.pivot = new Vector2(0.5f, 0f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, 220f);
        }

        private static void Heading(RectTransform parent, string title, string subtitle,
            Color? color = null)
        {
            var heading = UIFactory.Label(parent, "Title", title, 58, TextAnchor.UpperCenter,
                color ?? UIFactory.Ink);
            UIFactory.Anchor(heading.rectTransform, UIFactory.TopCentre,
                new Vector2(0f, -70f), new Vector2(1400f, 70f));

            if (string.IsNullOrEmpty(subtitle)) return;

            var sub = UIFactory.Label(parent, "Subtitle", subtitle, 19, TextAnchor.UpperCenter,
                new Color(0.52f, 0.48f, 0.42f), wrap: true);
            UIFactory.Anchor(sub.rectTransform, UIFactory.TopCentre,
                new Vector2(0f, -136f), new Vector2(1100f, 52f));
        }

        private static void SectionLabel(RectTransform parent, string text, Vector2 position, float width)
        {
            var label = UIFactory.Label(parent, "Section_" + text, text, 15, TextAnchor.LowerLeft,
                new Color(0.45f, 0.42f, 0.38f));
            UIFactory.Anchor(label.rectTransform, UIFactory.Centre, position, new Vector2(width, 22f));

            var rule = UIFactory.Rule(parent, "Rule_" + text, width);
            UIFactory.Anchor(rule.rectTransform, UIFactory.Centre,
                position + new Vector2(0f, -12f), new Vector2(width, 1.5f));
        }

        /// <summary>A vertical run of buttons. The returned object tracks its own cursor.</summary>
        private RectTransform Column(RectTransform parent, Vector2 origin, Vector2 anchor)
        {
            var column = new GameObject("Column", typeof(RectTransform));
            column.transform.SetParent(parent, worldPositionStays: false);
            column.layer = parent.gameObject.layer;

            var rect = (RectTransform)column.transform;
            UIFactory.Anchor(rect, anchor, origin, new Vector2(ColumnWidth, 0f));
            return rect;
        }

        private void AddButton(RectTransform column, string label, System.Action onClick)
        {
            float y = -column.childCount * (ButtonHeight + ButtonGap);

            var button = UIFactory.TextButton(column, "Btn_" + label, label,
                new Vector2(ColumnWidth, ButtonHeight), onClick, 24);

            UIFactory.Anchor((RectTransform)button.transform, UIFactory.TopLeft,
                new Vector2(0f, y), new Vector2(ColumnWidth, ButtonHeight));
        }

        private void AddSlider(RectTransform parent, string id, string caption, Vector2 position,
            float width, float height, float value, float min, float max,
            System.Action<float> onChanged, System.Func<float, string> format = null)
        {
            var host = new GameObject("Slider_" + id, typeof(RectTransform));
            host.transform.SetParent(parent, worldPositionStays: false);
            host.layer = parent.gameObject.layer;
            UIFactory.Anchor((RectTransform)host.transform, UIFactory.Centre, position, new Vector2(width, height));

            UIFactory.ValueSlider(host.transform, "Row", caption, new Vector2(width, height),
                value, min, max, onChanged, format);
        }

        private void AddToggle(RectTransform parent, string id, string caption, Vector2 position,
            float width, float height, bool value, System.Action<bool> onChanged, string note = "")
        {
            var host = new GameObject("Toggle_" + id, typeof(RectTransform));
            host.transform.SetParent(parent, worldPositionStays: false);
            host.layer = parent.gameObject.layer;
            UIFactory.Anchor((RectTransform)host.transform, UIFactory.Centre, position, new Vector2(width, height));

            UIFactory.ToggleRow(host.transform, "Row", caption, new Vector2(width, height),
                value, onChanged, note);
        }

        private void BackButton(RectTransform parent)
        {
            var button = UIFactory.TextButton(parent, "Back", "BACK",
                new Vector2(220f, 48f), Back, 22);
            UIFactory.Anchor((RectTransform)button.transform, UIFactory.BottomRight,
                new Vector2(-150f, 60f), new Vector2(220f, 48f));
        }

        private static void Footer(RectTransform parent)
        {
            var footer = UIFactory.Label(parent, "Footer",
                $"v{Application.version}   ·   An original work. Not affiliated with, endorsed by, " +
                "or containing any assets from any other game.",
                14, TextAnchor.LowerLeft, new Color(0.34f, 0.32f, 0.30f));
            UIFactory.Anchor(footer.rectTransform, UIFactory.BottomLeft,
                new Vector2(150f, 40f), new Vector2(1200f, 20f));
        }
    }
}
