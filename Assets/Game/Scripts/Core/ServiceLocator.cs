using System;
using System.Collections.Generic;

namespace Grotto.Core
{
    /// <summary>
    /// Small, explicit service registry.
    ///
    /// Deliberately *not* a singleton-per-class pattern: systems are registered by
    /// the scene bootstrap and resolved by interface, so a test (or the dev console)
    /// can swap a fake in without any <c>FindObjectOfType</c> archaeology.
    ///
    /// Not thread safe — everything here runs on the Unity main thread.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>(32);

        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            var type = typeof(T);
            if (Services.ContainsKey(type))
            {
                GLog.Warn(LogChannel.Core,
                    $"Service '{type.Name}' registered twice. The later registration wins — " +
                    "check for a duplicate bootstrap in the scene.");
            }

            Services[type] = service;
            GLog.Verbose(LogChannel.Core, $"Registered service {type.Name}");
        }

        public static void Unregister<T>(T service) where T : class
        {
            var type = typeof(T);
            // Only remove if the instance still matches; a newer owner may have replaced it.
            if (Services.TryGetValue(type, out var existing) && ReferenceEquals(existing, service))
                Services.Remove(type);
        }

        /// <summary>Resolves a service, or throws with a message that says who is missing.</summary>
        public static T Get<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out var service))
                return (T)service;

            throw new InvalidOperationException(
                $"Service '{typeof(T).Name}' was requested before it was registered. " +
                "Services are registered by GameBootstrap in script execution order; " +
                "resolve services in Start(), not Awake().");
        }

        /// <summary>Non-throwing resolve for optional systems (audio on a headless test run, etc.).</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out var found))
            {
                service = (T)found;
                return true;
            }

            service = null;
            return false;
        }

        public static bool Has<T>() where T : class => Services.ContainsKey(typeof(T));

        public static void Clear() => Services.Clear();

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode() => Clear();
#endif
    }
}
