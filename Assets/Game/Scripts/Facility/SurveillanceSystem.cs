using System;
using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>A thing that can produce a picture for the monitor.</summary>
    public interface ICameraFeed
    {
        NodeId Node { get; }
        /// <summary>The live texture. May be null until the feed is first activated.</summary>
        Texture Texture { get; }
        /// <summary>Only the selected feed renders — everything else is switched off.</summary>
        void SetRendering(bool rendering);
    }

    /// <summary>
    /// The camera loop: fifteen nodes wired, twelve of them actually covered, and one
    /// monitor to look at them through.
    ///
    /// Only the selected camera renders. That is both the honest simulation of a
    /// 1979 multiplexer and a large performance win — a cave with fifteen live
    /// cameras would spend its whole frame budget rendering rooms nobody is looking
    /// at. Cameras wear out while they are the live feed, and a failed one is snow
    /// until you spend six seconds rebooting it.
    /// </summary>
    public sealed class SurveillanceSystem : IPowerConsumer
    {
        private sealed class CameraState
        {
            public ICameraFeed Feed;
            public float Condition01 = 1f;
            public bool Rebooting;
            public float RebootTimer;
        }

        private readonly FacilityTuning _tuning;
        private readonly FacilityGraph _graph;
        private readonly Dictionary<NodeId, CameraState> _cameras = new Dictionary<NodeId, CameraState>(16);
        private readonly List<NodeId> _order = new List<NodeId>(16);

        private bool _powered = true;
        private bool _monitorUp;

        public bool MonitorUp => _monitorUp && _powered;

        /// <summary>Camera the monitor is showing. Valid even while the monitor is down.</summary>
        public NodeId ActiveNode { get; private set; } = NodeId.None;

        /// <summary>Nodes with a camera, in map order.</summary>
        public IReadOnlyList<NodeId> CameraOrder => _order;

        public event Action<bool> MonitorToggled;
        public event Action<NodeId> ActiveNodeChanged;

        /// <summary>Raised with a 0..1 loudness for the monitor's own clunk.</summary>
        public event Action<float> NoiseBurst;

        public SurveillanceSystem(FacilityGraph graph, FacilityTuning tuning)
        {
            _graph = graph;
            _tuning = tuning != null ? tuning : ScriptableObject.CreateInstance<FacilityTuning>();

            foreach (var node in _graph.Nodes)
            {
                if (!node.HasCamera) continue;
                _cameras[node.Id] = new CameraState();
                _order.Add(node.Id);
            }

            // Stable order, so CAM numbers do not shuffle between runs.
            _order.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            if (_order.Count > 0) ActiveNode = _order[0];
        }

        public void RegisterFeed(ICameraFeed feed)
        {
            if (feed == null) return;

            if (!_cameras.TryGetValue(feed.Node, out var state))
            {
                GLog.Warn(LogChannel.Facility,
                    $"Camera feed registered for '{feed.Node}', which the layout says has no camera. Ignored.");
                return;
            }

            state.Feed = feed;
            feed.SetRendering(MonitorUp && ActiveNode == feed.Node);
        }

        public void UnregisterFeed(ICameraFeed feed)
        {
            if (feed == null) return;
            if (_cameras.TryGetValue(feed.Node, out var state) && ReferenceEquals(state.Feed, feed))
                state.Feed = null;
        }

        public void ResetForNight()
        {
            foreach (var state in _cameras.Values)
            {
                state.Condition01 = 1f;
                state.Rebooting = false;
                state.RebootTimer = 0f;
            }
            SetMonitorUp(false);
        }

        // ---------------------------------------------------------------------
        // Queries
        // ---------------------------------------------------------------------

        public bool HasCamera(NodeId node) => _cameras.ContainsKey(node);

        public float ConditionOf(NodeId node)
            => _cameras.TryGetValue(node, out var s) ? s.Condition01 : 0f;

        /// <summary>A camera is online when it has power and enough condition to hold a picture.</summary>
        public bool IsOnline(NodeId node)
        {
            if (DebugFlags.IsDevBuild && DebugFlags.AllCamerasOnline) return true;
            if (!_powered) return false;
            if (!_cameras.TryGetValue(node, out var s)) return false;
            return !s.Rebooting && s.Condition01 > _tuning.cameraFailureThreshold;
        }

        public bool IsRebooting(NodeId node)
            => _cameras.TryGetValue(node, out var s) && s.Rebooting;

        public float RebootProgress01(NodeId node)
        {
            if (!_cameras.TryGetValue(node, out var s) || !s.Rebooting) return 0f;
            return Mathf.Clamp01(s.RebootTimer / Mathf.Max(0.01f, _tuning.cameraRebootSeconds));
        }

        public Texture TextureOf(NodeId node)
            => _cameras.TryGetValue(node, out var s) ? s.Feed?.Texture : null;

        /// <summary>CAM number as printed on the monitor, 1-based. 0 when the node has no camera.</summary>
        public int CameraNumber(NodeId node)
        {
            int index = _order.IndexOf(node);
            return index < 0 ? 0 : index + 1;
        }

        // ---------------------------------------------------------------------
        // Commands
        // ---------------------------------------------------------------------

        public void SetMonitorUp(bool up)
        {
            if (_monitorUp == up) return;
            _monitorUp = up;

            RefreshRendering();
            NoiseBurst?.Invoke(0.25f);
            MonitorToggled?.Invoke(MonitorUp);
            GLog.Verbose(LogChannel.Facility, $"Monitor {(up ? "raised" : "lowered")}.");
        }

        public void ToggleMonitor() => SetMonitorUp(!_monitorUp);

        public bool SelectNode(NodeId node)
        {
            if (!_cameras.ContainsKey(node) || node == ActiveNode) return false;

            ActiveNode = node;
            RefreshRendering();
            NoiseBurst?.Invoke(0.18f);
            ActiveNodeChanged?.Invoke(node);
            EventBus.Publish(new CameraSwitchedSignal(node));
            return true;
        }

        public void SelectNext(int direction)
        {
            if (_order.Count == 0) return;
            int index = Mathf.Max(0, _order.IndexOf(ActiveNode));
            index = (index + direction + _order.Count) % _order.Count;
            SelectNode(_order[index]);
        }

        /// <summary>Starts a reboot on a failed camera. Returns false if it is fine or already rebooting.</summary>
        public bool BeginReboot(NodeId node)
        {
            if (!_cameras.TryGetValue(node, out var state)) return false;
            if (state.Rebooting || state.Condition01 > _tuning.cameraFailureThreshold) return false;

            state.Rebooting = true;
            state.RebootTimer = 0f;
            GLog.Info(LogChannel.Facility, $"Rebooting camera at {node}.");
            return true;
        }

        // ---------------------------------------------------------------------
        // Simulation
        // ---------------------------------------------------------------------

        public void Tick(float hourDelta, float realDelta)
        {
            foreach (var pair in _cameras)
            {
                var state = pair.Value;

                if (state.Rebooting)
                {
                    state.RebootTimer += realDelta;
                    if (state.RebootTimer >= _tuning.cameraRebootSeconds)
                    {
                        state.Rebooting = false;
                        state.RebootTimer = 0f;
                        state.Condition01 = 1f;
                        GLog.Info(LogChannel.Facility, $"Camera at {pair.Key} back online.");
                    }
                    continue;
                }

                // Only the live feed wears. Idle cameras are genuinely idle.
                if (MonitorUp && pair.Key == ActiveNode && !(DebugFlags.IsDevBuild && DebugFlags.AllCamerasOnline))
                {
                    float before = state.Condition01;
                    state.Condition01 = Mathf.Max(0f, state.Condition01 - _tuning.cameraWearPerHour * hourDelta);

                    if (before > _tuning.cameraFailureThreshold && state.Condition01 <= _tuning.cameraFailureThreshold)
                    {
                        GLog.Info(LogChannel.Facility, $"Camera at {pair.Key} dropped out.");
                        EventBus.Publish(new AlertSignal(
                            $"CAM {CameraNumber(pair.Key):00} signal lost.", AlertSeverity.Warning));
                        RefreshRendering();
                    }
                }
            }
        }

        private void RefreshRendering()
        {
            foreach (var pair in _cameras)
            {
                bool shouldRender = MonitorUp && pair.Key == ActiveNode && IsOnline(pair.Key);
                pair.Value.Feed?.SetRendering(shouldRender);
            }
        }

        // ---- IPowerConsumer --------------------------------------------------

        public string PowerLabel => "Camera multiplexer";
        public float LoadKilowatts => MonitorUp ? _tuning.monitorKilowatts : 0f;
        public bool IsEssential => false;

        public void SetPowered(bool powered)
        {
            if (_powered == powered) return;
            _powered = powered;
            RefreshRendering();
            if (!powered) MonitorToggled?.Invoke(false);
        }

        public void DebugRepairAll()
        {
            foreach (var state in _cameras.Values)
            {
                state.Condition01 = 1f;
                state.Rebooting = false;
            }
            RefreshRendering();
        }
    }
}
