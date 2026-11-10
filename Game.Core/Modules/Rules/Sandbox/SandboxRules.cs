using Game.Core.Math;
using Game.Core.Sim;

namespace Game.Core.Modules.Rules.Sandbox
{
    /// <summary>The swing sandbox: no points, no zone. Mannequins and the swing test bench
    /// are added by the sandbox feature.</summary>
    public sealed class SandboxRules : IRuleSet
    {
        public string Name => "Sandbox";
        public ZoneInfo? Zone => null;
        public void Step(Simulation sim, RulesModule host, TickInputs inputs) { }
        public void Hash(ref StateHasher h) { }
        public IRuleSet Clone() => this; // no state
    }
}
