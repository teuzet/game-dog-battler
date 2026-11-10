using UnityEngine;

namespace DogBattler.View
{
    /// <summary>The numbers of the picture, free of scene objects so they can be tested.
    /// Nothing here is game logic: it only turns state into looks.</summary>
    public static class ViewMath
    {
        /// <summary>Player colours by index (art/style.md → «Палитра»): red, blue, yellow, purple.</summary>
        public static readonly Color[] PlayerColors =
        {
            new Color(0.88f, 0.17f, 0.17f),
            new Color(0.18f, 0.41f, 0.92f),
            new Color(0.97f, 0.80f, 0.12f),
            new Color(0.58f, 0.27f, 0.82f),
        };

        public static Color PlayerColor(int player) => PlayerColors[player % PlayerColors.Length];

        /// <summary>The simulation plane XY lies on the ground XZ; <paramref name="height"/> is cosmetic.</summary>
        public static Vector3 ToWorld(float simX, float simY, float height = 0f) => new Vector3(simX, height, simY);

        /// <summary>Where a camera ray meets the ground (y = 0), as sim XY in metres.</summary>
        public static Vector2 GroundPoint(Ray ray)
        {
            if (ray.direction.y >= -1e-4f) throw new System.InvalidOperationException("the camera ray does not go down to the ground");
            float t = -ray.origin.y / ray.direction.y;
            var p = ray.origin + ray.direction * t;
            return new Vector2(p.x, p.z);
        }

        /// <summary>0..1: how fast the owner flies relative to the strong-hit speed (HitsModule.StrongThreshold, m/s).</summary>
        public static float Airborne(float speed, float referenceSpeed) => Mathf.Clamp01(speed / referenceSpeed);

        public const float MaxOwnerHeight = 1.2f;
        public static float OwnerHeight(float airborne) => airborne * MaxOwnerHeight;

        /// <summary>The shadow grows and fades with the owner's speed, so the flight reads from the shadow.</summary>
        public static float ShadowScale(float airborne) => 1f + 0.8f * airborne;
        public static float ShadowAlpha(float airborne) => Mathf.Lerp(0.35f, 0.18f, airborne);

        /// <summary>Leash line: thin and in the player's colour when slack, thick and bright when taut.</summary>
        public static float LeashWidth(float tension) => Mathf.Lerp(0.05f, 0.16f, tension);
        public static Color LeashColor(Color player, float tension) => Color.Lerp(new Color(player.r * 0.7f, player.g * 0.7f, player.b * 0.7f), Color.Lerp(player, Color.white, 0.35f), tension);

        /// <summary>0..1 brightness of a hit flash. Hit.Strength is attacker mass × relSpeed², so the speed is
        /// taken back out of it and compared with the strong threshold (m/s); a strong hit flashes fully.</summary>
        public static float FlashAmount(float strength, float attackerMass, float strongThresholdMps) =>
            Mathf.Clamp01(Mathf.Sqrt(strength / attackerMass) / strongThresholdMps);

        /// <summary>Camera distance along its viewing axis such that the whole w×h arena
        /// (plus a margin, metres) is in frame at the given pitch and vertical field of view.</summary>
        public static float CameraDistance(float width, float height, float pitchDeg, float vFovDeg, float aspect, float margin)
        {
            var rot = Quaternion.Euler(pitchDeg, 0f, 0f);
            var forward = rot * Vector3.forward;
            float hw = width / 2 + margin, hh = height / 2 + margin;
            float tanV = Mathf.Tan(vFovDeg * Mathf.Deg2Rad / 2), tanH = tanV * aspect;
            float need = 0f;
            foreach (var corner in new[] { new Vector3(-hw, 0, -hh), new Vector3(hw, 0, -hh), new Vector3(-hw, 0, hh), new Vector3(hw, 0, hh) })
            {
                // camera at -forward*d looking at the origin: view-space coordinates of the corner
                var right = rot * Vector3.right;
                var up = rot * Vector3.up;
                float x = Vector3.Dot(corner, right), y = Vector3.Dot(corner, up), z = Vector3.Dot(corner, forward);
                // need d + z >= |x| / tanH and d + z >= |y| / tanV
                need = Mathf.Max(need, Mathf.Abs(x) / tanH - z, Mathf.Abs(y) / tanV - z);
            }
            return need;
        }
    }
}
