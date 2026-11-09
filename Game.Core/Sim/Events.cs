using System.Collections.Generic;
using Game.Core.Math;

namespace Game.Core.Sim
{
    /// <summary>Something that happened in the session. Modules write events;
    /// the chronicle prints them; the ones that matter to a player carry
    /// pace tags, from which the factory's tool `sim` builds the pace map.</summary>
    public sealed class GameEvent
    {
        public int Tick;
        public int Actor = -1; // player index, -1 = the world
        public string Kind = "";
        public string Text = "";
        // Pace tags (see the factory's tools/sim/SKILL.md). Unset = not this kind.
        public Fixed? Threat; // 0..1: how close the actor is to losing right now
        public Fixed? Reward; // size of what the actor just got
        public string? Novel; // a key naming a kind of situation; repeats are not new
        public int DecisionOptions; // >0: the actor just chose among this many options
    }

    public sealed class EventLog
    {
        public readonly List<GameEvent> All = new List<GameEvent>();
        public int Tick;

        public GameEvent Add(string kind, int actor, string text)
        {
            var e = new GameEvent { Tick = Tick, Kind = kind, Actor = actor, Text = text };
            All.Add(e);
            return e;
        }
    }

    public enum Outcome { Win, Loss, Draw }

    /// <summary>How the session ended, per player, and how close it was.</summary>
    public sealed class SessionResult
    {
        public readonly Outcome[] Outcomes;
        public readonly Fixed Margin; // 0 = razor-close, 1 = blowout
        public readonly string Why;

        public SessionResult(Outcome[] outcomes, Fixed margin, string why)
        {
            Outcomes = outcomes;
            Margin = Fixed.Clamp01(margin);
            Why = why;
        }

        public static SessionResult Draw(int players, string why)
        {
            var o = new Outcome[players];
            for (int i = 0; i < players; i++) o[i] = Outcome.Draw;
            return new SessionResult(o, Fixed.Zero, why);
        }
    }
}
