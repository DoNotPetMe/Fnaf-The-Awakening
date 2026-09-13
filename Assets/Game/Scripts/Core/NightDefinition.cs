using System;
using System.Collections.Generic;
using UnityEngine;

namespace Grotto.Core
{
    /// <summary>
    /// Everything that makes one night different from another.
    ///
    /// Animatronics are addressed by string id rather than by object reference so
    /// that <c>Grotto.Core</c> keeps no dependency on <c>Grotto.AI</c>, and so that a
    /// designer can author a night before the character it references exists.
    /// </summary>
    [CreateAssetMenu(menuName = "Grotto/Night Definition", fileName = "Night_00")]
    public sealed class NightDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Min(1)] public int night = 1;
        public string displayName = "Night 1";

        [TextArea(4, 10)]
        [Tooltip("Shown during the briefing phase; also drives the subtitle track.")]
        public string briefing =
            "Reclamation log, night one. Cameras are up, the pump's holding. " +
            "Do not go past the blast doors.";

        [Header("Pacing")]
        [Tooltip("Real seconds per in-game hour. 60 gives a six minute night.")]
        [Range(10f, 240f)] public float secondsPerHour = 60f;

        [Tooltip("Seconds of briefing before the clock starts. 0 skips straight in.")]
        [Range(0f, 60f)] public float briefingSeconds = 8f;

        [Header("Cast")]
        [Tooltip("AI level 0-20 per animatronic. 0 means the character stays dormant all night.")]
        public List<AiLevelEntry> aiLevels = new List<AiLevelEntry>();

        [Tooltip("Extra AI level granted at each hour boundary, indexed 1AM..5AM. Leave empty for none.")]
        public List<AiLevelRamp> hourlyRamps = new List<AiLevelRamp>();

        [Header("Environment modifiers")]
        [Tooltip("Multiplies how fast flood water enters the lower gallery.")]
        [Range(0.25f, 3f)] public float waterInflowScale = 1f;

        [Tooltip("Multiplies how fast air quality decays with the fan off.")]
        [Range(0.25f, 3f)] public float airDecayScale = 1f;

        [Tooltip("Multiplies generator fuel burn.")]
        [Range(0.25f, 3f)] public float fuelBurnScale = 1f;

        [Tooltip("Litres of diesel in the day tank at midnight.")]
        [Range(10f, 400f)] public float startingFuelLitres = 120f;

        [Tooltip("Spare jerry cans available from the station this night.")]
        [Range(0, 6)] public int spareFuelCans = 2;

        /// <summary>AI level for an animatronic, including any ramp already earned.</summary>
        public int GetAiLevel(string animatronicId, int hour)
        {
            int level = 0;
            for (int i = 0; i < aiLevels.Count; i++)
            {
                if (string.Equals(aiLevels[i].animatronicId, animatronicId, StringComparison.OrdinalIgnoreCase))
                {
                    level = aiLevels[i].level;
                    break;
                }
            }

            // A level of 0 means "not in this night at all" — ramps must not wake them.
            if (level <= 0) return 0;

            for (int i = 0; i < hourlyRamps.Count; i++)
            {
                var ramp = hourlyRamps[i];
                if (!string.Equals(ramp.animatronicId, animatronicId, StringComparison.OrdinalIgnoreCase)) continue;
                if (hour >= ramp.fromHour) level += ramp.levelBonus;
            }

            return Mathf.Clamp(level, 0, 20);
        }

        /// <summary>Ids with a non-zero level, i.e. who actually shows up tonight.</summary>
        public IEnumerable<string> ActiveAnimatronicIds()
        {
            for (int i = 0; i < aiLevels.Count; i++)
                if (aiLevels[i].level > 0) yield return aiLevels[i].animatronicId;
        }

        private void OnValidate()
        {
            for (int i = 0; i < aiLevels.Count; i++)
                aiLevels[i].level = Mathf.Clamp(aiLevels[i].level, 0, 20);

            if (string.IsNullOrWhiteSpace(displayName)) displayName = "Night " + night;
        }
    }

    [Serializable]
    public struct AiLevelRamp
    {
        public string animatronicId;
        [Range(1, 5)] public int fromHour;
        [Range(0, 10)] public int levelBonus;
    }
}
