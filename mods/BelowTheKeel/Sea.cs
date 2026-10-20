using UnityEngine;

namespace BelowTheKeel
{
    /// <summary>
    /// Is the ship out on the deep, open sea? Read from the world generator rather than the loaded terrain, so it gives the same answer
    /// on every player's machine and doesn't depend on which zones are loaded.
    /// </summary>
    internal static class Sea
    {
        private const int RingPoints = 16;

        /// <summary>
        /// How far one can see through the fog now (metres): where the game's fog has all but swallowed things. The fog follows the
        /// weather, which the server syncs, so it is near enough the same for the whole crew.
        /// </summary>
        internal static float SightDistance()
        {
            float d = Mathf.Max(RenderSettings.fogDensity, 1e-4f);
            switch (RenderSettings.fogMode)
            {
                case FogMode.Linear: return RenderSettings.fogEndDistance;
                case FogMode.ExponentialSquared: return 1.87f / d;   // exp(-(d x)^2) = 3%
                default: return 3.5f / d;                            // exp(-d x) = 3%
            }
        }

        internal static float WaterLevel => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

        /// <summary>How deep the sea is under a point (metres; negative over land).</summary>
        internal static float Depth(Vector3 p) => WaterLevel - WorldGenerator.instance.GetHeight(p.x, p.z);

        /// <summary>The ship is somewhere an omen may come; otherwise why not (for the status command).</summary>
        internal static bool IsDeep(Vector3 p, out string why)
        {
            why = null;
            if (WorldGenerator.instance == null) { why = "no world"; return false; }

            if (WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.Ocean) { why = "not the Ocean biome"; return false; }

            float depth = Depth(p);
            if (depth < Plugin.MinDepth.Value) { why = $"only {depth:0} m deep (needs {Plugin.MinDepth.Value:0})"; return false; }

            // Two rings round the ship: any point above the water is land too close.
            float far = Plugin.LandDistance.Value;
            for (int ring = 1; ring <= 2 && far > 0f; ring++)
            {
                float r = far * ring / 2f;
                for (int i = 0; i < RingPoints; i++)
                {
                    float a = i * Mathf.PI * 2f / RingPoints;
                    if (Depth(p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)) <= 0f)
                    {
                        why = $"land within {r:0} m";
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
