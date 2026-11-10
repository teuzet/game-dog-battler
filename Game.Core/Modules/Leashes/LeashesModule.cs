using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Props;
using Game.Core.Sim;

namespace Game.Core.Modules.Leashes
{
    /// <summary>The leashes: one per player between his dog and his owner. Stub of F-001: when
    /// Dogs and Owners have both registered their ends, a plain rope joins the two bodies.
    /// Wrapping around posts, knots and temporary lengths come later.</summary>
    public sealed class LeashesModule : IModule
    {
        public const string ModuleName = "Leashes";
        public string Name => ModuleName;
        public IReadOnlyList<string> DependsOn { get; } = new[] { ArenaModule.ModuleName, BodiesModule.ModuleName, PropsModule.ModuleName };

        private readonly Fixed _stiffness;
        private readonly int[] _dog, _owner, _rope;
        private readonly Fixed[] _length;

        public LeashesModule(GameData data, int players)
        {
            _stiffness = data.Num("leash.stiffness");
            _dog = Filled(players);
            _owner = Filled(players);
            _rope = Filled(players);
            _length = new Fixed[players];
        }

        private LeashesModule(LeashesModule src)
        {
            _stiffness = src._stiffness;
            _dog = (int[])src._dog.Clone();
            _owner = (int[])src._owner.Clone();
            _rope = (int[])src._rope.Clone();
            _length = (Fixed[])src._length.Clone();
        }

        private static int[] Filled(int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = -1;
            return a;
        }

        public int Players => _dog.Length;
        public Fixed LengthOf(int player) => _length[player];
        public int DogBody(int player) => _dog[player];
        public int OwnerBody(int player) => _owner[player];

        public void AttachDog(int player, int body, Fixed length)
        {
            _dog[player] = body;
            _length[player] = length;
        }

        public void AttachOwner(int player, int body) => _owner[player] = body;

        public void Init(Simulation sim)
        {
            var bodies = sim.Need<BodiesModule>(this, BodiesModule.ModuleName);
            for (int p = 0; p < _dog.Length; p++)
                _rope[p] = bodies.AddRope(_dog[p], _owner[p], _length[p], _stiffness);
        }

        public void Step(Simulation sim, TickInputs inputs) { }

        public void Hash(ref StateHasher h)
        {
            for (int p = 0; p < _dog.Length; p++) { h.Add(_dog[p]); h.Add(_owner[p]); h.Add(_rope[p]); h.Add(_length[p]); }
        }

        public IModule Clone() => new LeashesModule(this);
    }
}
