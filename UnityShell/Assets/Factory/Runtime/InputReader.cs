using Game.Core.Sim;
using UnityEngine;

namespace Factory.Shell
{
    /// <summary>Mouse → PlayerInput. Tab switches push / cursor mode, LMB or Q is the dog's ability,
    /// RMB or E the owner's (design/overview.md → «Кнопки»), R restarts. <see cref="Frame"/> is called
    /// once per rendered frame, <see cref="Read"/> once per tick.</summary>
    public static class InputReader
    {
        /// <summary>Arena centimetres per unit of raw mouse movement in push mode: the player's setting,
        /// changeable without a rebuild.</summary>
        public static float CmPerMouseUnit { get; set; } = 1f;

        private static readonly MouseInput Mouse = new MouseInput();

        /// <summary>Set by the view: the point of the ground under the mouse cursor, in metres
        /// (the view owns the camera, so the ray cast is its business).</summary>
        public static System.Func<Vector2> CursorGround;

        /// <summary>A script instead of the mouse (screenshot runs); null in normal play.</summary>
        public static System.Func<PlayerInput> Scripted;

        public static bool PushMode => Scripted != null || Mouse.Push;

        /// <summary>The mouse mode picked on the pre-match screen (Tab still switches it later).</summary>
        public static void SetPushMode(bool push) => Mouse.SetPush(push);

#if ENABLE_LEGACY_INPUT_MANAGER
        public static void Frame()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) Mouse.ToggleMode();
            Mouse.Accumulate(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * CmPerMouseUnit);
        }

        public static PlayerInput Read()
        {
            if (Scripted != null) return Scripted();
            var ground = Vector2.zero;
            if (!Mouse.Push) ground = CursorGround() * 100f; // m → cm
            bool dog = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Q);
            bool owner = Input.GetMouseButton(1) || Input.GetKey(KeyCode.E);
            return Mouse.Take(ground, dog, owner);
        }

        public static bool RestartPressed() => Input.GetKeyDown(KeyCode.R);
        public static bool BackPressed() => Input.GetKeyDown(KeyCode.Escape);
#else
        // The shell reads the legacy Input Manager only; a dead input must not be silent.
        public static void Frame() => throw new System.NotSupportedException("InputReader needs the legacy Input Manager (Active Input Handling: Input Manager or Both)");
        public static PlayerInput Read() => throw new System.NotSupportedException("InputReader needs the legacy Input Manager");
        public static bool RestartPressed() => throw new System.NotSupportedException("InputReader needs the legacy Input Manager");
        public static bool BackPressed() => throw new System.NotSupportedException("InputReader needs the legacy Input Manager");
#endif
    }
}
