using NUnit.Framework;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Tests
{
    /// <summary>
    /// Tests for the facility simulation.
    ///
    /// These are worth having precisely because the systems are plain classes rather
    /// than MonoBehaviours: the whole resource model can be stepped here, with no
    /// scene, no frame loop and no Play Mode, which means a balance change can be
    /// checked in milliseconds instead of by playing a six-minute night.
    /// </summary>
    public class SimulationTests
    {
        private FacilityTuning _tuning;

        [SetUp]
        public void SetUp()
        {
            _tuning = ScriptableObject.CreateInstance<FacilityTuning>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_tuning);
            EventBus.Clear();
        }

        /// <summary>A consumer that draws whatever the test tells it to.</summary>
        private sealed class TestLoad : IPowerConsumer
        {
            public string PowerLabel { get; set; } = "test";
            public float LoadKilowatts { get; set; }
            public bool IsEssential { get; set; }
            public bool Powered { get; private set; } = true;

            public void SetPowered(bool powered) => Powered = powered;
        }

        // =====================================================================
        // Clock
        // =====================================================================

        [Test]
        public void Clock_StartsAtMidnightAndReachesSix()
        {
            var clock = new GameClock(secondsPerHour: 10f);
            int lastHour = -1;
            bool completed = false;

            clock.HourChanged += hour => lastHour = hour;
            clock.NightComplete += () => completed = true;

            Assert.AreEqual(0, clock.Hour, "A night starts at 12 AM.");
            Assert.AreEqual("12 AM", clock.DisplayHour);

            clock.Start();
            for (int i = 0; i < 700; i++) clock.Tick(0.1f);   // 70 seconds > 6 hours

            Assert.AreEqual(GameClock.EndHour, clock.Hour);
            Assert.AreEqual(6, lastHour, "The final HourChanged should report 6.");
            Assert.IsTrue(completed, "NightComplete must fire when the clock reaches six.");
            Assert.IsFalse(clock.IsRunning, "The clock stops itself at six.");
        }

        [Test]
        public void Clock_DoesNotAdvanceWhilePaused()
        {
            var clock = new GameClock(10f);
            clock.Start();
            clock.Tick(5f);

            float elapsed = clock.ElapsedSeconds;
            clock.Pause();
            clock.Tick(5f);

            Assert.AreEqual(elapsed, clock.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void Clock_DebugJumpFiresEveryInterveningHour()
        {
            var clock = new GameClock(10f);
            int changes = 0;
            clock.HourChanged += _ => changes++;

            clock.Start();
            clock.DebugSetHour(4);

            // Systems downstream of the clock assume they see every boundary; skipping
            // them would leave AI levels un-ramped.
            Assert.AreEqual(4, changes, "Jumping to 4 AM must fire 1, 2, 3 and 4.");
            Assert.AreEqual(4, clock.Hour);
        }

        // =====================================================================
        // Generator
        // =====================================================================

        [Test]
        public void Generator_BurnsFasterUnderLoad()
        {
            var idle = new Generator(_tuning);
            var loaded = new Generator(_tuning);

            idle.ResetForNight(100f, 0);
            loaded.ResetForNight(100f, 0);

            for (int i = 0; i < 60; i++)
            {
                idle.Tick(0.01f, 0.1f, 0f, 1f);
                loaded.Tick(0.01f, 0.1f, _tuning.generatorRatedKilowatts, 1f);
            }

            Assert.Less(loaded.FuelLitres, idle.FuelLitres,
                "A loaded set must burn more than an idling one.");
        }

        [Test]
        public void Generator_StopsWhenTheTankRunsDry()
        {
            var generator = new Generator(_tuning);
            generator.ResetForNight(0.5f, 0);

            string stopReason = null;
            generator.Stopped += reason => stopReason = reason;

            for (int i = 0; i < 200 && generator.IsSupplying; i++)
                generator.Tick(0.05f, 0.1f, _tuning.generatorRatedKilowatts, 1f);

            Assert.AreEqual(Generator.State.Dry, generator.CurrentState);
            Assert.IsFalse(generator.IsSupplying);
            Assert.IsNotNull(stopReason, "Stopping must report a reason for the alert strip.");
        }

        [Test]
        public void Generator_RefuelConsumesACanAndTakesTime()
        {
            var generator = new Generator(_tuning);
            generator.ResetForNight(10f, 2);

            Assert.IsTrue(generator.BeginRefuel());
            Assert.AreEqual(1, generator.SpareCans, "The can is spent when the pour starts.");
            Assert.IsTrue(generator.IsBusy, "Refuelling occupies the player.");

            // Half way through, nothing has arrived yet.
            generator.Tick(0f, _tuning.refuelSeconds * 0.5f, 0f, 1f);
            Assert.AreEqual(10f, generator.FuelLitres, 0.01f);

            generator.Tick(0f, _tuning.refuelSeconds * 0.6f, 0f, 1f);
            Assert.AreEqual(10f + _tuning.jerryCanLitres, generator.FuelLitres, 0.01f);
            Assert.IsFalse(generator.IsBusy);
        }

        [Test]
        public void Generator_RefusesToRefuelWithNoCans()
        {
            var generator = new Generator(_tuning);
            generator.ResetForNight(10f, 0);

            Assert.IsFalse(generator.BeginRefuel());
            Assert.IsFalse(generator.IsBusy);
        }

        // =====================================================================
        // Grid
        // =====================================================================

        [Test]
        public void Grid_TripsAfterSustainedOverload()
        {
            var grid = new PowerGrid(_tuning);
            grid.ResetForNight(100f, 0);

            var hog = new TestLoad { LoadKilowatts = _tuning.generatorRatedKilowatts * 2f };
            grid.Register(hog);

            string reason = null;
            grid.BreakerTripped += r => reason = r;

            // Just under the grace period: still holding.
            for (int i = 0; i < 30; i++) grid.Tick(0.001f, _tuning.overloadGraceSeconds / 40f, 1f);
            Assert.IsFalse(grid.BreakerOpen, "The breaker must tolerate a brief overload.");
            Assert.Greater(grid.OverloadProgress01, 0f, "The trip timer should be visibly running.");

            for (int i = 0; i < 30; i++) grid.Tick(0.001f, _tuning.overloadGraceSeconds / 40f, 1f);
            Assert.IsTrue(grid.BreakerOpen, "A sustained overload must open the breaker.");
            Assert.IsNotNull(reason);
        }

        [Test]
        public void Grid_TripDropsNonEssentialConsumersOnly()
        {
            var grid = new PowerGrid(_tuning);
            grid.ResetForNight(100f, 0);

            var door = new TestLoad { PowerLabel = "door", LoadKilowatts = 1.5f, IsEssential = false };
            var alarm = new TestLoad { PowerLabel = "alarm", LoadKilowatts = 0.1f, IsEssential = true };

            grid.Register(door);
            grid.Register(alarm);

            grid.TripBreaker("test");
            grid.Tick(0.001f, 0.016f, 1f);

            // This is the rule the whole tension of a trip rests on: the doors let go.
            Assert.IsFalse(door.Powered, "A breaker trip must release the blast doors.");
            Assert.IsTrue(alarm.Powered, "Essential circuits stay up on battery.");
            Assert.AreEqual(PowerState.Tripped, grid.State);
        }

        [Test]
        public void Grid_BreakerResetTakesTheFullHoldAndCanBeLost()
        {
            var grid = new PowerGrid(_tuning);
            grid.ResetForNight(100f, 0);
            grid.TripBreaker("test");

            Assert.IsTrue(grid.BeginBreakerReset());

            grid.Tick(0f, _tuning.breakerResetSeconds * 0.7f, 1f);
            Assert.IsTrue(grid.BreakerOpen, "The breaker is not back until the hold completes.");

            grid.CancelBreakerReset();
            Assert.AreEqual(0f, grid.ResetProgress01, 0.0001f, "Letting go must lose all progress.");

            grid.BeginBreakerReset();
            grid.Tick(0f, _tuning.breakerResetSeconds * 1.1f, 1f);
            Assert.IsFalse(grid.BreakerOpen);
        }

        [Test]
        public void Grid_FallsToBlackoutWhenTheBatteryIsFlat()
        {
            var grid = new PowerGrid(_tuning);
            grid.ResetForNight(0f, 0);      // no fuel at all

            for (int i = 0; i < 400; i++) grid.Tick(_tuning.batteryHours / 100f, 0.016f, 1f);

            Assert.AreEqual(PowerState.Blackout, grid.State);
            Assert.AreEqual(0f, grid.BatteryCharge01, 0.001f);
        }

        // =====================================================================
        // Ventilation
        // =====================================================================

        [Test]
        public void Ventilation_DecaysWithTheFanOffAndRecoversWithItOn()
        {
            var vent = new VentilationSystem(_tuning);
            vent.ResetForNight();
            vent.Mode = FanMode.Off;

            for (int i = 0; i < 50; i++) vent.Tick(0.01f, 0.1f, 1f);
            float fouled = vent.AirQuality01;
            Assert.Less(fouled, 1f, "Air must degrade with the fan off.");

            vent.Mode = FanMode.Purge;
            for (int i = 0; i < 50; i++) vent.Tick(0.01f, 0.1f, 1f);

            Assert.Greater(vent.AirQuality01, fouled, "Purge must recover the air.");
        }

        [Test]
        public void Ventilation_HallucinationPressureRisesBeforeTheCriticalThreshold()
        {
            var vent = new VentilationSystem(_tuning);
            vent.ResetForNight();

            vent.DebugSetAir(_tuning.hallucinationThreshold + 0.05f);
            Assert.AreEqual(0f, vent.HallucinationPressure01, 0.001f,
                "Above the threshold the player should see nothing.");

            vent.DebugSetAir((_tuning.hallucinationThreshold + _tuning.criticalAirThreshold) * 0.5f);
            float midway = vent.HallucinationPressure01;
            Assert.Greater(midway, 0f);
            Assert.Less(midway, 1f);

            vent.DebugSetAir(_tuning.criticalAirThreshold);
            Assert.AreEqual(1f, vent.HallucinationPressure01, 0.02f);
        }

        [Test]
        public void Ventilation_SuffocatesOnlyAfterSustainedCriticalAir()
        {
            var vent = new VentilationSystem(_tuning);
            vent.ResetForNight();
            vent.Mode = FanMode.Off;
            vent.DebugSetAir(0f);

            bool suffocated = false;
            vent.Suffocated += () => suffocated = true;

            vent.Tick(0f, _tuning.suffocationSeconds * 0.5f, 1f);
            Assert.IsFalse(suffocated, "A brief dip must not be fatal.");

            vent.Tick(0f, _tuning.suffocationSeconds * 0.6f, 1f);
            Assert.IsTrue(suffocated);
        }

        // =====================================================================
        // Water — the game's central dial
        // =====================================================================

        [Test]
        public void Water_RisesWithThePumpOffAndFallsWithItOn()
        {
            var water = new WaterSystem(_tuning);
            water.ResetForNight();

            float start = water.Level01;
            for (int i = 0; i < 30; i++) water.Tick(0.02f, 0.1f, 0.5f, 1f);
            Assert.Greater(water.Level01, start, "The spring fills the gallery when nothing opposes it.");

            float high = water.Level01;
            water.PumpCommanded = true;
            for (int i = 0; i < 30; i++) water.Tick(0.02f, 0.1f, 0.5f, 1f);

            Assert.Less(water.Level01, high, "A healthy pump must beat the inflow.");
        }

        [Test]
        public void Water_InflowIncreasesTowardDawn()
        {
            var water = new WaterSystem(_tuning);
            water.ResetForNight();

            water.Tick(0.001f, 0.016f, nightProgress01: 0f, inflowScale: 1f);
            float midnight = water.CurrentInflowPerHour;

            water.Tick(0.001f, 0.016f, nightProgress01: 1f, inflowScale: 1f);
            float dawn = water.CurrentInflowPerHour;

            Assert.Greater(dawn, midnight * 1.5f,
                "The spring is supposed to wake up with everything else.");
        }

        [Test]
        public void Water_GatesAreMutuallyExclusive()
        {
            var water = new WaterSystem(_tuning);
            water.ResetForNight();

            // The premise of the whole design: there is no level at which both
            // Marlow's route and Echo's route are shut.
            water.DebugSetLevel(0.2f);
            Assert.IsTrue(water.SumpIsDry, "A drained sump lets Marlow dig.");
            Assert.IsFalse(water.ChannelIsSwimmable);

            water.DebugSetLevel(0.8f);
            Assert.IsFalse(water.SumpIsDry);
            Assert.IsTrue(water.ChannelIsSwimmable, "A deep channel lets Echo swim.");

            // And a middle band where neither can act, which is the one safe window.
            water.DebugSetLevel((GrottoSpringsLayout.SumpDiggable + GrottoSpringsLayout.ChannelSwimmable) * 0.5f);
            Assert.IsFalse(water.SumpIsDry);
            Assert.IsFalse(water.ChannelIsSwimmable);
        }

        [Test]
        public void Water_PumpCavitatesAndDegradesWhenRunDry()
        {
            var water = new WaterSystem(_tuning);
            water.ResetForNight();
            water.DebugSetLevel(0f);
            water.PumpCommanded = true;

            water.Tick(0.001f, 0.016f, 0.5f, 0f);   // no inflow, so it stays dry
            Assert.IsTrue(water.IsCavitating);

            for (int i = 0; i < 50; i++) water.Tick(0.01f, 0.1f, 0.5f, 0f);

            Assert.Less(water.PumpCondition01, 1f, "Running dry must wreck the impeller.");
        }

        [Test]
        public void Water_ReportsFloodingExactlyOnce()
        {
            var water = new WaterSystem(_tuning);
            water.ResetForNight();

            int floods = 0;
            water.Flooded += () => floods++;

            water.DebugSetLevel(0.99f);
            for (int i = 0; i < 50; i++) water.Tick(0.05f, 0.1f, 1f, 3f);

            Assert.AreEqual(1, floods, "The failure must fire once, not every frame after.");
        }
    }
}
