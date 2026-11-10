using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;

namespace Game.Core.Modules.Dogs.Breeds
{
    /// <summary>A breed: numbers from Data/dogs/&lt;name&gt;.json plus (in subclasses) the
    /// behaviour that makes it play differently. Roster table: design/roster.md.</summary>
    public abstract class Breed
    {
        public string Name { get; }
        public Fixed Speed { get; }
        public Fixed AccelTime { get; }
        public Fixed Mass { get; }
        public Fixed Radius { get; }
        public Fixed Friction { get; }
        public Fixed LeashLength { get; }
        /// <summary>Passive of the collie: a change of direction turns the velocity at once, no skid.</summary>
        public bool InstantTurn { get; }

        protected Breed(string name, GameData data)
        {
            Name = name;
            var j = data.Get("dogs." + name);
            Speed = j["speed"]!.AsFixed();
            AccelTime = j["accel_time_s"]!.AsFixed();
            Mass = j["mass"]!.AsFixed();
            Radius = j["radius"]!.AsFixed();
            Friction = j["friction"]!.AsFixed();
            LeashLength = j["leash_m"]!.AsFixed();
            InstantTurn = j["instant_turn"]!.AsBool();
        }
    }

    public sealed class Collie : Breed
    {
        public Collie(GameData data) : base("collie", data) { }
    }

    /// <summary>One line per breed: name in the json → class.</summary>
    public static class BreedRegistry
    {
        public static Breed Create(string name, GameData data)
        {
            switch (name)
            {
                case "collie": return new Collie(data);
                default: throw new KeyNotFoundException($"no dog breed '{name}' in the registry (Modules/Dogs/Breeds)");
            }
        }
    }
}
