using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Grotto.Core;

namespace Grotto.DevTools
{
    /// <summary>
    /// Marks a static method as a console command.
    ///
    /// The method must be <c>static</c> and take a single <see cref="CommandArgs"/>.
    /// It may return <see cref="string"/> (printed to the console) or <c>void</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DevCommandAttribute : Attribute
    {
        public string Name { get; }
        public string Help { get; set; } = "";
        public string Usage { get; set; } = "";
        public string Category { get; set; } = "general";

        public DevCommandAttribute(string name) => Name = name;
    }

    /// <summary>Parsed arguments, with typed accessors that fail loudly rather than silently.</summary>
    public sealed class CommandArgs
    {
        private readonly string[] _values;

        public CommandArgs(string[] values) => _values = values ?? Array.Empty<string>();

        public int Count => _values.Length;
        public string Raw => string.Join(" ", _values);

        public string String(int index, string fallback = null)
        {
            if (index < _values.Length) return _values[index];
            if (fallback != null) return fallback;
            throw new ArgumentException($"expected an argument at position {index + 1}");
        }

        public int Int(int index, int? fallback = null)
        {
            if (index < _values.Length && int.TryParse(_values[index], out int value)) return value;
            if (fallback.HasValue) return fallback.Value;
            throw new ArgumentException($"argument {index + 1} must be a whole number");
        }

        public float Float(int index, float? fallback = null)
        {
            if (index < _values.Length &&
                float.TryParse(_values[index], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value))
                return value;

            if (fallback.HasValue) return fallback.Value;
            throw new ArgumentException($"argument {index + 1} must be a number");
        }

        /// <summary>
        /// Reads a boolean, accepting on/off, true/false, 1/0, yes/no. With no argument
        /// at all, returns <paramref name="toggleFrom"/> inverted — so <c>ai.freeze</c>
        /// with no argument toggles, which is what anyone typing it actually wants.
        /// </summary>
        public bool Bool(int index, bool toggleFrom)
        {
            if (index >= _values.Length) return !toggleFrom;

            string value = _values[index].ToLowerInvariant();
            switch (value)
            {
                case "1": case "on": case "true": case "yes": return true;
                case "0": case "off": case "false": case "no": return false;
                default: throw new ArgumentException($"'{_values[index]}' is not on/off");
            }
        }
    }

    /// <summary>
    /// Finds and runs console commands.
    ///
    /// Commands are declared by attribute next to the code they operate on, rather
    /// than in one central switch. That matters more than it sounds: a debug command
    /// that lives next to its system gets updated when the system changes, and a
    /// central registry of eighty commands does not.
    /// </summary>
    public static class DevCommandRegistry
    {
        public sealed class Command
        {
            public string Name;
            public string Help;
            public string Usage;
            public string Category;
            public MethodInfo Method;
            public bool ReturnsString;

            public string Signature => string.IsNullOrEmpty(Usage) ? Name : Usage;
        }

        private static readonly SortedDictionary<string, Command> Commands =
            new SortedDictionary<string, Command>(StringComparer.OrdinalIgnoreCase);

        private static bool _initialised;

        public static IEnumerable<Command> All => Commands.Values;
        public static int Count => Commands.Count;

        public static void Initialise()
        {
            if (_initialised) return;
            _initialised = true;

            Commands.Clear();
            int scanned = 0;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Only our own assemblies — reflecting over the whole domain including
                // UnityEngine and mscorlib costs hundreds of milliseconds for nothing.
                string name = assembly.GetName().Name;
                if (!name.StartsWith("Grotto", StringComparison.Ordinal)) continue;

                scanned++;
                RegisterFrom(assembly);
            }

            GLog.Info(LogChannel.Dev, $"Dev console: {Commands.Count} commands from {scanned} assemblies.");
        }

        private static void RegisterFrom(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = Array.FindAll(ex.Types, t => t != null);
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(flags))
                {
                    var attribute = method.GetCustomAttribute<DevCommandAttribute>();
                    if (attribute == null) continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length != 1 || parameters[0].ParameterType != typeof(CommandArgs))
                    {
                        GLog.Warn(LogChannel.Dev,
                            $"[DevCommand] '{type.Name}.{method.Name}' must take exactly one CommandArgs. Skipped.");
                        continue;
                    }

