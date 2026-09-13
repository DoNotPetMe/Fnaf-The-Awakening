using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;
using Grotto.Player;
using Grotto.Rendering;
using Grotto.UI;

namespace Grotto.DevTools
{
    /// <summary>
    /// The gameplay console commands.
    ///
    /// The design goal for this set is that any state the game can reach on its own,
    /// a developer can reach in one line. Debugging a horror game is otherwise
    /// miserable: the interesting failures happen at 5 AM on night five with the
    /// water at a particular level and two characters in specific rooms, and getting
    /// there by playing takes six minutes per attempt.
    ///
    /// With <c>night.seed</c>, <c>night.hour</c>, <c>env.water</c> and <c>ai.move</c>,
    /// it takes four lines and reproduces exactly.
    /// </summary>
    public static class DebugCommands
    {
        // ---------------------------------------------------------------------
        // Service access
        // ---------------------------------------------------------------------

        private static FacilityRuntime Facility =>
            ServiceLocator.TryGet(out FacilityRuntime f) ? f : throw new System.InvalidOperationException(
                "no facility in this scene");

        private static NightController Night =>
            ServiceLocator.TryGet(out NightController n) ? n : throw new System.InvalidOperationException(
                "no night controller in this scene");

        private static AIDirector Ai =>
            ServiceLocator.TryGet(out AIDirector a) ? a : throw new System.InvalidOperationException(
                "no AI director in this scene");

        private static AnimatronicController Character(string id)
        {
            var controller = Ai.Find(id);
            if (controller == null)
                throw new System.ArgumentException($"no animatronic '{id}'. Try 'ai.list'.");
            return controller;
        }

        private static NodeId Node(string key)
        {
            var node = new NodeId(key);
            if (!Facility.Graph.Contains(node))
                throw new System.ArgumentException($"no node '{key}'. Try 'map.nodes'.");
            return node;
        }

        // =====================================================================
        // Night
        // =====================================================================

        [DevCommand("night.start", Category = "night",
            Help = "Starts a night. Optional seed makes it reproducible.",
            Usage = "night.start <1-7> [seed]")]
        private static string NightStart(CommandArgs args)
        {
            int night = args.Int(0);
            int? seed = args.Count > 1 ? args.Int(1) : (int?)null;

            Night.StartNight(night, seed);
            return $"Started night {night}" + (seed.HasValue ? $" with seed {seed.Value}." : ".");
        }

        [DevCommand("night.restart", Category = "night", Help = "Restarts the current night with the same seed.")]
        private static string NightRestart(CommandArgs args)
        {
            int night = Night.CurrentNight;
            int seed = Night.Rng?.Seed ?? 0;
            Night.StartNight(night, seed);
            return $"Restarted night {night} (seed {seed}).";
        }

        [DevCommand("night.hour", Category = "night",
            Help = "Jumps the clock to an hour, firing every hour change on the way.",
            Usage = "night.hour <0-6>")]
        private static string NightHour(CommandArgs args)
        {
            int hour = args.Int(0);
            Night.Clock.DebugSetHour(hour);
            return $"Clock at {Night.Clock.DisplayHour}.";
        }

        [DevCommand("night.time", Category = "night",
            Help = "Scales the night clock. 1 is normal, 10 runs a night in 36 seconds.",
            Usage = "night.time <scale>")]
        private static string NightTime(CommandArgs args)
        {
            DebugFlags.ClockScale = Mathf.Clamp(args.Float(0), 0f, 60f);
            DebugFlags.NotifyChanged();
            return $"Clock scale {DebugFlags.ClockScale:0.##}x.";
        }

        [DevCommand("night.seed", Category = "night",
            Help = "Forces the seed for every subsequent night. No argument clears it.",
            Usage = "night.seed [value]")]
        private static string NightSeed(CommandArgs args)
        {
            if (args.Count == 0)
            {
                DebugFlags.ForcedSeed = null;
                DebugFlags.NotifyChanged();
                return "Seed released; nights will be random again.";
            }

            DebugFlags.ForcedSeed = args.Int(0);
            DebugFlags.NotifyChanged();
            return $"Seed forced to {DebugFlags.ForcedSeed.Value}. Run 'night.restart' to apply.";
        }

