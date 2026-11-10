using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Bodies;
using Game.Core.Sim;

namespace Game.Core.Modules.Control
{
    /// <summary>What a player wants his dog to do this tick.</summary>
    public readonly struct Intent
    {
        public readonly Vec2 Velocity; // desired velocity, m/s, up to control.max_speed_mps
        public readonly bool DogAbility, OwnerAbility;

        public Intent(Vec2 velocity, bool dogAbility, bool ownerAbility)
        {
            Velocity = velocity;
            DogAbility = dogAbility;
            OwnerAbility = ownerAbility;
        }
    }

    /// <summary>Both mouse modes live here. Cursor: the dog runs to the point on the arena,
    /// slowing down as it arrives. Push: the mouse shift of this tick is the velocity,
    /// up to the ceiling. The mode is a bit of the input, so replays and bots see the same thing.
    /// The dog's body is registered by Dogs (<see cref="Bind"/>); breed limits are Dogs' business.</summary>
    public sealed class ControlModule : IModule
    {
        public const string ModuleName = "Control";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[] { BodiesModule.ModuleName };

        private readonly Fixed _maxSpeed, _cursorGain, _deadzone, _pushGain;
        private readonly Intent[] _intents;
        private readonly int[] _dogBody;

        public ControlModule(GameData data, int players)
        {
            _maxSpeed = data.Num("control.max_speed_mps");
            _cursorGain = data.Num("control.cursor_gain_per_s");
            _deadzone = data.Num("control.cursor_deadzone_m");
            _pushGain = data.Num("control.push_gain");
            _intents = new Intent[players];
            _dogBody = new int[players];
            for (int i = 0; i < players; i++) _dogBody[i] = -1;
        }

        private ControlModule(ControlModule src)
        {
            _maxSpeed = src._maxSpeed;
            _cursorGain = src._cursorGain;
            _deadzone = src._deadzone;
            _pushGain = src._pushGain;
            _intents = (Intent[])src._intents.Clone();
            _dogBody = (int[])src._dogBody.Clone();
        }

        /// <summary>The player's dog body: where cursor mode measures from.</summary>
        public void Bind(int player, int dogBody) => _dogBody[player] = dogBody;

        public Intent Of(int player) => _intents[player];

        public void Init(Simulation sim) { }

        public void Step(Simulation sim, TickInputs inputs)
        {
            var bodies = sim.Need<BodiesModule>(this, BodiesModule.ModuleName);
            for (int p = 0; p < _intents.Length; p++)
            {
                var input = inputs[p];
                var aim = new Vec2(Fixed.Ratio(input.AimX, 100), Fixed.Ratio(input.AimY, 100)); // cm → m
                Vec2 velocity;
                if (input.Push)
                    velocity = aim * (_pushGain * Simulation.TicksPerSecond);
                else if (_dogBody[p] < 0 || input.Equals(PlayerInput.None))
                    velocity = Vec2.Zero; // no cursor yet: stand still
                else
                {
                    var toTarget = aim - bodies.Position(_dogBody[p]);
                    var dist = toTarget.Length;
                    velocity = dist <= _deadzone ? Vec2.Zero : toTarget.Normalized() * (dist * _cursorGain);
                }
                _intents[p] = new Intent(velocity.ClampLength(_maxSpeed), input.DogAbility, input.OwnerAbility);
            }
        }

        public void Hash(ref StateHasher h)
        {
            foreach (var i in _intents) { h.Add(i.Velocity); h.Add(i.DogAbility); h.Add(i.OwnerAbility); }
        }

        public IModule Clone() => new ControlModule(this);
    }
}
