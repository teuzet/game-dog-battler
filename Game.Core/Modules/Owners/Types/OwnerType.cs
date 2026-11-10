using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Math;

namespace Game.Core.Modules.Owners.Types
{
    /// <summary>An owner type: numbers from Data/owners/&lt;name&gt;.json plus (in subclasses)
    /// the behaviour. The owner has no health: nothing here may add one.</summary>
    public abstract class OwnerType
    {
        public string Name { get; }
        public Fixed Mass { get; }
        public Fixed Radius { get; }
        public Fixed Friction { get; }

        protected OwnerType(string name, GameData data)
        {
            Name = name;
            var j = data.Get("owners." + name);
            Mass = j["mass"]!.AsFixed();
            Radius = j["radius"]!.AsFixed();
            Friction = j["friction"]!.AsFixed();
        }
    }

    /// <summary>Teenager in a phone: notices nothing, the reference flail.</summary>
    public sealed class Teen : OwnerType
    {
        public Teen(GameData data) : base("teen", data) { }
    }

    /// <summary>Jock with a shaker: heavy, slippery underfoot.</summary>
    public sealed class Jock : OwnerType
    {
        public Jock(GameData data) : base("jock", data) { }
    }

    /// <summary>One line per type: name in the json → class.</summary>
    public static class OwnerTypeRegistry
    {
        public static OwnerType Create(string name, GameData data)
        {
            switch (name)
            {
                case "teen": return new Teen(data);
                case "jock": return new Jock(data);
                default: throw new KeyNotFoundException($"no owner type '{name}' in the registry (Modules/Owners/Types)");
            }
        }
    }
}