        [DevCommand("night.end", Category = "night",
            Help = "Ends the night. Outcome defaults to survived.",
            Usage = "night.end [survived|killed|flooded|suffocated|aborted]")]
        private static string NightEnd(CommandArgs args)
        {
            string requested = args.String(0, "survived");
            if (!System.Enum.TryParse(requested, ignoreCase: true, out NightOutcome outcome))
                return $"Unknown outcome '{requested}'.";

            Night.RequestOutcome(outcome);
            return $"Ending the night as {outcome}.";
        }

        [DevCommand("night.info", Category = "night", Help = "Prints the current night's state.")]
        private static string NightInfo(CommandArgs args)
        {
            var night = Night;
            var builder = new StringBuilder(256);

            builder.Append($"Night {night.CurrentNight} — {night.CurrentPhase}\n");
            builder.Append($"  clock    {night.Clock.DisplayHour} ({night.Clock.NightProgress01 * 100f:0}% of the night)\n");
            builder.Append($"  seed     {night.Rng?.Seed ?? 0} ({night.Rng?.DrawCount ?? 0} draws)\n");
            builder.Append($"  scale    {DebugFlags.ClockScale:0.##}x, {night.Clock.SecondsPerHour:0}s per hour\n");

            if (night.CurrentDefinition != null)
            {
                builder.Append($"  water    x{night.CurrentDefinition.waterInflowScale:0.00} inflow\n");
                builder.Append($"  air      x{night.CurrentDefinition.airDecayScale:0.00} decay\n");
                builder.Append($"  fuel     {night.CurrentDefinition.startingFuelLitres:0} L, " +
                               $"{night.CurrentDefinition.spareFuelCans} cans\n");
            }

            return builder.ToString();
        }

        // =====================================================================
        // AI
        // =====================================================================

        [DevCommand("ai.list", Category = "ai", Help = "Lists the cast with level, state and location.")]
        private static string AiList(CommandArgs args)
        {
            var builder = new StringBuilder(512);
            builder.Append("id        lvl  state       node       next   odds  note\n");

            foreach (var controller in Ai.Cast)
            {
                if (controller == null) continue;

                builder.Append(controller.Id.PadRight(10));
                builder.Append(controller.AiLevel.ToString().PadRight(5));
                builder.Append(controller.State.ToString().PadRight(12));

                string where = controller.IsInTransit
                    ? $"{controller.CurrentNode}→{controller.TransitTarget}"
                    : controller.CurrentNode.Key;
                builder.Append(where.PadRight(11));

                builder.Append($"{controller.NextRollIn,5:0.0}s");
                builder.Append($"{controller.CurrentRollChance01 * 100f,6:0}%");
                builder.Append("  ").Append(controller.BehaviourSummary);
                builder.Append('\n');
            }

            builder.Append($"\ndirector pressure {Ai.NightPressure:0.00}");
            if (Ai.AttackClaim != null) builder.Append($", attack claimed by {Ai.AttackClaim.Id}");

            return builder.ToString();
        }

        [DevCommand("ai.level", Category = "ai",
            Help = "Sets one animatronic's AI level.", Usage = "ai.level <id> <0-20>")]
        private static string AiLevel(CommandArgs args)
        {
            var controller = Character(args.String(0));
            int level = Mathf.Clamp(args.Int(1), 0, 20);
            controller.SetLevel(level);
            return $"{controller.DisplayName} at level {level}.";
        }

        [DevCommand("ai.levels", Category = "ai",
            Help = "Sets every animatronic's level at once.", Usage = "ai.levels <0-20>")]
        private static string AiLevels(CommandArgs args)
        {
            int level = Mathf.Clamp(args.Int(0), 0, 20);
            int count = 0;

            foreach (var controller in Ai.Cast)
            {
                if (controller == null) continue;
                controller.SetLevel(level);
                count++;
            }

            return $"{count} character(s) at level {level}.";
        }

