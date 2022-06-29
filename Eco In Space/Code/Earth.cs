namespace EcoInSpace
{
    using Eco.Shared.Math;

    public sealed class Earth : Planet
    {
        public Earth() : base("Earth", true)
        {
        }

        public override bool InBounds(Vector3 position)
        {
            return !Planets.OnExoplanet(position);
        }
    }
}