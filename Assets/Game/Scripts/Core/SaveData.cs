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
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string createdUtc = string.Empty;
        public string lastPlayedUtc = string.Empty;

        public int highestNightUnlocked = 1;
        public bool nightSixUnlocked;
        public bool customNightUnlocked;
        public int totalDeaths;
        public float totalPlaytimeSeconds;

        public List<NightRecord> nightRecords = new List<NightRecord>();
        public List<AiLevelEntry> customNightLevels = new List<AiLevelEntry>();
        public SettingsData settings = new SettingsData();

        public NightRecord GetOrCreateRecord(int night)
        {
            for (int i = 0; i < nightRecords.Count; i++)
                if (nightRecords[i].night == night) return nightRecords[i];

            var record = new NightRecord { night = night };
            nightRecords.Add(record);
            return record;
        }
    }

    [Serializable]
    public class NightRecord
    {
        public int night;
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
