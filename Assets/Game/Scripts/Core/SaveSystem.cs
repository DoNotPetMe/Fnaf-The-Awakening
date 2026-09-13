using System;
using System.IO;
using UnityEngine;

namespace Grotto.Core
{
    /// <summary>
    /// JSON profile storage.
    ///
    /// Writes are atomic — a temp file is written, flushed and then swapped over the
    /// real one — so an alt-F4 mid-save leaves the previous profile intact rather
    /// than a half-written file. A corrupt profile is moved aside, not deleted, so a
    /// player's progress can still be recovered by hand.
    /// </summary>
    public sealed class SaveSystem
    {
        private const string FileName = "profile.json";
        private const string BackupSuffix = ".bak";

        private readonly string _directory;
        private SaveData _data;

        public string FilePath => Path.Combine(_directory, FileName);

        /// <summary>The in-memory profile. Never null after construction.</summary>
        public SaveData Data => _data ?? (_data = CreateDefault());

        public SaveSystem(string directoryOverride = null)
        {
            _directory = directoryOverride ?? Application.persistentDataPath;
        }

        private static SaveData CreateDefault() => new SaveData
        {
            createdUtc = DateTime.UtcNow.ToString("o"),
            lastPlayedUtc = DateTime.UtcNow.ToString("o")
        };

        public SaveData Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    GLog.Info(LogChannel.Save, "No profile found; starting a fresh one.");
                    _data = CreateDefault();
                    return _data;
                }

                string json = File.ReadAllText(FilePath);
                var loaded = JsonUtility.FromJson<SaveData>(json);

                if (loaded == null)
                    throw new InvalidDataException("Profile deserialised to null.");

                Migrate(loaded);
                _data = loaded;
                GLog.Info(LogChannel.Save, $"Loaded profile (version {loaded.version}) from {FilePath}");
                return _data;
            }
            catch (Exception ex)
            {
                GLog.Error(LogChannel.Save, $"Profile at '{FilePath}' could not be read: {ex.Message}");
                QuarantineCorruptFile();
                _data = CreateDefault();
                return _data;
            }
        }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(_directory);

                Data.version = SaveData.CurrentVersion;
                Data.lastPlayedUtc = DateTime.UtcNow.ToString("o");

                string json = JsonUtility.ToJson(Data, prettyPrint: true);
                string temp = FilePath + ".tmp";

                File.WriteAllText(temp, json);

                // Replace() keeps a backup and is atomic where the platform supports it.
                if (File.Exists(FilePath))
                {
                    File.Replace(temp, FilePath, FilePath + BackupSuffix);
                }
                else
                {
                    File.Move(temp, FilePath);
                }

                GLog.Verbose(LogChannel.Save, $"Saved profile to {FilePath}");
                return true;
            }
            catch (Exception ex)
            {
                GLog.Error(LogChannel.Save, $"Failed to save profile: {ex.Message}");
                return false;
            }
        }

        /// <summary>Wipes progress. Used by the main menu and by <c>save.reset</c>.</summary>
        public void ResetProfile()
        {
            _data = CreateDefault();
            Save();
            GLog.Info(LogChannel.Save, "Profile reset.");
        }

        public void RecordNightAttempt(int night)
        {
            var record = Data.GetOrCreateRecord(night);
            record.attempts++;
        }

        public void RecordNightResult(int night, NightOutcome outcome, float survivedSeconds)
        {
            var record = Data.GetOrCreateRecord(night);
            record.bestSurvivalSeconds = Mathf.Max(record.bestSurvivalSeconds, survivedSeconds);

            if (outcome == NightOutcome.Survived)
            {
                record.completed = true;
                Data.highestNightUnlocked = Mathf.Max(Data.highestNightUnlocked, night + 1);
                if (night >= 5) Data.nightSixUnlocked = true;
                if (night >= 6) Data.customNightUnlocked = true;
            }
            else if (outcome != NightOutcome.Aborted)
            {
                record.deaths++;
                Data.totalDeaths++;
            }

            Save();
        }

        private static void Migrate(SaveData data)
        {
            if (data.version == SaveData.CurrentVersion) return;

            GLog.Info(LogChannel.Save, $"Migrating profile from version {data.version} to {SaveData.CurrentVersion}.");

            // Migration steps go here as the format evolves, e.g.
            //   if (data.version < 2) { ...fill new fields... }

            data.version = SaveData.CurrentVersion;
        }

        private void QuarantineCorruptFile()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string quarantine = FilePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                File.Move(FilePath, quarantine);
                GLog.Warn(LogChannel.Save, $"Corrupt profile preserved at '{quarantine}'.");
            }
            catch (Exception ex)
            {
                GLog.Error(LogChannel.Save, $"Could not quarantine the corrupt profile: {ex.Message}");
            }
        }
    }
}
