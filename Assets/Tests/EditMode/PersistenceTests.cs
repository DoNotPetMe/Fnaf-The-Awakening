using System.IO;
using NUnit.Framework;
using Grotto.Core;

namespace Grotto.Tests
{
    /// <summary>
    /// Tests for the profile.
    ///
    /// Save systems fail quietly and lose player progress, so the two behaviours worth
    /// asserting are that a round trip survives, and that a corrupt file is preserved
    /// rather than destroyed.
    /// </summary>
    public class PersistenceTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "grotto-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void Save_RoundTripsProgress()
        {
            var first = new SaveSystem(_directory);
            first.Load();
            first.Data.highestNightUnlocked = 4;
            first.Data.totalDeaths = 11;
            first.Data.settings.photosensitiveMode = true;
            first.Data.GetOrCreateRecord(3).completed = true;
            Assert.IsTrue(first.Save());

            var second = new SaveSystem(_directory);
            var loaded = second.Load();

            Assert.AreEqual(4, loaded.highestNightUnlocked);
            Assert.AreEqual(11, loaded.totalDeaths);
            Assert.IsTrue(loaded.settings.photosensitiveMode);
            Assert.IsTrue(loaded.GetOrCreateRecord(3).completed);
        }

        [Test]
        public void Save_MissingProfileStartsFresh()
        {
            var save = new SaveSystem(_directory);
            var data = save.Load();

            Assert.IsNotNull(data);
            Assert.AreEqual(1, data.highestNightUnlocked);
            Assert.AreEqual(SaveData.CurrentVersion, data.version);
        }

        [Test]
        public void Save_CorruptProfileIsQuarantinedNotDeleted()
        {
            var save = new SaveSystem(_directory);
            File.WriteAllText(save.FilePath, "{ this is not json");

            var data = save.Load();

            Assert.IsNotNull(data, "A corrupt profile must still yield a usable one.");
            Assert.AreEqual(1, data.highestNightUnlocked);

            var quarantined = Directory.GetFiles(_directory, "*.corrupt-*");
            Assert.IsNotEmpty(quarantined,
                "The bad file is kept so a player's progress can be recovered by hand.");
        }

        [Test]
        public void Save_SurvivingANightUnlocksTheNext()
        {
            var save = new SaveSystem(_directory);
            save.Load();

            save.RecordNightAttempt(1);
            save.RecordNightResult(1, NightOutcome.Survived, survivedSeconds: 360f);

            Assert.AreEqual(2, save.Data.highestNightUnlocked);
            Assert.IsTrue(save.Data.GetOrCreateRecord(1).completed);
            Assert.AreEqual(1, save.Data.GetOrCreateRecord(1).attempts);
            Assert.AreEqual(0, save.Data.GetOrCreateRecord(1).deaths);
        }

        [Test]
        public void Save_DyingRecordsTheAttemptWithoutUnlocking()
        {
            var save = new SaveSystem(_directory);
            save.Load();

            save.RecordNightAttempt(1);
            save.RecordNightResult(1, NightOutcome.Killed, survivedSeconds: 120f);

            Assert.AreEqual(1, save.Data.highestNightUnlocked, "Dying must not unlock anything.");
            Assert.AreEqual(1, save.Data.GetOrCreateRecord(1).deaths);
            Assert.AreEqual(120f, save.Data.GetOrCreateRecord(1).bestSurvivalSeconds, 0.01f);
        }

        [Test]
        public void Save_BestSurvivalOnlyImproves()
        {
            var save = new SaveSystem(_directory);
            save.Load();

            save.RecordNightResult(2, NightOutcome.Killed, 200f);
            save.RecordNightResult(2, NightOutcome.Killed, 90f);

            Assert.AreEqual(200f, save.Data.GetOrCreateRecord(2).bestSurvivalSeconds, 0.01f);
        }

        [Test]
        public void Save_AbortingIsNotADeath()
        {
            var save = new SaveSystem(_directory);
            save.Load();

            save.RecordNightResult(3, NightOutcome.Aborted, 45f);

            Assert.AreEqual(0, save.Data.GetOrCreateRecord(3).deaths,
                "Quitting out should not count against the player.");
            Assert.AreEqual(0, save.Data.totalDeaths);
        }

        [Test]
        public void Save_SixthNightUnlocksCustomNight()
        {
            var save = new SaveSystem(_directory);
            save.Load();

            save.RecordNightResult(5, NightOutcome.Survived, 400f);
            Assert.IsTrue(save.Data.nightSixUnlocked);
            Assert.IsFalse(save.Data.customNightUnlocked);

            save.RecordNightResult(6, NightOutcome.Survived, 410f);
            Assert.IsTrue(save.Data.customNightUnlocked);
        }

        [Test]
        public void Save_ResetClearsProgress()
        {
            var save = new SaveSystem(_directory);
            save.Load();
            save.Data.highestNightUnlocked = 6;
            save.Save();

            save.ResetProfile();

            Assert.AreEqual(1, save.Data.highestNightUnlocked);
            Assert.IsEmpty(save.Data.nightRecords);
        }
    }
}
