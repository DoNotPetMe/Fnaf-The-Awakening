using System;
using System.Collections.Generic;

namespace Grotto.Core
{
    /// <summary>
    /// Typed, allocation-light publish/subscribe hub for gameplay signals.
    ///
    /// Signals are structs (see <see cref="GameSignals"/>) so publishing does not
    /// allocate. Handlers are stored per signal type in a single dictionary rather
    /// than in <c>static</c> generic fields, because a flat dictionary can actually
    /// be cleared — which matters when Domain Reload is disabled and stale handlers
    /// from the previous Play session would otherwise fire into destroyed objects.
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> Handlers = new Dictionary<Type, Delegate>(64);

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) return;
            var type = typeof(T);
            Handlers[type] = Handlers.TryGetValue(type, out var existing)
                ? Delegate.Combine(existing, handler)
                : handler;
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) return;
            var type = typeof(T);
            if (!Handlers.TryGetValue(type, out var existing)) return;

            var remaining = Delegate.Remove(existing, handler);
            if (remaining == null) Handlers.Remove(type);
            else Handlers[type] = remaining;
        }

        public static void Publish<T>(T signal) where T : struct
        {
            if (!Handlers.TryGetValue(typeof(T), out var existing)) return;

            // One misbehaving listener must not stop the rest from being notified,
            // so invoke the invocation list entry by entry.
            var list = existing.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<T>)list[i]).Invoke(signal);
                }
                catch (Exception ex)
                {
                    GLog.Error(LogChannel.Core,
                        $"Listener '{list[i].Method.DeclaringType?.Name}.{list[i].Method.Name}' " +
                        $"threw while handling {typeof(T).Name}.");
                    GLog.Exception(ex);
                }
            }
        }

        public static void Clear() => Handlers.Clear();

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode() => Clear();
#endif
    }
}
