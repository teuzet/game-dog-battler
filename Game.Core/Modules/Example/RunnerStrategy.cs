using Game.Core.Bots;
using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Modules.Example
{
    /// <summary>Sprints while there is stamina, rests down to a third; takes
    /// the risky upper lane when behind or fresh (param risk: 0 never, 1
    /// always); empties the tank when the finish is on screen.</summary>
    public sealed class RunnerStrategy : IBotStrategy
    {
        private readonly Fixed _risk;
        private bool _resting;

        public RunnerStrategy(Fixed risk) { _risk = risk; }

        public PlayerInput Decide(BotView v, ref DetRandom rng)
        {
            Fixed stamina = v.HudOr("stamina", Fixed.FromInt(100));
            bool finish = false, behind = false, forkNear = false;
            foreach (var o in v.Objects)
            {
                if (o.Kind == "finish") finish = true;
                if (o.Kind == "runner" && o.X > v.X) behind = true;
                if (o.Kind == "fork" && o.X > v.X && o.X - v.X < Fixed.FromInt(4)) forkNear = true;
            }
            if (stamina < Fixed.FromInt(15)) _resting = true;
            if (stamina > Fixed.FromInt(60)) _resting = false;
            int x = finish && stamina > Fixed.FromInt(10) ? 1 : _resting ? -1 : stamina > Fixed.FromInt(35) ? 1 : 0;
            bool upper = _risk >= Fixed.One || (_risk > Fixed.Zero && (behind || stamina > Fixed.FromInt(70)) && rng.Chance(_risk + Fixed.Half));
            return new PlayerInput(x, forkNear && upper ? 1 : -1);
        }

        public IBotStrategy Clone() => new RunnerStrategy(_risk) { _resting = _resting };
    }
}
