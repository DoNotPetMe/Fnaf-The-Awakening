namespace Grotto.Facility
{
    /// <summary>Overall state of the facility's electrical supply.</summary>
    public enum PowerState
    {
        /// <summary>Generator running, breaker closed, everything works.</summary>
        Online,
        /// <summary>Drawing above the continuous rating. The overload timer is running.</summary>
        Overloaded,
        /// <summary>Breaker open. Only essential circuits, on battery.</summary>
        Tripped,
        /// <summary>No generator and no battery. Torch and prayer.</summary>
        Blackout
    }

    /// <summary>
    /// Anything that draws current.
    ///
    /// Consumers report their own draw rather than the grid tracking it, so adding a
    /// new powered device is one interface implementation and no edits to the grid.
    /// </summary>
    public interface IPowerConsumer
    {
        /// <summary>Name shown on the station's load readout and in the debug overlay.</summary>
        string PowerLabel { get; }

        /// <summary>Current draw in kilowatts. Return 0 when idle.</summary>
        float LoadKilowatts { get; }

        /// <summary>Essential circuits stay alive on battery after a trip.</summary>
        bool IsEssential { get; }

        /// <summary>Called when supply to this consumer starts or stops.</summary>
        void SetPowered(bool powered);
    }
}