        [DevCommand("ai.move", Category = "ai",
            Help = "Teleports an animatronic to a node.", Usage = "ai.move <id> <node>")]
        private static string AiMove(CommandArgs args)
        {
            var controller = Character(args.String(0));
            var node = Node(args.String(1));
            controller.DebugTeleport(node);
            return $"{controller.DisplayName} is now at {node}.";
        }

        [DevCommand("ai.state", Category = "ai",
            Help = "Forces an animatronic into a state.",
            Usage = "ai.state <id> <dormant|roam|stalk|threshold|attack|retreat|stranded>")]
        private static string AiState(CommandArgs args)
        {
            var controller = Character(args.String(0));
            string requested = args.String(1);

            if (!System.Enum.TryParse(requested, ignoreCase: true, out AnimatronicState state))
                return $"Unknown state '{requested}'.";

            controller.DebugForceState(state);
            return $"{controller.DisplayName} forced to {state}.";
        }

        [DevCommand("ai.freeze", Category = "ai",
            Help = "Stops the cast moving and attacking.", Usage = "ai.freeze [on|off]")]
        private static string AiFreeze(CommandArgs args)
        {
            DebugFlags.FreezeAI = args.Bool(0, DebugFlags.FreezeAI);
            DebugFlags.NotifyChanged();
            return $"AI freeze {(DebugFlags.FreezeAI ? "ON" : "OFF")}.";
        }

        [DevCommand("ai.attack", Category = "ai",
            Help = "Moves an animatronic to a threshold and lets it strike.",
            Usage = "ai.attack <id>")]
        private static string AiAttack(CommandArgs args)
        {
            var controller = Character(args.String(0));
            var attackNodes = controller.Definition.attackNodes;

            if (attackNodes.Count == 0) return $"{controller.Id} has no attack nodes.";

            controller.DebugTeleport(new NodeId(attackNodes[0]));
            controller.DebugForceState(AnimatronicState.Attack);
            return $"{controller.DisplayName} attacking from {attackNodes[0]}.";
        }

        // =====================================================================
        // Power
        // =====================================================================

