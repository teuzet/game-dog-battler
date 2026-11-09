using Game.Core.Sim;
using UnityEngine;

namespace Factory.Shell
{
    /// <summary>Keyboard → PlayerInput. WASD/arrows are the stick, Space/Shift/E/Q buttons 0–3, R restarts.</summary>
    public static class InputReader
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        public static PlayerInput Read()
        {
            int x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            int y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            uint b = 0;
            if (Input.GetKey(KeyCode.Space)) b |= 1u;
            if (Input.GetKey(KeyCode.LeftShift)) b |= 2u;
            if (Input.GetKey(KeyCode.E)) b |= 4u;
            if (Input.GetKey(KeyCode.Q)) b |= 8u;
            return new PlayerInput(x, y, b);
        }

        public static bool RestartPressed() => Input.GetKeyDown(KeyCode.R);
#elif ENABLE_INPUT_SYSTEM
        public static PlayerInput Read()
        {
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k == null) return PlayerInput.None;
            int x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0);
            int y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0);
            uint b = 0;
            if (k.spaceKey.isPressed) b |= 1u;
            if (k.leftShiftKey.isPressed) b |= 2u;
            if (k.eKey.isPressed) b |= 4u;
            if (k.qKey.isPressed) b |= 8u;
            return new PlayerInput(x, y, b);
        }

        public static bool RestartPressed()
        {
            var k = UnityEngine.InputSystem.Keyboard.current;
            return k != null && k.rKey.wasPressedThisFrame;
        }
#else
        public static PlayerInput Read() => PlayerInput.None;
        public static bool RestartPressed() => false;
#endif
    }
}
