namespace EcoInSpace
{
    using Eco.Shared.Math;

    public abstract class Planet : IBounds
    {
        public string Name { get; set; }
        public bool OxygenAvailable { get; set; }
        protected Planet(string name, bool oxygenAvailable)
        {
            Name = name;
            OxygenAvailable = oxygenAvailable;
        }
        public abstract bool InBounds(Vector3 position);
    }
}