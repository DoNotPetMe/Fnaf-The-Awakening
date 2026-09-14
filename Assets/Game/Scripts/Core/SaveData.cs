using System;
using System.Collections.Generic;

namespace Grotto.Core
{
    /// <summary>
    /// Persisted profile. Shaped for <see cref="UnityEngine.JsonUtility"/>: public
    /// fields, concrete types, no dictionaries — arrays of pairs instead.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>Bump when the shape changes; <see cref="SaveSystem"/> migrates on load.</summary>
        /// <remarks>
        /// 2 — added <see cref="selectedSiteId"/> and per-site records. A version 1
        /// profile has neither; the migration fills them in from the flat night list
        /// and assumes every night on record was played at the grotto, which it was,
        /// because that was the only site that existed.
        /// </remarks>
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public string createdUtc = string.Empty;
        public string lastPlayedUtc = string.Empty;

        /// <summary>
        /// Which site the player last chose. Validated against the catalog on load —
        /// an unknown or still-locked id reverts rather than throwing.
        /// </summary>
        public string selectedSiteId = "grotto";

        public int highestNightUnlocked = 1;
        public bool nightSixUnlocked;
        public bool customNightUnlocked;
        public int totalDeaths;
        public float totalPlaytimeSeconds;

        public List<NightRecord> nightRecords = new List<NightRecord>();
        public List<AiLevelEntry> customNightLevels = new List<AiLevelEntry>();
        public SettingsData settings = new SettingsData();

        /// <summary>
        /// The record for one night at one site, created if it does not exist.
        ///
        /// Records are keyed by (site, night) rather than by night alone, so clearing
        /// night three at the hydro station does not overwrite the grotto's night
        /// three. A null or empty site falls back to whatever is currently selected,
        /// which keeps older call sites working unchanged.
        /// </summary>
        public NightRecord GetOrCreateRecord(int night, string site = null)
        {
            string key = Normalise(site);

            for (int i = 0; i < nightRecords.Count; i++)
                if (nightRecords[i].night == night && SameSite(nightRecords[i].siteId, key))
                    return nightRecords[i];

            var record = new NightRecord { night = night, siteId = key };
            nightRecords.Add(record);
            return record;
        }

        /// <summary>Highest night cleared at one site, or 0 if none.</summary>
        public int HighestNightCleared(string site = null)
        {
            string key = Normalise(site);

            int best = 0;
            for (int i = 0; i < nightRecords.Count; i++)
            {
                var record = nightRecords[i];
                if (record.completed && SameSite(record.siteId, key) && record.night > best)
                    best = record.night;
            }
            return best;
        }

        /// <summary>Nights cleared anywhere. This is what unlocks the other sites.</summary>
        public int TotalNightsCleared()
        {
            int cleared = 0;
            for (int i = 0; i < nightRecords.Count; i++)
                if (nightRecords[i].completed) cleared++;
            return cleared;
        }

        private string Normalise(string site)
            => string.IsNullOrWhiteSpace(site)
                ? (string.IsNullOrWhiteSpace(selectedSiteId) ? "grotto" : selectedSiteId)
                : site;

        private static bool SameSite(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a)) a = "grotto";
            if (string.IsNullOrWhiteSpace(b)) b = "grotto";
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    public class NightRecord
    {
        public int night;

        /// <summary>Which site this record belongs to. Empty means the original grotto.</summary>
        public string siteId = "grotto";

        public bool completed;
        public int attempts;
        public int deaths;
        /// <summary>Furthest point reached, in seconds of night time.</summary>
        public float bestSurvivalSeconds;
    }

    /// <summary>An animatronic's AI level (0-20) for custom night.</summary>
    [Serializable]
    public class AiLevelEntry
    {
        public string animatronicId;
        public int level;

        public AiLevelEntry() { }
        public AiLevelEntry(string id, int level) { animatronicId = id; this.level = level; }
    }

    [Serializable]
    public class SettingsData
    {
        public float masterVolume = 1f;
        public float sfxVolume = 1f;
        public float musicVolume = 0.8f;
        public float lookSensitivity = 1f;
        public bool invertY;

        /// <summary>Caps flashing, strobing and jumpscare contrast. Accessibility, not difficulty.</summary>
        public bool photosensitiveMode;

        /// <summary>Replaces the jumpscare sting with a quieter cue.</summary>
        public bool reducedJumpscareAudio;

        /// <summary>Subtitles for the pre-night phone briefings and radio chatter.</summary>
        public bool subtitles = true;

        public int qualityLevel = -1;   // -1 = leave Unity's default alone
        public int targetFrameRate = -1;
    }
}
