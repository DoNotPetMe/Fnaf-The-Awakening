namespace Grotto.Core
{
    /// <summary>How a night ended.</summary>
    public enum NightOutcome
    {
        Survived,
        /// <summary>An animatronic reached the player.</summary>
        Killed,
        /// <summary>The control room flooded — the pump lost the race.</summary>
        Flooded,
        /// <summary>Air quality bottomed out for too long.</summary>
        Suffocated,
        /// <summary>The player quit or the night was aborted from the dev console.</summary>
        Aborted
    }

    public enum AlertSeverity { Info, Warning, Critical }

    // -------------------------------------------------------------------------
    // Cross-cutting signals. Anything a *different subsystem* needs to know about
    // goes here. Tightly coupled chatter inside one subsystem uses plain C# events
    // on the system itself instead — the bus is not a dumping ground.
    // -------------------------------------------------------------------------

    public readonly struct NightStartedSignal
    {
        public readonly int Night;
        public readonly int Seed;
        public NightStartedSignal(int night, int seed) { Night = night; Seed = seed; }
    }

    public readonly struct NightHourChangedSignal
    {
        public readonly int Hour;
        public NightHourChangedSignal(int hour) { Hour = hour; }
    }

    public readonly struct NightEndedSignal
    {
        public readonly int Night;
        public readonly NightOutcome Outcome;
        public NightEndedSignal(int night, NightOutcome outcome) { Night = night; Outcome = outcome; }
    }

    /// <summary>An animatronic has committed to a kill. The UI turns this into a jumpscare.</summary>
    public readonly struct AttackSignal
    {
        public readonly string AnimatronicId;
        public readonly NodeId From;
        public AttackSignal(string animatronicId, NodeId from) { AnimatronicId = animatronicId; From = from; }
    }

    /// <summary>A non-lethal fright: a hallucination, a slammed door, a cable-chase scrape.</summary>
    public readonly struct ScareSignal
    {
        public readonly float Intensity;   // 0..1
        public readonly string Source;
        public ScareSignal(float intensity, string source) { Intensity = intensity; Source = source; }
    }

    /// <summary>Operator-facing message for the station's alert strip.</summary>
    public readonly struct AlertSignal
    {
        public readonly string Message;
        public readonly AlertSeverity Severity;
        public AlertSignal(string message, AlertSeverity severity = AlertSeverity.Info)
        {
            Message = message; Severity = severity;
        }
    }

    public readonly struct CameraSwitchedSignal
    {
        public readonly NodeId Node;
        public CameraSwitchedSignal(NodeId node) { Node = node; }
    }

    /// <summary>Emitted whenever an animatronic changes state, purely so tooling can watch it.</summary>
    public readonly struct AiStateChangedSignal
    {
        public readonly string AnimatronicId;
        public readonly string State;
        public readonly NodeId Node;
        public AiStateChangedSignal(string animatronicId, string state, NodeId node)
        {
            AnimatronicId = animatronicId; State = state; Node = node;
        }
    }
}
