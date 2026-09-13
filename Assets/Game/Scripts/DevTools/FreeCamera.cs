using UnityEngine;
using UnityEngine.InputSystem;
using Grotto.Core;
using Grotto.Player;

namespace Grotto.DevTools
{
    /// <summary>
    /// Detaches the camera from the station and flies it around the cave.
    ///
    /// Indispensable for level work: the whole game is played from one chair, so
    /// without this there is no way to look at the geometry the generator produced,
    /// check that a chamber actually joins its neighbour, or see what a character
    /// looks like from the front.
    ///
    /// Remembers its original parent and transform, so returning puts the player
    /// exactly back where they were mid-night.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FreeCamera : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 7f;
        [SerializeField] private float sprintMultiplier = 4f;
        [SerializeField] private float lookSensitivity = 0.12f;

        private Camera _camera;
        private Transform _originalParent;
        private Vector3 _originalLocalPosition;
        private Quaternion _originalLocalRotation;

        private StationController _station;
        private float _yaw;
        private float _pitch;

        public bool IsDetached { get; private set; }

        private void Start()
        {
            _camera = Camera.main;
            ServiceLocator.TryGet(out _station);
            ServiceLocator.Register(this);
        }

        private void OnDestroy() => ServiceLocator.Unregister(this);

        public void Toggle()
        {
            if (IsDetached) Reattach();
            else Detach(null);
        }

        /// <summary>Detaches, optionally jumping to a position.</summary>
        public void Detach(Vector3? position)
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null)
            {
                GLog.Error(LogChannel.Dev, "No main camera to detach.");
                return;
            }

            if (!IsDetached)
            {
                _originalParent = _camera.transform.parent;
                _originalLocalPosition = _camera.transform.localPosition;
                _originalLocalRotation = _camera.transform.localRotation;

                _camera.transform.SetParent(null, worldPositionStays: true);
                IsDetached = true;

                // The station would keep writing to the head pivot and fight us.
                if (_station != null) _station.enabled = false;

                var euler = _camera.transform.eulerAngles;
                _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
                _yaw = euler.y;
            }

            if (position.HasValue) _camera.transform.position = position.Value;
        }

        public void Reattach()
        {
            if (!IsDetached || _camera == null) return;

            _camera.transform.SetParent(_originalParent, worldPositionStays: false);
            _camera.transform.localPosition = _originalLocalPosition;
            _camera.transform.localRotation = _originalLocalRotation;

            IsDetached = false;
            if (_station != null) _station.enabled = true;
        }

        private void Update()
        {
            if (!IsDetached || _camera == null) return;

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null) return;

            // Right mouse to look, so the cursor stays usable for the console.
            if (mouse != null && mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                _yaw += delta.x * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * lookSensitivity, -89f, 89f);
                _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            var move = Vector3.zero;
            if (keyboard.wKey.isPressed) move += _camera.transform.forward;
            if (keyboard.sKey.isPressed) move -= _camera.transform.forward;
            if (keyboard.dKey.isPressed) move += _camera.transform.right;
            if (keyboard.aKey.isPressed) move -= _camera.transform.right;
            if (keyboard.eKey.isPressed) move += Vector3.up;
            if (keyboard.qKey.isPressed) move -= Vector3.up;

            if (move.sqrMagnitude < 0.0001f) return;

            float speed = moveSpeed * (keyboard.leftShiftKey.isPressed ? sprintMultiplier : 1f);
            if (keyboard.leftCtrlKey.isPressed) speed *= 0.25f;

            // Unscaled, so free-flying still works with the game paused.
            _camera.transform.position += move.normalized * speed * Time.unscaledDeltaTime;
        }
    }
}
