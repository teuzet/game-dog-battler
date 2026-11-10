using DogBattler.View;
using Factory.Shell;
using Game.Core.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DogBattler.Tests
{
    public class InputTests
    {
        [Test]
        public void Push_mode_is_the_default_and_sets_the_push_bit()
        {
            var m = new MouseInput();
            Assert.IsTrue(m.Push);
            Assert.IsTrue(m.Take(Vector2.zero, false, false).Push);
        }

        [Test]
        public void Push_mode_accumulates_the_delta_between_ticks_into_aim()
        {
            var m = new MouseInput();
            m.Accumulate(new Vector2(3, -2)); // two frames in one tick
            m.Accumulate(new Vector2(4, 1));
            var i = m.Take(new Vector2(999, 999), false, false); // the cursor point is ignored
            Assert.AreEqual(7, i.AimX);
            Assert.AreEqual(-1, i.AimY);
            var next = m.Take(Vector2.zero, false, false);
            Assert.AreEqual(0, next.AimX);
            Assert.AreEqual(0, next.AimY);
        }

        [Test]
        public void Push_mode_keeps_the_fraction_for_the_next_tick()
        {
            var m = new MouseInput();
            m.Accumulate(new Vector2(0.6f, 0f));
            Assert.AreEqual(1, m.Take(Vector2.zero, false, false).AimX);
            m.Accumulate(new Vector2(0.6f, 0f)); // 0.6 - 1 + 0.6 = 0.2
            Assert.AreEqual(0, m.Take(Vector2.zero, false, false).AimX);
        }

        [Test]
        public void Cursor_mode_sends_the_ground_point_in_centimetres_and_clears_the_push_bit()
        {
            var m = new MouseInput();
            m.ToggleMode();
            Assert.IsFalse(m.Push);
            m.Accumulate(new Vector2(50, 50)); // a mouse shift means nothing in cursor mode
            var i = m.Take(new Vector2(-350f, 120f), false, false);
            Assert.IsFalse(i.Push);
            Assert.AreEqual(-350, i.AimX);
            Assert.AreEqual(120, i.AimY);
            m.ToggleMode();
            Assert.AreEqual(0, m.Take(Vector2.zero, false, false).AimX, "the shift made in cursor mode is dropped");
        }

        [Test]
        public void Ability_buttons_map_to_the_dog_and_owner_bits_in_both_modes()
        {
            var m = new MouseInput();
            var push = m.Take(Vector2.zero, true, false);
            Assert.IsTrue(push.DogAbility);
            Assert.IsFalse(push.OwnerAbility);
            m.ToggleMode();
            var cursor = m.Take(Vector2.zero, false, true);
            Assert.IsFalse(cursor.DogAbility);
            Assert.IsTrue(cursor.OwnerAbility);
            Assert.AreEqual(PlayerInput.OwnerAbilityBit, cursor.Buttons);
        }

        [Test]
        public void The_ray_hits_the_ground_and_sim_y_is_world_z()
        {
            var p = ViewMath.GroundPoint(new Ray(new Vector3(1, 10, -10), new Vector3(0, -1, 1).normalized));
            Assert.AreEqual(1f, p.x, 1e-4f);
            Assert.AreEqual(0f, p.y, 1e-4f);
            Assert.AreEqual(new Vector3(2, 0.5f, 3), ViewMath.ToWorld(2, 3, 0.5f));
        }
    }
}
