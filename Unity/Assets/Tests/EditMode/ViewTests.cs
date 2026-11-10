using System.IO;
using DogBattler.View;
using Game.Core;
using Game.Core.Data;
using Game.Core.Math;
using Game.Core.Modules.Bodies;
using Game.Core.Modules.Dogs;
using Game.Core.Modules.Hits;
using Game.Core.Modules.Leashes;
using Game.Core.Modules.Owners;
using Game.Core.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DogBattler.Tests
{
    public class ViewTests
    {
        private static string DataDir() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game.Core", "Data"));

        private static Simulation NewSim(string arena)
        {
            var data = GameData.Load(DataDir());
            data.Get("game").Set("arena", JsonValue.Str(arena));
            return GameSetup.Game.Create(1, data);
        }

        private GameObject _go;
        private ArenaView _view;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private ArenaView Build(Simulation sim)
        {
            _go = new GameObject("view");
            _view = _go.AddComponent<ArenaView>();
            _view.Rebuild(sim);
            return _view;
        }

        [Test]
        public void Player_colours_differ_and_follow_the_index()
        {
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++) Assert.AreNotEqual(ViewMath.PlayerColor(i), ViewMath.PlayerColor(j));
            Assert.AreEqual(ViewMath.PlayerColor(0), ViewMath.PlayerColor(4));
        }

        [Test]
        public void Owner_height_and_shadow_grow_with_speed()
        {
            float slow = ViewMath.Airborne(1f, 9f), fast = ViewMath.Airborne(9f, 9f);
            Assert.Greater(ViewMath.OwnerHeight(fast), ViewMath.OwnerHeight(slow));
            Assert.Greater(ViewMath.ShadowScale(fast), ViewMath.ShadowScale(slow));
            Assert.AreEqual(0f, ViewMath.OwnerHeight(ViewMath.Airborne(0f, 9f)));
            Assert.AreEqual(1f, ViewMath.Airborne(30f, 9f), "capped");
        }

        [Test]
        public void Leash_gets_thicker_and_brighter_when_taut_and_flash_follows_strength()
        {
            var c = ViewMath.PlayerColor(1);
            Assert.Greater(ViewMath.LeashWidth(1f), ViewMath.LeashWidth(0f));
            Assert.Greater(ViewMath.LeashColor(c, 1f).r + ViewMath.LeashColor(c, 1f).g, ViewMath.LeashColor(c, 0f).r + ViewMath.LeashColor(c, 0f).g);
            // strength = mass × v², as the hits module records it
            const float mass = 60f;
            float weak = ViewMath.FlashAmount(mass * 6f * 6f, mass, 9f);
            Assert.Less(weak, 1f);
            Assert.Greater(weak, 0f);
            Assert.AreEqual(1f, ViewMath.FlashAmount(mass * 9f * 9f, mass, 9f), 1e-4f);
            Assert.AreEqual(1f, ViewMath.FlashAmount(mass * 20f * 20f, mass, 9f));
        }

        [Test]
        public void The_camera_is_about_60_degrees_and_keeps_the_whole_arena_in_frame()
        {
            var view = Build(NewSim("park"));
            var cam = view.ViewCamera;
            Assert.AreEqual(60f, cam.transform.eulerAngles.x, 0.01f);
            foreach (var corner in new[] { new Vector3(-12, 0, -8), new Vector3(12, 0, -8), new Vector3(-12, 0, 8), new Vector3(12, 0, 8) })
            {
                var v = cam.WorldToViewportPoint(corner);
                Assert.Greater(v.z, 0f);
                Assert.That(v.x, Is.InRange(0f, 1f));
                Assert.That(v.y, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void The_park_shows_trees_and_benches_and_a_pair_per_player_on_the_ground()
        {
            var sim = NewSim("park");
            var view = Build(sim);
            Assert.AreEqual(4, view.TreeCount);
            Assert.AreEqual(3, view.BenchCount);
            Assert.AreEqual(4, view.DogTransforms.Count);
            Assert.AreEqual(4, view.OwnerTransforms.Count);
            Assert.AreEqual(4, view.Leashes.Count);
            // sim (-6, -4) is the dog of player 0: ground X, Z, no height
            Assert.AreEqual(-6f, view.DogTransforms[0].position.x, 1e-3f);
            Assert.AreEqual(-4f, view.DogTransforms[0].position.z, 1e-3f);
            Assert.AreEqual(0f, view.DogTransforms[0].position.y, 1e-3f);
        }

        [Test]
        public void Collar_and_owner_accent_carry_the_player_colour()
        {
            var view = Build(NewSim("sandbox"));
            for (int p = 0; p < 4; p++)
            {
                var collar = view.DogTransforms[p].Find("Facing/Collar").GetComponent<Renderer>().sharedMaterial.color;
                var accent = view.OwnerTransforms[p].Find("Accent").GetComponent<Renderer>().sharedMaterial.color;
                Assert.AreEqual(ViewMath.PlayerColor(p), collar);
                Assert.AreEqual(ViewMath.PlayerColor(p), accent);
            }
        }

        [Test]
        public void Leash_is_a_line_between_the_dog_and_the_owner()
        {
            var view = Build(NewSim("sandbox"));
            var line = view.Leashes[0];
            var dog = view.DogTransforms[0].position;
            var owner = view.OwnerTransforms[0].position;
            // sandbox spawn: dog (-6,-4), owner (-8,-5.5): the bar runs between them and has their distance as length
            Assert.AreEqual((dog.x + owner.x) / 2, line.position.x, 1e-3f);
            Assert.AreEqual((dog.z + owner.z) / 2, line.position.z, 1e-3f);
            var d = new Vector2(dog.x - owner.x, dog.z - owner.z).magnitude;
            Assert.Greater(line.localScale.z, d * 0.99f);
        }

        [Test]
        public void Positions_are_interpolated_between_ticks()
        {
            var sim = NewSim("sandbox");
            var view = Build(sim);
            var inputs = new TickInputs(sim.Players);
            // a cursor far from the dog: it starts running, so two consecutive ticks differ
            inputs[0] = new PlayerInput(600, -400);
            for (int i = 0; i < 5; i++) { sim.Step(inputs); view.Take(sim); }
            view.Render(0f, 0f);
            float a = view.DogTransforms[0].position.x;
            view.Render(1f, 0f);
            float b = view.DogTransforms[0].position.x;
            view.Render(0.5f, 0f);
            float mid = view.DogTransforms[0].position.x;
            Assert.AreNotEqual(a, b);
            Assert.AreEqual((a + b) / 2, mid, 1e-3f);
        }

        [Test]
        public void The_scripted_swing_pulls_the_leash_taut_and_lifts_the_owner_inside_the_arena()
        {
            var sim = NewSim("sandbox");
            var inputs = new TickInputs(sim.Players);
            while (sim.Tick < ViewCapture.SwingTick)
            {
                inputs[0] = ViewCapture.SwingInput(sim.Tick);
                for (int p = 1; p < sim.Players; p++) inputs[p] = PlayerInput.None;
                sim.Step(inputs);
            }
            var bodies = sim.Get<BodiesModule>(BodiesModule.ModuleName);
            var owners = sim.Get<OwnersModule>(OwnersModule.ModuleName);
            var dogs = sim.Get<DogsModule>(DogsModule.ModuleName);
            var leashes = sim.Get<LeashesModule>(LeashesModule.ModuleName);
            var hits = sim.Get<HitsModule>(HitsModule.ModuleName);
            Assert.GreaterOrEqual((float)leashes.Tension(sim, 0).ToDouble(), 0.95f);
            float speed = (float)bodies.Velocity(owners.BodyOf(0)).Length.ToDouble();
            Assert.GreaterOrEqual(speed, 3f);
            Assert.Greater(ViewMath.OwnerHeight(ViewMath.Airborne(speed, (float)hits.StrongThreshold.ToDouble())), 0f);
            foreach (var pos in new[] { bodies.Position(dogs.BodyOf(0)), bodies.Position(owners.BodyOf(0)) })
            {
                Assert.That((float)pos.X.ToDouble(), Is.InRange(-12f, 12f));
                Assert.That((float)pos.Y.ToDouble(), Is.InRange(-8f, 8f));
            }
        }

        [Test]
        public void A_hit_flashes_the_victim_by_strength_and_the_flash_fades()
        {
            var sim = NewSim("sandbox");
            var view = Build(sim);
            var hits = sim.Get<HitsModule>(HitsModule.ModuleName);
            var mass = sim.Get<BodiesModule>(BodiesModule.ModuleName).Mass(sim.Get<OwnersModule>(OwnersModule.ModuleName).BodyOf(0));
            var weakV = (hits.Threshold + hits.StrongThreshold) / Fixed.FromInt(2); // between the two thresholds
            hits.Record(new Hit(0, 1, mass * weakV * weakV, false));
            hits.Record(new Hit(0, 2, mass * hits.StrongThreshold * hits.StrongThreshold, true));
            view.Take(sim);
            Assert.Less(view.FlashOf(1), 1f);
            Assert.Greater(view.FlashOf(2), view.FlashOf(1));
            Assert.Greater(view.FlashOf(1), 0f);
            Assert.AreEqual(0f, view.FlashOf(3));
            view.Render(0f, 1f);
            Assert.AreEqual(0f, view.FlashOf(2));
        }
    }
}
