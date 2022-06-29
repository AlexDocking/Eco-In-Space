namespace EcoInSpace
{
    using Eco.Gameplay.Components;
    using Eco.Gameplay.Objects;
    using Eco.Shared.Utils;

    public static class Power
    {
        public static bool WorldObjectHasPower(WorldObject worldObject)
        {
            if (worldObject == null)
            {
                return false;
            }
            if (worldObject.TryGetComponent(out PowerGridComponent grid))
            {
                if (grid.PowerGrid != null)
                {
                    //Never gets here because the power grid is null
                    Log.WriteErrorLineLocStr("Has PowerGrid");
                    return grid.PowerGrid.EnergySupply > 0;
                }
                //always prints this instead
                Log.WriteErrorLineLocStr("Null PowerGrid");
            }
            return false;
        }
    }
}