        [DevCommand("power.info", Category = "power", Help = "Prints the electrical state and the load breakdown.")]
        private static string PowerInfo(CommandArgs args)
        {
            var power = Facility.Power;
            var generator = power.Generator;

            var builder = new StringBuilder(512);
            builder.Append($"state     {power.State}\n");
            builder.Append($"generator {generator.CurrentState}  {generator.FuelLitres:0.0} L  " +
                           $"{generator.SpareCans} can(s)  rpm {generator.Rpm01:0.00}\n");
            builder.Append($"load      {power.TotalLoadKilowatts:0.00} kW " +
                           $"({power.LoadFraction * 100f:0}% of rating)\n");
            builder.Append($"breaker   {(power.BreakerOpen ? "OPEN" : "closed")}  " +
                           $"overload {power.OverloadProgress01 * 100f:0}%\n");
            builder.Append($"battery   {power.BatteryCharge01 * 100f:0}%\n\n");

            var loads = new List<(string label, float kilowatts, bool powered)>();
            power.SnapshotLoads(loads);

            foreach (var entry in loads)
            {
                if (entry.kilowatts <= 0f) continue;
                builder.Append($"  {entry.label.PadRight(26)}{entry.kilowatts,6:0.00} kW " +
                               $"{(entry.powered ? "" : "<color=#FF6B61>unpowered</color>")}\n");
            }

            return builder.ToString();
        }

        [DevCommand("power.fuel", Category = "power",
            Help = "Sets the day tank level in litres.", Usage = "power.fuel <litres>")]
        private static string PowerFuel(CommandArgs args)
        {
            Facility.Power.Generator.DebugSetFuel(args.Float(0));
            return $"Day tank at {Facility.Power.Generator.FuelLitres:0.0} L.";
        }

        [DevCommand("power.cans", Category = "power",
            Help = "Sets the number of spare jerry cans.", Usage = "power.cans <count>")]
        private static string PowerCans(CommandArgs args)
        {
            Facility.Power.Generator.DebugSetCans(args.Int(0));
            return $"{Facility.Power.Generator.SpareCans} can(s).";
        }

        [DevCommand("power.infinite", Category = "power",
            Help = "Fuel and battery never deplete.", Usage = "power.infinite [on|off]")]
        private static string PowerInfinite(CommandArgs args)
        {
            DebugFlags.InfinitePower = args.Bool(0, DebugFlags.InfinitePower);
            DebugFlags.NotifyChanged();
            return $"Infinite power {(DebugFlags.InfinitePower ? "ON" : "OFF")}.";
        }

        [DevCommand("power.trip", Category = "power", Help = "Trips the main breaker immediately.")]
        private static string PowerTrip(CommandArgs args)
        {
            Facility.Power.TripBreaker("dev console");
            return "Breaker tripped.";
        }

        [DevCommand("power.kill", Category = "power", Help = "Stops the generator dead.")]
        private static string PowerKill(CommandArgs args)
        {
            Facility.Power.Generator.Shutdown("dev console");
            return "Generator stopped.";
        }

        [DevCommand("power.restore", Category = "power", Help = "Refills the tank, restarts the set and closes the breaker.")]
        private static string PowerRestore(CommandArgs args)
        {
            var power = Facility.Power;
            power.Generator.DebugForceRunning();
            power.BeginBreakerReset();
            return "Generator running. Breaker reset started.";
        }

        // =====================================================================
        // Environment
        // =====================================================================

        [DevCommand("env.info", Category = "env", Help = "Prints air, water and noise state.")]
        private static string EnvInfo(CommandArgs args)
        {
            var facility = Facility;
            var water = facility.Water;
            var air = facility.Ventilation;

            var loudest = facility.Noise.Loudest(out float loudestLevel);

            var builder = new StringBuilder(512);
            builder.Append($"air     {air.AirQuality01 * 100f:0}%  fan {air.Mode}  " +
                           $"hallucination {air.HallucinationPressure01:0.00}  " +
                           $"suffocation {air.SuffocationProgress01 * 100f:0}%\n");
            builder.Append($"water   {water.Level01:0.000} ({water.GaugeFeet:0.0} ft)  " +
                           $"net {water.NetRatePerHour:+0.000;-0.000}/h  " +
                           $"pump {(water.IsPumping ? "RUN" : "off")}" +
                           $"{(water.IsCavitating ? " CAVITATING" : "")}  " +
                           $"impeller {water.PumpCondition01 * 100f:0}%\n");
            builder.Append($"gates   sump {(water.SumpIsDry ? "DRY (Marlow can dig)" : "wet")}, " +
                           $"channel {(water.ChannelIsSwimmable ? "DEEP (Echo can swim)" : "shallow")}\n");
            builder.Append($"noise   loudest {loudest} at {loudestLevel:0.00}, " +
                           $"total energy {facility.Noise.TotalEnergy:0.00}\n");

            return builder.ToString();
        }

        [DevCommand("env.air", Category = "env",
            Help = "Sets air quality, 0 to 1.", Usage = "env.air <0-1>")]
        private static string EnvAir(CommandArgs args)
        {
            Facility.Ventilation.DebugSetAir(args.Float(0));
            return $"Air at {Facility.Ventilation.AirQuality01 * 100f:0}%.";
        }

        [DevCommand("env.water", Category = "env",
            Help = "Sets the water level, 0 to 1.", Usage = "env.water <0-1>")]
        private static string EnvWater(CommandArgs args)
        {
            Facility.Water.DebugSetLevel(args.Float(0));

            var water = Facility.Water;
            return $"Water at {water.Level01:0.000}. " +
                   $"Marlow {(water.SumpIsDry ? "can" : "cannot")} dig, " +
                   $"Echo {(water.ChannelIsSwimmable ? "can" : "cannot")} swim.";
        }

        [DevCommand("env.fan", Category = "env",
            Help = "Sets the ventilation fan.", Usage = "env.fan <off|low|purge>")]
        private static string EnvFan(CommandArgs args)
        {
            string requested = args.String(0);
            if (!System.Enum.TryParse(requested, ignoreCase: true, out FanMode mode))
                return $"Unknown fan mode '{requested}'.";

            Facility.Ventilation.Mode = mode;
            return $"Fan {mode}.";
        }

        [DevCommand("env.pump", Category = "env",
            Help = "Starts or stops the sump pump.", Usage = "env.pump [on|off]")]
        private static string EnvPump(CommandArgs args)
        {
            var water = Facility.Water;
            water.PumpCommanded = args.Bool(0, water.PumpCommanded);
            return $"Pump {(water.PumpCommanded ? "running" : "stopped")}.";
        }

        [DevCommand("env.freeze", Category = "env",
            Help = "Stops air decaying and water rising.", Usage = "env.freeze [on|off]")]
        private static string EnvFreeze(CommandArgs args)
        {
            DebugFlags.FreezeEnvironment = args.Bool(0, DebugFlags.FreezeEnvironment);
            DebugFlags.NotifyChanged();
            return $"Environment freeze {(DebugFlags.FreezeEnvironment ? "ON" : "OFF")}.";
        }

        [DevCommand("env.noise", Category = "env",
            Help = "Emits a noise at a node.", Usage = "env.noise <node> [0-1]")]
        private static string EnvNoise(CommandArgs args)
        {
            var node = Node(args.String(0));
            float loudness = args.Float(1, 0.8f);
            Facility.Noise.Emit(node, loudness, NoiseKind.Impact);
            return $"Emitted {loudness:0.00} at {node}.";
        }

        // =====================================================================
        // Cameras and doors
        // =====================================================================

        [DevCommand("cam.list", Category = "facility", Help = "Lists the cameras and their condition.")]
        private static string CamList(CommandArgs args)
        {
            var surveillance = Facility.Surveillance;
            var builder = new StringBuilder(512);

            foreach (var node in surveillance.CameraOrder)
            {
                builder.Append($"CAM {surveillance.CameraNumber(node):00}  {node.Key.PadRight(10)}");
                builder.Append($"{surveillance.ConditionOf(node) * 100f,5:0}%  ");
                builder.Append(surveillance.IsRebooting(node) ? "rebooting"
                    : surveillance.IsOnline(node) ? "online" : "<color=#FF6B61>offline</color>");
                if (node == surveillance.ActiveNode) builder.Append("  <- live");
                builder.Append('\n');
            }

            return builder.ToString();
        }

        [DevCommand("cam.select", Category = "facility",
            Help = "Switches the monitor to a camera.", Usage = "cam.select <node>")]
        private static string CamSelect(CommandArgs args)
        {
            var node = Node(args.String(0));
            Facility.Surveillance.SetMonitorUp(true);
            return Facility.Surveillance.SelectNode(node) ? $"Showing {node}." : $"{node} has no camera.";
        }

        [DevCommand("cam.monitor", Category = "facility",
            Help = "Raises or lowers the monitor.", Usage = "cam.monitor [on|off]")]
        private static string CamMonitor(CommandArgs args)
        {
            var surveillance = Facility.Surveillance;
            surveillance.SetMonitorUp(args.Bool(0, surveillance.MonitorUp));
            return $"Monitor {(surveillance.MonitorUp ? "up" : "down")}.";
        }

        [DevCommand("cam.repair", Category = "facility", Help = "Restores every camera to full condition.")]
        private static string CamRepair(CommandArgs args)
        {
            Facility.Surveillance.DebugRepairAll();
            return "All cameras repaired.";
        }

        [DevCommand("cam.always", Category = "facility",
            Help = "Every camera reports online regardless of condition.", Usage = "cam.always [on|off]")]
        private static string CamAlways(CommandArgs args)
        {
            DebugFlags.AllCamerasOnline = args.Bool(0, DebugFlags.AllCamerasOnline);
            DebugFlags.NotifyChanged();
            return $"Force cameras online {(DebugFlags.AllCamerasOnline ? "ON" : "OFF")}.";
        }

        [DevCommand("door", Category = "facility",
            Help = "Opens, closes or buckles a blast door.",
            Usage = "door <n|s> <open|close|buckle|repair>")]
        private static string Door(CommandArgs args)
        {
            string side = args.String(0).ToLowerInvariant();
            string barrierId = side.StartsWith("n") ? GrottoSpringsLayout.DoorNorth
                : side.StartsWith("s") ? GrottoSpringsLayout.DoorSouth
                : throw new System.ArgumentException("side must be n or s");

            if (!(Facility.GetBarrier(barrierId) is BlastDoor door))
                return $"No door registered as '{barrierId}'.";

            string action = args.String(1, "close").ToLowerInvariant();
            switch (action)
            {
                case "open": door.Open(); break;
                case "close": door.Close(); break;
                case "buckle": door.ApplyPressure(1e9f); break;
                case "repair": door.DebugRepair(); break;
                default: return $"Unknown action '{action}'.";
            }

            return $"{door.PowerLabel}: {door.State}.";
        }

        [DevCommand("grate", Category = "facility",
            Help = "Locks or unlocks the sump grate.", Usage = "grate [on|off]")]
        private static string Grate(CommandArgs args)
        {
            if (!(Facility.GetBarrier(GrottoSpringsLayout.SumpGrate) is SumpGrate grate))
                return "No sump grate in this scene.";

            grate.SetLocked(args.Bool(0, grate.IsLocked));
            return $"Grate bolts {(grate.IsLocked ? "shot" : "withdrawn")} " +
                   $"(energised: {grate.IsEnergised}, swimmer pass chance {grate.SwimmerPassChance:0.00}).";
        }

        // =====================================================================
        // Map
        // =====================================================================

        [DevCommand("map.nodes", Category = "map", Help = "Lists every node in the layout.")]
        private static string MapNodes(CommandArgs args)
        {
            var builder = new StringBuilder(1024);
            builder.Append("id         kind          zone      cam  hops  coupling  noise\n");

            foreach (var node in Facility.Graph.Nodes)
            {
                builder.Append(node.Id.Key.PadRight(11));
                builder.Append(node.Kind.ToString().PadRight(14));
                builder.Append(node.Zone.ToString().PadRight(10));
                builder.Append(node.HasCamera ? "yes  " : "no   ");
                builder.Append($"{Facility.Graph.HopsToStation(node.Id),4}  ");
                builder.Append($"{node.StationCoupling,8:0.00}  ");
                builder.Append($"{node.NoiseLevel,5:0.00}");
                if (node.IsLit) builder.Append("  LIT");
                builder.Append('\n');
            }

            return builder.ToString();
        }

        [DevCommand("map.links", Category = "map",
            Help = "Lists links, optionally only those touching a node.", Usage = "map.links [node]")]
        private static string MapLinks(CommandArgs args)
        {
            var filter = args.Count > 0 ? new NodeId(args.String(0)) : NodeId.None;
            var builder = new StringBuilder(1024);

            foreach (var link in Facility.Graph.Links)
            {
                if (filter.IsValid && link.A != filter && link.B != filter) continue;

                builder.Append($"{link}  ".PadRight(28));
                builder.Append(link.Allowed.ToString().PadRight(28));
                builder.Append($"{link.TraverseSeconds:0.0}s  ");
                if (link.MinWater > 0f) builder.Append($"water>={link.MinWater:0.00}  ");
                if (link.MaxWater < 1f) builder.Append($"water<={link.MaxWater:0.00}  ");
                if (!string.IsNullOrEmpty(link.BarrierId)) builder.Append($"[{link.BarrierId}]  ");
                if (link.LightDeters) builder.Append("light-deters  ");
                builder.Append('\n');
            }

            return builder.ToString();
        }

        [DevCommand("map.path", Category = "map",
            Help = "Finds a route under a traversal mask, as the AI would.",
            Usage = "map.path <from> <to> [walk|crawl|climb|swim|burrow|any]")]
        private static string MapPath(CommandArgs args)
        {
            var from = Node(args.String(0));
            var to = Node(args.String(1));

            string maskName = args.String(2, "walk");
            if (!System.Enum.TryParse(maskName, ignoreCase: true, out TraversalMask mask))
                return $"Unknown traversal '{maskName}'.";

            var path = new List<NodeId>();
            var filter = Facility.MakeFilter(mask);

            if (!Facility.Graph.TryFindPath(from, to, filter, path))
                return $"No {mask} route from {from} to {to} at the current water level " +
                       $"({Facility.Water.Level01:0.00}).";

            var builder = new StringBuilder(256);
            builder.Append(from.Key);
            foreach (var step in path) builder.Append(" -> ").Append(step.Key);
            builder.Append($"   ({path.Count} hops)");

            return builder.ToString();
        }

        [DevCommand("map.validate", Category = "map", Help = "Reports layout problems.")]
        private static string MapValidate(CommandArgs args)
        {
            var problems = Facility.Graph.Validate();
            if (problems.Count == 0) return "Layout is clean.";

            var builder = new StringBuilder(512);
            builder.Append($"{problems.Count} problem(s):\n");
            foreach (var problem in problems) builder.Append("  ").Append(problem).Append('\n');
            return builder.ToString();
        }

        // =====================================================================
        // Effects
        // =====================================================================

        [DevCommand("fx.scare", Category = "fx",
            Help = "Triggers a non-lethal scare.", Usage = "fx.scare [0-1]")]
        private static string FxScare(CommandArgs args)
        {
            float intensity = Mathf.Clamp01(args.Float(0, 0.8f));
            EventBus.Publish(new ScareSignal(intensity, "dev-console"));
            return $"Scare at {intensity:0.00}.";
        }

        [DevCommand("fx.jumpscare", Category = "fx",
            Help = "Plays a character's jumpscare without ending the night.",
            Usage = "fx.jumpscare <id>")]
        private static string FxJumpscare(CommandArgs args)
        {
            string id = args.String(0);
            if (!ServiceLocator.TryGet(out OverlayController overlay)) return "No overlay controller.";

            overlay.DebugJumpscare(id);
            return $"Playing {id}'s jumpscare.";
        }

        [DevCommand("fx.hallucinate", Category = "fx",
            Help = "Forces a hallucination.",
            Usage = "fx.hallucinate [phantomoncamera|feedcorruption|falsecontact|presence]")]
        private static string FxHallucinate(CommandArgs args)
        {
            if (!ServiceLocator.TryGet(out PhantomCotton phantom)) return "No PhantomCotton in this scene.";

            string requested = args.String(0, "phantomoncamera");
            if (!System.Enum.TryParse(requested, ignoreCase: true, out HallucinationKind kind))
                return $"Unknown kind '{requested}'.";

            phantom.DebugTrigger(kind);
            return $"Triggered {kind}.";
        }

        [DevCommand("fx.nojumpscares", Category = "fx",
            Help = "Suppresses jumpscare presentation entirely.", Usage = "fx.nojumpscares [on|off]")]
        private static string FxNoJumpscares(CommandArgs args)
        {
            DebugFlags.DisableJumpscares = args.Bool(0, DebugFlags.DisableJumpscares);
            DebugFlags.NotifyChanged();
            return $"Jumpscares {(DebugFlags.DisableJumpscares ? "suppressed" : "enabled")}.";
        }

        // =====================================================================
        // Global
        // =====================================================================

        [DevCommand("god", Category = "global",
            Help = "Attacks are logged instead of ending the night.", Usage = "god [on|off]")]
        private static string God(CommandArgs args)
        {
            DebugFlags.GodMode = args.Bool(0, DebugFlags.GodMode);
            DebugFlags.NotifyChanged();
            return $"God mode {(DebugFlags.GodMode ? "ON" : "OFF")}.";
        }

        [DevCommand("show", Category = "global",
            Help = "Toggles a debug visualisation.",
            Usage = "show <overlay|graph|noise|paths|audio> [on|off]")]
        private static string Show(CommandArgs args)
        {
            string what = args.String(0).ToLowerInvariant();
            bool enabled;

            switch (what)
            {
                case "overlay":
                    enabled = DebugFlags.ShowDebugOverlay = args.Bool(1, DebugFlags.ShowDebugOverlay);
                    break;
                case "graph":
                    enabled = DebugFlags.ShowNodeGraph = args.Bool(1, DebugFlags.ShowNodeGraph);
                    break;
                case "noise":
                    enabled = DebugFlags.ShowNoiseField = args.Bool(1, DebugFlags.ShowNoiseField);
                    break;
                case "paths":
                    enabled = DebugFlags.ShowAIPaths = args.Bool(1, DebugFlags.ShowAIPaths);
                    break;
                case "audio":
                    enabled = DebugFlags.ShowAudioRanges = args.Bool(1, DebugFlags.ShowAudioRanges);
                    break;
                default:
                    return $"Unknown visualisation '{what}'. Try overlay, graph, noise, paths or audio.";
            }

            DebugFlags.NotifyChanged();
            return $"show {what}: {(enabled ? "ON" : "OFF")}";
        }

        [DevCommand("flags", Category = "global", Help = "Prints every debug override.")]
        private static string Flags(CommandArgs args)
        {
            var builder = new StringBuilder(512);
            builder.Append($"freezeAI       {DebugFlags.FreezeAI}\n");
            builder.Append($"godMode        {DebugFlags.GodMode}\n");
            builder.Append($"infinitePower  {DebugFlags.InfinitePower}\n");
            builder.Append($"freezeEnv      {DebugFlags.FreezeEnvironment}\n");
            builder.Append($"noJumpscares   {DebugFlags.DisableJumpscares}\n");
            builder.Append($"allCameras     {DebugFlags.AllCamerasOnline}\n");
            builder.Append($"clockScale     {DebugFlags.ClockScale:0.##}\n");
            builder.Append($"forcedSeed     {(DebugFlags.ForcedSeed.HasValue ? DebugFlags.ForcedSeed.Value.ToString() : "none")}\n");
            builder.Append($"show.overlay   {DebugFlags.ShowDebugOverlay}\n");
            builder.Append($"show.graph     {DebugFlags.ShowNodeGraph}\n");
            builder.Append($"show.noise     {DebugFlags.ShowNoiseField}\n");
            builder.Append($"show.paths     {DebugFlags.ShowAIPaths}\n");
            return builder.ToString();
        }

        [DevCommand("reset", Category = "global", Help = "Clears every debug override back to shipping behaviour.")]
        private static string Reset(CommandArgs args)
        {
            DebugFlags.ResetAll();
            return "All debug overrides cleared.";
        }

        [DevCommand("stats", Category = "global", Help = "Frame timing, memory and draw statistics.")]
        private static string Stats(CommandArgs args)
        {
            long managed = System.GC.GetTotalMemory(false) / (1024 * 1024);

            return $"fps {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0}  " +
                   $"frame {Time.smoothDeltaTime * 1000f:0.0} ms\n" +
                   $"managed heap {managed} MB, GC collections gen0 {System.GC.CollectionCount(0)}\n" +
                   $"timeScale {Time.timeScale:0.00}, realtime {Time.realtimeSinceStartup:0} s\n" +
                   $"screen {Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio.value:0} Hz";
        }

        [DevCommand("teleport", Category = "global",
            Help = "Detaches the camera and flies free. Run again to return.",
            Usage = "teleport [node]")]
        private static string Teleport(CommandArgs args)
        {
            if (!ServiceLocator.TryGet(out FreeCamera freeCamera))
                return "No FreeCamera in this scene.";

            if (args.Count > 0)
            {
                var node = Node(args.String(0));
                freeCamera.Detach(Facility.Graph.PositionOf(node) + Vector3.up * 1.6f);
                return $"Free camera at {node}.";
            }

            freeCamera.Toggle();
            return freeCamera.IsDetached ? "Free camera on. WASD, shift to sprint." : "Camera returned to the station.";
        }
    }
}
