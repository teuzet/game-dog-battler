using System.Collections.Generic;
using Game.Core.Math;

namespace Game.Core.Bots
{
    /// <summary>What a participant can see: objects inside its screen and the
    /// numbers its HUD shows. A bot decides only from this, never from the
    /// simulation itself (honest sensors). The game builds it in IGame.Sense.</summary>
    public sealed class BotView
    {
        public int Tick;
        public int Self;
        public Fixed X, Y;
        public readonly Dictionary<string, Fixed> Hud = new Dictionary<string, Fixed>();
        public readonly List<SeenObject> Objects = new List<SeenObject>();

        public Fixed HudOr(string key, Fixed fallback) => Hud.TryGetValue(key, out var v) ? v : fallback;
    }

    public struct SeenObject
    {
        public string Kind;
        public int Id;
        public Fixed X, Y;
        public Fixed Value;
    }

    /// <summary>A rectangle around the participant: the screen. Sensors keep only what is inside.</summary>
    public readonly struct ScreenRect
    {
        public readonly Fixed HalfWidth, HalfHeight;

        public ScreenRect(Fixed halfWidth, Fixed halfHeight)
        {
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
        }

        public bool Sees(Fixed cx, Fixed cy, Fixed x, Fixed y) =>
            (x - cx).Abs() <= HalfWidth && (y - cy).Abs() <= HalfHeight;
    }
}
