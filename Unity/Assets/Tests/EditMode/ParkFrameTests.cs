using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DogBattler.View;
using Game.Core;
using Game.Core.Bots;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Arena;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Leashes;
using Game.Core.Modules.Match;
using Game.Core.Modules.Owners;
using Game.Core.Modules.Rules;
using Game.Core.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DogBattler.Tests
{
    /// <summary>The park frame (F-034): the match the capture build plays, replayed without a renderer, and the
    /// sheet art/frame-base.json with the numbers of that moment for the artist and the producer.</summary>
    public class ParkFrameTests
    {
        private const float Aspect = 16f / 9f;
        private static string Root() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        private static float F(Fixed v) => (float)v.ToDouble();

        [Test]
        public void ToScreen_puts_the_park_lawn_in_frame_and_its_centre_in_the_middle()
        {
            const float w = 24f, h = 16f;
            var mid = ViewMath.ToScreen(0f, 0f, 0f, w, h, Aspect);
            Assert.AreEqual(0.5f, mid.x, 0.05f);
            Assert.AreEqual(0.5f, mid.y, 0.05f);
            foreach (var c in new[] { new Vector2(-12, -8), new Vector2(12, -8), new Vector2(-12, 8), new Vector2(12, 8) })
            {
                var s = ViewMath.ToScreen(c.x, c.y, 0f, w, h, Aspect);
                Assert.That(s.x, Is.InRange(0f, 1f), $"x of {c}");
                Assert.That(s.y, Is.InRange(0f, 1f), $"y of {c}");
            }
            // sim +Y is away from the camera: it is higher on the screen; sim +X is to the right
            Assert.Greater(ViewMath.ToScreen(0f, 8f, 0f, w, h, Aspect).y, mid.y);
            Assert.Greater(ViewMath.ToScreen(12f, 0f, 0f, w, h, Aspect).x, mid.x);
        }

        [Test]
        public void The_park_frame_meets_its_conditions_and_the_sheet_is_written()
        {
            var data = GameData.Load(Path.Combine(Root(), "Game.Core", "Data"));
            data = data.WithArena(ViewCapture.ParkArena).WithHumanOwner(data.Str("game.human_owner"));
            // the moment is searched (seeds 1..ParkMaxSeed), not kept as a constant tick that bot tuning moves
            var moment = ViewCapture.FindParkMoment(data);
            var sim = moment.Sim;
            var blow = moment.Blow;
            var arena = sim.Get<ArenaModule>(ArenaModule.ModuleName);
            var bodies = sim.Get<BodiesModule>(BodiesModule.ModuleName);
            var dogs = sim.Get<DogsModule>(DogsModule.ModuleName);
            var owners = sim.Get<OwnersModule>(OwnersModule.ModuleName);
            var leashes = sim.Get<LeashesModule>(LeashesModule.ModuleName);
            var hits = sim.Get<HitsModule>(HitsModule.ModuleName);
            var rules = sim.Get<RulesModule>(RulesModule.ModuleName);
            var match = sim.Get<MatchModule>(MatchModule.ModuleName);

            Assert.That(moment.Seed, Is.InRange(1, ViewCapture.ParkMaxSeed));
            var zone = rules.Active.Zone;
            Assert.IsTrue(zone.HasValue, "the circle is on the lawn");
            bool inside = false;
            for (int p = 0; p < sim.Players; p++) inside |= dogs.InZone(sim, p, zone.Value.Center, zone.Value.Radius);
            Assert.IsTrue(inside, "a dog is inside the circle");
            Assert.IsTrue(rules.Active.NextZone.HasValue || zone.HasValue, "the next circle is announced or the circle is visible");
            float left = F(match.TimeLeft);
            Assert.That(left, Is.InRange(30f, 100f), "time left");
            bool anyScore = false;
            foreach (var s in match.Scores) anyScore |= s != 0;
            Assert.IsTrue(anyScore, "not all scores are zero");
            float tension = F(leashes.Tension(sim, blow.Attacker));
            Assert.GreaterOrEqual(Mathf.Max(tension, moment.AttackerTensionBefore), 0.9f, "the attacker's leash is taut");

            float W = F(arena.Width), H = F(arena.Height);
            float strong = F(hits.StrongThreshold);
            float mass = F(bodies.Mass(owners.BodyOf(blow.Attacker)));
            float flash = ViewMath.FlashAmount(F(blow.Strength), mass, strong);
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"arena\": \"park\",\n");
            sb.Append($"  \"seed\": {moment.Seed},\n");
            sb.Append($"  \"tick\": {sim.Tick},\n");
            sb.Append($"  \"time_left_s\": {N(left)},\n");
            sb.Append($"  \"aspect\": \"16:9\",\n");
            sb.Append($"  \"scores\": [{string.Join(", ", match.Scores)}],\n");
            sb.Append($"  \"zone\": {Circle(zone.Value)},\n");
            sb.Append($"  \"zone_screen\": {Pt(ViewMath.ToScreen(F(zone.Value.Center.X), F(zone.Value.Center.Y), 0f, W, H, Aspect))},\n");
            sb.Append($"  \"next_zone\": {(rules.Active.NextZone is ZoneInfo nz ? Circle(nz) : "null")},\n");
            sb.Append($"  \"hit\": {{ \"attacker\": {blow.Attacker}, \"victim\": {blow.Victim}, \"flash\": {N(flash)} }},\n");
            sb.Append("  \"lawn_corners_screen\": [");
            var corners = new[] { new Vector2(-W / 2, -H / 2), new Vector2(W / 2, -H / 2), new Vector2(W / 2, H / 2), new Vector2(-W / 2, H / 2) };
            for (int i = 0; i < 4; i++)
                sb.Append((i > 0 ? ", " : "") + Pt(ViewMath.ToScreen(corners[i].x, corners[i].y, 0f, W, H, Aspect)));
            sb.Append("],\n  \"players\": [\n");
            for (int p = 0; p < sim.Players; p++)
            {
                var dp = dogs.Position(sim, p);
                var op = owners.Position(sim, p);
                float speed = F(bodies.Velocity(owners.BodyOf(p)).Length);
                float air = ViewMath.Airborne(speed, strong);
                float height = ViewMath.OwnerHeight(air);
                float t = F(leashes.Tension(sim, p));
                sb.Append("    {");
                sb.Append($" \"player\": {p}, \"color\": \"{new[] { "red", "blue", "yellow", "purple" }[p]}\",");
                sb.Append($" \"dog\": {{ \"x\": {N(F(dp.X))}, \"y\": {N(F(dp.Y))}, \"screen\": {Pt(ViewMath.ToScreen(F(dp.X), F(dp.Y), 0f, W, H, Aspect))} }},");
                sb.Append($" \"owner\": {{ \"x\": {N(F(op.X))}, \"y\": {N(F(op.Y))}, \"speed_mps\": {N(speed)}, \"airborne\": {N(air)}, \"height_m\": {N(height)}, \"screen\": {Pt(ViewMath.ToScreen(F(op.X), F(op.Y), height, W, H, Aspect))} }},");
                sb.Append($" \"leash\": {{ \"tension\": {N(t)}, \"width_m\": {N(ViewMath.LeashWidth(t))} }} }}");
                sb.Append(p < sim.Players - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            string path = Path.Combine(Root(), "art", "frame-base.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Assert.IsNotNull(Json.Parse(sb.ToString()), "the sheet is valid json");
        }

        private static string N(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        private static string Pt(Vector2 v) => $"[{N(v.x)}, {N(v.y)}]";
        private static string Circle(ZoneInfo z) => $"{{ \"x\": {N(F(z.Center.X))}, \"y\": {N(F(z.Center.Y))}, \"radius\": {N(F(z.Radius))} }}";
    }
}
