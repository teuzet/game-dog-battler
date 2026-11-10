using Game.Core.Sim;
using UnityEngine;

namespace Factory.Shell
{
    /// <summary>The mouse side of PlayerInput without any Unity input calls, so it can be tested.
    /// Push mode: the mouse shift accumulates between ticks and <see cref="Take"/> hands it out in
    /// whole centimetres. Cursor mode: the point of the ground under the cursor, in centimetres.</summary>
    public sealed class MouseInput
    {
        /// <summary>True = push mode (locked cursor), false = cursor mode.</summary>
        public bool Push { get; private set; } = true;

        private Vector2 _pendingCm;

        public void ToggleMode() => SetPush(!Push);

        /// <summary>The player's choice before the match: push (true) or cursor (false).</summary>
        public void SetPush(bool push)
        {
            Push = push;
            _pendingCm = Vector2.zero;
        }

        /// <summary>Add a mouse shift (centimetres of the arena) made since the last call.</summary>
        public void Accumulate(Vector2 deltaCm)
        {
            if (Push) _pendingCm += deltaCm;
        }

        /// <summary>Input for one tick. <paramref name="groundCm"/> is the cursor's ground point in
        /// centimetres from the arena centre; it is used in cursor mode only.</summary>
        public PlayerInput Take(Vector2 groundCm, bool dogAbility, bool ownerAbility)
        {
            uint buttons = 0;
            if (dogAbility) buttons |= PlayerInput.DogAbilityBit;
            if (ownerAbility) buttons |= PlayerInput.OwnerAbilityBit;
            if (!Push) return new PlayerInput(Mathf.RoundToInt(groundCm.x), Mathf.RoundToInt(groundCm.y), buttons);
            int x = Mathf.RoundToInt(_pendingCm.x), y = Mathf.RoundToInt(_pendingCm.y);
            _pendingCm -= new Vector2(x, y); // the fraction stays for the next tick
            return new PlayerInput(x, y, buttons | PlayerInput.PushBit);
        }
    }
}
