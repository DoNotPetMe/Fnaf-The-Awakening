using System;
using UnityEngine.InputSystem;

namespace Grotto.Player
{
    /// <summary>
    /// The player's control scheme, built in code.
    ///
    /// No <c>.inputactions</c> asset: that file is generated JSON with embedded GUIDs
    /// that merges badly and cannot be reviewed, and this game's controls are a fixed
    /// list of eighteen bindings that will not be reconfigured by a designer. Defining
    /// them here makes every binding greppable and the whole scheme diffable.
    ///
    /// Rebinding at runtime still works — the Input System's rebinding API operates on
    /// <see cref="InputAction"/> instances regardless of where they came from.
    /// </summary>
    public sealed class GrottoInput : IDisposable
    {
        private readonly InputActionMap _map;

        public InputAction Look { get; }
        public InputAction ToggleMonitor { get; }
        public InputAction NextCamera { get; }
        public InputAction PreviousCamera { get; }
        public InputAction Interact { get; }

        public InputAction DoorNorth { get; }
        public InputAction DoorSouth { get; }
        public InputAction LightNorth { get; }
        public InputAction LightSouth { get; }
        public InputAction LightChase { get; }

        public InputAction CycleFan { get; }
        public InputAction TogglePump { get; }
        public InputAction ToggleGrate { get; }
        public InputAction Headlamp { get; }

        public InputAction HoldBreakerReset { get; }
        public InputAction Refuel { get; }
        public InputAction Crank { get; }

        public InputAction Pause { get; }
        public InputAction ToggleConsole { get; }
        public InputAction ToggleDebugOverlay { get; }

        public GrottoInput()
        {
            _map = new InputActionMap("Station");

            Look = Add("Look", InputActionType.Value, "<Mouse>/delta");
            Look.AddBinding("<Gamepad>/rightStick");

            ToggleMonitor = Add("ToggleMonitor", InputActionType.Button, "<Keyboard>/space");
            ToggleMonitor.AddBinding("<Gamepad>/buttonNorth");

            NextCamera = Add("NextCamera", InputActionType.Button, "<Keyboard>/e");
            NextCamera.AddBinding("<Gamepad>/rightShoulder");

            PreviousCamera = Add("PreviousCamera", InputActionType.Button, "<Keyboard>/q");
            PreviousCamera.AddBinding("<Gamepad>/leftShoulder");

            Interact = Add("Interact", InputActionType.Button, "<Mouse>/leftButton");
            Interact.AddBinding("<Keyboard>/enter");
            Interact.AddBinding("<Gamepad>/buttonSouth");

            // Doors on the keys either side of the home row: muscle memory maps to
            // "left door" and "right door" without anyone having to think about it.
            DoorNorth = Add("DoorNorth", InputActionType.Button, "<Keyboard>/a");
            DoorNorth.AddBinding("<Gamepad>/dpad/left");

            DoorSouth = Add("DoorSouth", InputActionType.Button, "<Keyboard>/d");
            DoorSouth.AddBinding("<Gamepad>/dpad/right");

            LightNorth = Add("LightNorth", InputActionType.Button, "<Keyboard>/1");
            LightSouth = Add("LightSouth", InputActionType.Button, "<Keyboard>/2");
            LightChase = Add("LightChase", InputActionType.Button, "<Keyboard>/3");
            LightChase.AddBinding("<Gamepad>/dpad/up");

            CycleFan = Add("CycleFan", InputActionType.Button, "<Keyboard>/f");
            TogglePump = Add("TogglePump", InputActionType.Button, "<Keyboard>/p");
            ToggleGrate = Add("ToggleGrate", InputActionType.Button, "<Keyboard>/g");
            ToggleGrate.AddBinding("<Gamepad>/dpad/down");

            Headlamp = Add("Headlamp", InputActionType.Button, "<Keyboard>/l");

            // Held, not pressed — the whole point of the breaker is the time it costs.
            HoldBreakerReset = Add("HoldBreakerReset", InputActionType.Button, "<Keyboard>/r");
            HoldBreakerReset.AddBinding("<Gamepad>/buttonWest");

            Refuel = Add("Refuel", InputActionType.Button, "<Keyboard>/t");
            Crank = Add("Crank", InputActionType.Button, "<Keyboard>/y");

            Pause = Add("Pause", InputActionType.Button, "<Keyboard>/escape");
            Pause.AddBinding("<Gamepad>/start");

            ToggleConsole = Add("ToggleConsole", InputActionType.Button, "<Keyboard>/backquote");
            ToggleDebugOverlay = Add("ToggleDebugOverlay", InputActionType.Button, "<Keyboard>/f3");
        }

        private InputAction Add(string actionName, InputActionType type, string binding)
        {
            var action = _map.AddAction(actionName, type);
            action.AddBinding(binding);
            return action;
        }

        public void Enable() => _map.Enable();
        public void Disable() => _map.Disable();

        public bool IsEnabled => _map.enabled;

        public void Dispose()
        {
            _map.Disable();
            _map.Dispose();
        }

        /// <summary>Human-readable binding list, for the pause menu's controls page.</summary>
        public string DescribeBindings()
        {
            var builder = new System.Text.StringBuilder(512);
            foreach (var action in _map.actions)
            {
                builder.Append(action.name).Append(": ");
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    if (i > 0) builder.Append(" / ");
                    builder.Append(InputControlPath.ToHumanReadableString(
                        action.bindings[i].effectivePath,
                        InputControlPath.HumanReadableStringOptions.OmitDevice));
                }
                builder.AppendLine();
            }
            return builder.ToString();
        }
    }
}
