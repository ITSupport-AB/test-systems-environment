// ============================================================================
// PlayerController.cs — Multiplatform input handler using Unity New Input System.
// Part of Freebuff Desktop (Unity GaaS).
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Freebuff.Core
{
    /// <summary>
    /// Translates raw Input System actions into gameplay-ready events.
    /// Supports touch (mobile), keyboard/mouse (PC), and gamepad.
    /// The gameplay state machine reads these events rather than polling
    /// the Input System directly.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float rotationSpeed = 10f;

        [Header("Input Actions (from Input Actions asset)")]
        [SerializeField] private InputActionAsset inputActions;

        // ── Events ────────────────────────────────────────────────────────
        /// <summary>Fires when the player presses the fire/attack action.</summary>
        public event Action OnFire;

        /// <summary>Fires when the player presses restart (e.g., after Game Over).</summary>
        public event Action OnRestart;

        /// <summary>Fires when the player opens the pause menu.</summary>
        public event Action OnPause;

        // ── State ─────────────────────────────────────────────────────────
        private CharacterController _charController;
        private InputAction _moveAction;
        private InputAction _fireAction;
        private InputAction _restartAction;
        private InputAction _pauseAction;
        private Vector2 _moveInput;
        private bool _enabled = true;

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
            _charController = GetComponent<CharacterController>();

            // Bind actions from the Input Action asset.
            var gameplayMap = inputActions.FindActionMap("Gameplay");
            _moveAction    = gameplayMap?.FindAction("Move");
            _fireAction    = gameplayMap?.FindAction("Fire");
            _restartAction = gameplayMap?.FindAction("Restart");
            _pauseAction   = gameplayMap?.FindAction("Pause");
        }

        private void OnEnable()
        {
            inputActions?.Enable();

            if (_fireAction != null)    _fireAction.performed += HandleFire;
            if (_restartAction != null) _restartAction.performed += HandleRestart;
            if (_pauseAction != null)   _pauseAction.performed += HandlePause;
        }

        private void OnDisable()
        {
            if (_fireAction != null)    _fireAction.performed -= HandleFire;
            if (_restartAction != null) _restartAction.performed -= HandleRestart;
            if (_pauseAction != null)   _pauseAction.performed -= HandlePause;

            inputActions?.Disable();
        }

        private void Update()
        {
            if (!_enabled) return;

            ReadMovement();
            ApplyMovement();
        }

        // ── Movement ──────────────────────────────────────────────────────

        private void ReadMovement()
        {
            _moveInput = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
        }

        private void ApplyMovement()
        {
            Vector3 direction = new Vector3(_moveInput.x, 0f, _moveInput.y);

            if (direction.sqrMagnitude > 0.01f)
            {
                // Smooth rotation toward movement direction.
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            // Apply gravity and movement via CharacterController.
            Vector3 velocity = direction * moveSpeed;
            if (!_charController.isGrounded) velocity.y += Physics.gravity.y;

            _charController.Move(velocity * Time.deltaTime);
        }

        // ── Action Handlers ───────────────────────────────────────────────

        private void HandleFire(InputAction.CallbackContext ctx)    => OnFire?.Invoke();
        private void HandleRestart(InputAction.CallbackContext ctx) => OnRestart?.Invoke();
        private void HandlePause(InputAction.CallbackContext ctx)   => OnPause?.Invoke();

        // ── Control ───────────────────────────────────────────────────────

        /// <summary>Enable or disable player input (e.g., during cutscenes).</summary>
        public void SetInputEnabled(bool enabled)
        {
            _enabled = enabled;
            if (enabled) inputActions?.Enable();
            else         inputActions?.Disable();
        }
    }
}
