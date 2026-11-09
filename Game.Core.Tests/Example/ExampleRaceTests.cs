// Tests of the template's example game; deleted together with it.
using Game.Core.Modules.Example;
using Game.Core.Tests.Contract;
using Xunit;

namespace Game.Core.Tests.Example
{
    public class ExampleRaceTests
    {
        [Fact]
        public void SomeoneCrossesTheLine()
        {
            var sim = Repo.PlayOut(GameSetup.Game, Repo.Data(), 2);
            var course = sim.Get<Course>(Course.ModuleName);
            Assert.Contains(sim.Get<Runners>(Runners.ModuleName).All, r => r.Pos >= course.Length);
        }

        [Fact]
        public void SensorsSeeOnlyTheScreen()
        {
            var data = Repo.Data();
            var sim = Repo.PlayOut(GameSetup.Game, data, 2, 15 * Game.Core.Sim.Simulation.TicksPerSecond);
            var view = GameSetup.Game.Sense(sim, 0);
            var half = data.Num("game.race.screen_half_width");
            Assert.All(view.Objects, o => Assert.True((o.X - view.X).Abs() <= half));
        }
    }
}
