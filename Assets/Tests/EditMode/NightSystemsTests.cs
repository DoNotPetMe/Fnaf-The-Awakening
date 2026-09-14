using NUnit.Framework;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Tests
{
    /// <summary>
    /// The survey and the night events.
    ///
    /// Both are plain classes with no Unity dependencies beyond Mathf, which is the
    /// point of the plain-class simulation layer: a whole night's worth of pacing can
    /// be stepped here in milliseconds, with no scene, no clock and no frame rate.
    /// </summary>
    public class NightSystemsTests
    {
        private FacilityLayout _layout;
        private FacilityGraph _graph;
        private FacilityTuning _tuning;

        [SetUp]
        public void SetUp()
        {
            _layout = SiteCatalog.Build("grotto");
            _graph = _layout.BuildGraph();
            _tuning = ScriptableObject.CreateInstance<FacilityTuning>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_layout);
            Object.DestroyImmediate(_tuning);
        }

        private SurveySystem NewSurvey(int targets = 3)
        {
            var survey = new SurveySystem(_graph);
            survey.ConfigureSite(_layout.wiring);
            survey.ResetForNight(new RandomSource(4242), targets);
            return survey;
        }

        /// <summary>Steps the survey in sixtieths, which is what a frame actually is.</summary>
        private static void Step(SurveySystem survey, float seconds, int hour,
            NodeId watching, Generator generator, float condition = 1f)
        {
            const float dt = 1f / 60f;
            for (float t = 0f; t < seconds; t += dt)
                survey.Tick(dt, hour, watching, condition, generator);
        }

        // =====================================================================
        // Survey
        // =====================================================================

        [Test]
        public void Survey_NeverAsksForAnApproachOrTheStation()
        {
            var survey = NewSurvey(targets: 6);
            var generator = new Generator(_tuning);
            generator.ResetForNight(100f, 0);

            var forbidden = new[]
            {
                _graph.StationNode.Key,
                _layout.wiring.northApproach,
                _layout.wiring.southApproach,
                _layout.wiring.sump,
                _layout.wiring.chase
            };

            // Walk the whole night, filing everything it asks for.
            for (int hour = 0; hour <= 6; hour++)
            {
                Step(survey, 0.1f, hour, NodeId.None, generator);

                for (int i = 0; i < 3 && survey.Target.IsValid; i++)
                {
                    CollectionAssert.DoesNotContain(forbidden, survey.Target.Key,
                        "A reading you can file while already watching the door you need to " +
                        "watch is not a decision.");

                    survey.DebugFile(generator);
                }
            }
        }

        [Test]
        public void Survey_FilesAfterTheDwellAndReleasesFuel()
        {
            var survey = NewSurvey();
            var generator = new Generator(_tuning);
            generator.ResetForNight(100f, 0);

            Step(survey, 0.1f, hour: 0, watching: NodeId.None, generator);
            var target = survey.Target;
            Assert.IsTrue(target.IsValid);

            float before = generator.FuelLitres;

            // Just short of the dwell: nothing yet.
            Step(survey, SurveySystem.DwellSeconds - 0.5f, 0, target, generator);
            Assert.AreEqual(0, survey.Filed);
            Assert.AreEqual(before, generator.FuelLitres, 0.001f);
            Assert.IsTrue(survey.IsRecording);

            // Over the line.
            Step(survey, 1f, 0, target, generator);
            Assert.AreEqual(1, survey.Filed);
            Assert.Greater(generator.FuelLitres, before);
            Assert.AreEqual(SurveySystem.FuelPerReading, survey.FuelReleased, 0.01f);
        }

        [Test]
        public void Survey_ProgressDecaysRatherThanResetsWhenYouLookAway()
        {
            var survey = NewSurvey();
            var generator = new Generator(_tuning);
            generator.ResetForNight(100f, 0);

            Step(survey, 0.1f, 0, NodeId.None, generator);
            var target = survey.Target;

            Step(survey, 3f, 0, target, generator);
            float held = survey.Progress01;
            Assert.Greater(held, 0.5f);

            // A one-second glance at a door should cost some of it, not all of it.
            Step(survey, 1f, 0, NodeId.None, generator);

            Assert.Less(survey.Progress01, held);
            Assert.Greater(survey.Progress01, 0f,
                "Resetting the dwell punishes exactly the door-checking the rest of the " +
                "game spends five nights teaching.");
        }

        [Test]
        public void Survey_ASnowyFeedFilesMoreSlowly()
        {
            var generator = new Generator(_tuning);
            generator.ResetForNight(100f, 0);

            var clean = NewSurvey();
            Step(clean, 0.1f, 0, NodeId.None, generator);
            Step(clean, 3f, 0, clean.Target, generator, condition: 1f);

            var snowy = NewSurvey();
            Step(snowy, 0.1f, 0, NodeId.None, generator);
            Step(snowy, 3f, 0, snowy.Target, generator, condition: 0f);

            Assert.Greater(clean.Progress01, snowy.Progress01);
        }

        [Test]
        public void Survey_StopsAskingOnceTheNightIsDone()
        {
            var survey = NewSurvey(targets: 2);
            var generator = new Generator(_tuning);
            generator.ResetForNight(100f, 0);

            Step(survey, 0.1f, 0, NodeId.None, generator);

            survey.DebugFile(generator);
            Assert.IsTrue(survey.Target.IsValid, "One left, so it should ask again immediately.");

            survey.DebugFile(generator);
            Assert.AreEqual(2, survey.Filed);
            Assert.IsFalse(survey.Target.IsValid);

            // And a new hour does not restart it.
            Step(survey, 0.1f, 1, NodeId.None, generator);
            Assert.IsFalse(survey.Target.IsValid);
        }

        [Test]
        public void Survey_OverflowIsNotCredited()
        {
            var survey = NewSurvey();

            var generator = new Generator(_tuning);
            generator.ResetForNight(_tuning.fuelCapacityLitres, 0);

            Step(survey, 0.1f, 0, NodeId.None, generator);
            survey.DebugFile(generator);

            Assert.AreEqual(0f, survey.FuelReleased, 0.01f,
                "A full day tank takes the overflow onto the floor; reporting litres that " +
                "went nowhere would make the HUD lie about the one resource that matters.");
        }

        [Test]
        public void Survey_IsDeterministicForASeed()
        {
            var first = new SurveySystem(_graph);
            first.ConfigureSite(_layout.wiring);
            first.ResetForNight(new RandomSource(99), 4);

            var second = new SurveySystem(_graph);
            second.ConfigureSite(_layout.wiring);
            second.ResetForNight(new RandomSource(99), 4);

            var generator = new Generator(_tuning);
            generator.ResetForNight(400f, 0);

            for (int i = 0; i < 4; i++)
            {
                Step(first, 0.1f, i, NodeId.None, generator);
                Step(second, 0.1f, i, NodeId.None, generator);

                Assert.AreEqual(first.Target.Key, second.Target.Key,
                    "A seeded night must ask for the same rooms, or a bug report with a seed " +
                    "in it is worthless.");

                first.DebugFile(generator);
                second.DebugFile(generator);
            }
        }

        // =====================================================================
        // Night events
        // =====================================================================

        private static void Step(NightEvents events, float seconds, float progress01)
        {
            const float dt = 1f / 60f;
            for (float t = 0f; t < seconds; t += dt) events.Tick(dt, progress01);
        }

        [Test]
        public void Events_LeaveTheFirstStretchOfTheNightAlone()
        {
            var events = new NightEvents();
            events.ResetForNight(new RandomSource(7), night: 6);

            Step(events, 30f, progress01: 0.05f);

            Assert.AreEqual(NightEventKind.None, events.Active);
            Assert.AreEqual(NightEventKind.None, events.Warning,
                "A night that opens with a surge teaches the player nothing except that " +
                "the game is unfair.");
        }

        [Test]
        public void Events_WarnBeforeTheyLand()
        {
            var events = new NightEvents();
            events.ResetForNight(new RandomSource(7), night: 3);

            NightEventKind warned = NightEventKind.None;
            NightEventKind began = NightEventKind.None;

            events.Warned += kind => warned = kind;
            events.Began += kind => began = kind;

            // Far enough through the night that something is due.
            Step(events, 1f, progress01: 0.6f);

            Assert.AreNotEqual(NightEventKind.None, warned);
            Assert.AreEqual(NightEventKind.None, began,
                "The warning has to arrive before the effect, or it is not a warning.");

            Step(events, NightEvents.WarningSeconds + 0.5f, 0.6f);
            Assert.AreEqual(warned, began);
            Assert.AreEqual(began, events.Active);
        }

        [Test]
        public void Events_EndOnTheirOwn()
        {
            var events = new NightEvents();
            events.Force(NightEventKind.Brownout);

            Assert.AreEqual(NightEventKind.Brownout, events.Active);
            Assert.Less(events.PowerCapacityMultiplier, 1f);

            Step(events, 120f, 0.6f);

            Assert.AreEqual(NightEventKind.None, events.Active);
            Assert.AreEqual(1f, events.PowerCapacityMultiplier, 0.001f,
                "A disturbance that never lifts is not an event, it is a difficulty change.");
        }

        [Test]
        public void Events_EachKindTouchesExactlyOneResource()
        {
            var events = new NightEvents();

            events.Force(NightEventKind.SpringSurge);
            Assert.Greater(events.WaterInflowMultiplier, 1f);
            Assert.AreEqual(1f, events.AirDecayMultiplier, 0.001f);
            Assert.AreEqual(1f, events.PowerCapacityMultiplier, 0.001f);

            events.Force(NightEventKind.VentFault);
            Assert.Greater(events.AirDecayMultiplier, 1f);
            Assert.AreEqual(1f, events.WaterInflowMultiplier, 0.001f);

            events.Force(NightEventKind.FeedDecay);
            Assert.Greater(events.FeedWearMultiplier, 1f);
            Assert.AreEqual(1f, events.AirDecayMultiplier, 0.001f);

            events.Force(NightEventKind.None);
            Assert.AreEqual(1f, events.WaterInflowMultiplier, 0.001f);
            Assert.AreEqual(1f, events.AirDecayMultiplier, 0.001f);
            Assert.AreEqual(1f, events.PowerCapacityMultiplier, 0.001f);
            Assert.AreEqual(1f, events.FeedWearMultiplier, 0.001f);
        }

        [Test]
        public void Events_LaterNightsGetMoreOfThem()
        {
            int Count(int night)
            {
                var events = new NightEvents();
                events.ResetForNight(new RandomSource(11), night);

                int seen = 0;
                events.Began += _ => seen++;

                // Walk the night in slices long enough for each event to warn, run and
                // clear before the next slot comes up — the longest is eighty seconds.
                for (float p = 0f; p <= 1f; p += 0.01f) Step(events, 6f, p);
                return seen;
            }

            Assert.Greater(Count(6), Count(1),
                "Night six should lean on the player harder than night one.");
        }

        [Test]
        public void Events_AreDeterministicForASeed()
        {
            string Trace(int seed)
            {
                var events = new NightEvents();
                events.ResetForNight(new RandomSource(seed), night: 5);

                var log = new System.Text.StringBuilder();
                events.Began += kind => log.Append(kind).Append(' ');

                for (float p = 0f; p <= 1f; p += 0.01f) Step(events, 6f, p);
                return log.ToString();
            }

            Assert.AreEqual(Trace(31337), Trace(31337));
            Assert.IsNotEmpty(Trace(31337));
        }

        [Test]
        public void Events_CanBeTurnedOffEntirely()
        {
            var events = new NightEvents();
            events.ResetForNight(new RandomSource(7), night: 6, intensity01: 0f);

            for (float p = 0f; p <= 1f; p += 0.05f) Step(events, 5f, p);

            Assert.AreEqual(NightEventKind.None, events.Active);
        }

        [Test]
        public void Events_EveryKindHasSomethingToSay()
        {
            foreach (NightEventKind kind in System.Enum.GetValues(typeof(NightEventKind)))
            {
                if (kind == NightEventKind.None) continue;

                Assert.IsNotEmpty(NightEvents.Describe(kind), $"{kind} has no banner text.");
                Assert.IsNotEmpty(NightEvents.Warn(kind), $"{kind} has no warning text.");
            }
        }

        // =====================================================================
        // Difficulty
        // =====================================================================

        [Test]
        public void Difficulty_MovesEveryResourceInTheSameDirection()
        {
            var easy = new SettingsData { difficulty = DifficultyPreset.Survey };
            var normal = new SettingsData { difficulty = DifficultyPreset.Standard };
            var hard = new SettingsData { difficulty = DifficultyPreset.Reclamation };

            Assert.AreEqual(1f, normal.WaterScale, 0.001f);
            Assert.AreEqual(1f, normal.AirScale, 0.001f);
            Assert.AreEqual(1f, normal.FuelScale, 0.001f);
            Assert.AreEqual(1f, normal.EventScale, 0.001f);

            foreach (var (name, e, h) in new[]
                     {
                         ("water", easy.WaterScale, hard.WaterScale),
                         ("air", easy.AirScale, hard.AirScale),
                         ("fuel", easy.FuelScale, hard.FuelScale),
                         ("events", easy.EventScale, hard.EventScale)
                     })
            {
                Assert.Less(e, 1f, $"Survey should be gentler on {name}.");
                Assert.Greater(h, 1f, $"Reclamation should be harsher on {name}.");
            }
        }

        [Test]
        public void Difficulty_EveryPresetIsDescribed()
        {
            foreach (DifficultyPreset preset in System.Enum.GetValues(typeof(DifficultyPreset)))
                Assert.IsNotEmpty(SettingsData.DescribeDifficulty(preset));
        }
    }
}
