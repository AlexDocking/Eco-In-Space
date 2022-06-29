namespace EcoInSpace
{
    using Eco.Shared.Math;

    public class Exoplanet : Planet
    {
        public Exoplanet(string name, bool oxygenAvailable) : base(name, oxygenAvailable)
        {
        }

        public MultiBounds<Bounds2D> Bounds { get; } = new MultiBounds<Bounds2D>();
        public override bool InBounds(Vector3 position)
        {
            return Bounds.InBounds(position);
        }
    }
}