                    if (method.ReturnType != typeof(string) && method.ReturnType != typeof(void))
                    {
                        GLog.Warn(LogChannel.Dev,
                            $"[DevCommand] '{type.Name}.{method.Name}' must return string or void. Skipped.");
                        continue;
                    }

                    if (Commands.ContainsKey(attribute.Name))
                    {
                        GLog.Warn(LogChannel.Dev, $"Duplicate dev command '{attribute.Name}'. The later one wins.");
                    }

                    Commands[attribute.Name] = new Command
                    {
                        Name = attribute.Name,
                        Help = attribute.Help,
                        Usage = attribute.Usage,
                        Category = attribute.Category,
                        Method = method,
                        ReturnsString = method.ReturnType == typeof(string)
                    };
                }
            }
        }

        /// <summary>Runs one line. Returns output for the console, never throws.</summary>
        public static string Execute(string line)
        {
            Initialise();

            if (string.IsNullOrWhiteSpace(line)) return "";

            var tokens = Tokenise(line);
            if (tokens.Count == 0) return "";

            string name = tokens[0];
            tokens.RemoveAt(0);

            if (!Commands.TryGetValue(name, out var command))
            {
                var suggestion = Suggest(name);
                return suggestion == null
                    ? $"Unknown command '{name}'. Type 'help' for a list."
                    : $"Unknown command '{name}'. Did you mean '{suggestion}'?";
            }

            try
            {
                var result = command.Method.Invoke(null, new object[] { new CommandArgs(tokens.ToArray()) });
                return command.ReturnsString ? (string)result ?? "" : "ok";
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                return $"{name}: {inner.Message}" +
                       (string.IsNullOrEmpty(command.Usage) ? "" : $"\nusage: {command.Usage}");
            }
            catch (Exception ex)
            {
                return $"{name}: {ex.Message}";
            }
        }

        /// <summary>
        /// Splits a line into tokens, honouring double quotes so an argument can
        /// contain spaces.
        /// </summary>
        public static List<string> Tokenise(string line)
        {
            var tokens = new List<string>(8);
            var current = new StringBuilder(32);
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0) tokens.Add(current.ToString());
            return tokens;
        }

        /// <summary>Command names starting with a prefix, for tab completion.</summary>
        public static List<string> Complete(string prefix)
        {
            Initialise();

            var matches = new List<string>(8);
            if (string.IsNullOrEmpty(prefix)) return matches;

            foreach (var pair in Commands)
                if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    matches.Add(pair.Key);

            return matches;
        }

        /// <summary>Nearest command by edit distance, for a typo.</summary>
        private static string Suggest(string typed)
        {
            string best = null;
            int bestDistance = int.MaxValue;

            foreach (var pair in Commands)
            {
                int distance = EditDistance(typed.ToLowerInvariant(), pair.Key.ToLowerInvariant());
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = pair.Key;
            }

            // Only suggest when it is actually close; otherwise it is noise.
            return bestDistance <= 3 ? best : null;
        }

        private static int EditDistance(string a, string b)
        {
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++) previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }

        /// <summary>Formatted command list, grouped by category.</summary>
        public static string Describe(string categoryFilter = null)
        {
            Initialise();

            var byCategory = new SortedDictionary<string, List<Command>>(StringComparer.OrdinalIgnoreCase);
            foreach (var command in Commands.Values)
            {
                if (!string.IsNullOrEmpty(categoryFilter) &&
                    !command.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase)) continue;

                if (!byCategory.TryGetValue(command.Category, out var list))
                    byCategory[command.Category] = list = new List<Command>();

                list.Add(command);
            }

            if (byCategory.Count == 0)
                return $"No commands in category '{categoryFilter}'.";

            var builder = new StringBuilder(2048);
            foreach (var pair in byCategory)
            {
                builder.Append("\n<b>").Append(pair.Key.ToUpperInvariant()).Append("</b>\n");
                foreach (var command in pair.Value)
                    builder.Append("  ").Append(command.Signature.PadRight(34))
                           .Append(command.Help).Append('\n');
            }

            return builder.ToString();
        }

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode()
        {
            _initialised = false;
            Commands.Clear();
        }
#endif
    }
}
