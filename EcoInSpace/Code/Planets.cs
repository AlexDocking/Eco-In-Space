namespace EcoInSpace
{
    using Eco.Shared.Math;
    using System.Collections.Generic;
    using System.Linq;
    public static class Planets
    {
        public static Earth Earth { get; }
        public static List<Exoplanet> Exoplanets { get; } = new List<Exoplanet>();
        public static bool OnExoplanet(Vector3 pos)
        {
            return Exoplanets.Any(planet => planet.InBounds(pos));
        }
    }
}