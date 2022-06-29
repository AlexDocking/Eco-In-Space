namespace EcoInSpace
{
    using Eco.Shared.Math;

    public interface IBounds
    {
        bool InBounds(Vector3 position);
    }
}