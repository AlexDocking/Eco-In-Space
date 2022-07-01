namespace EcoInSpace
{
    using Eco.Core.Utils;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.GameActions;
    using Eco.Gameplay.Items;
    using Eco.Mods.TechTree;
    using System;

    public class OxygenRefillGameActionListener : IGameActionAware
    {
        public static float OxygenLitresPerBarrel { get; set; } = 300;
        public OxygenRefillGameActionListener()
        { }
        public void ActionPerformed(GameAction baseAction)
        {
            if (baseAction == null)
            {
                return;
            }
            WorldObjectInteractAction action = baseAction as WorldObjectInteractAction;
            if (action == null)
            {
                return;
            }
            if (action.Citizen == null)
            {
                return;
            }
            if (!action.Citizen.LoggedIn)
            {
                return;
            }
            if (!IsTargetingOxygenRefillStation(baseAction, out WasteFilterObject refillSource))
            {
                return;
            }
            if (refillSource == null)
            {
                return;
            }

            PowerGridComponent power = refillSource.GetComponent<PowerGridComponent>();
            if (power == null)
            {
                return;
            }
            PowerGrid grid = power.PowerGrid;
            if (grid == null)
            {
                return;
            }
            float supplyRequired = 250f;
            float energyAvailable = grid.EnergySupply - grid.EnergyDemand;
            if (energyAvailable < supplyRequired)
            {
                MessageManager.SendMessage(action.Citizen, OxygenTankRefillMessageHandler.NEEDS_MORE_POWER, supplyRequired);
                return;
            }
            ItemStack carriedItems = action.Citizen.Carrying;
            if (carriedItems == null)
            {
                return;
            }
            if (!OxygenBackpack.WearingBackpack(action.Citizen))
            {
                MessageManager.SendMessage(action.Citizen, OxygenTankRefillMessageHandler.NEEDS_BACKPACK);
                //action.Citizen.MsgLocStr("Wear a backpack to refill oxygen", NotificationStyle.InfoBox);
                return;
            }
            if (OxygenManager.OxygenRemaining(action.Citizen) == OxygenBackpack.OxygenTankCapacity(action.Citizen))
            {
                MessageManager.SendMessage(action.Citizen, OxygenTankRefillMessageHandler.OXYGEN_FULL);
                //action.Citizen.MsgLocStr("Oxygen full", NotificationStyle.InfoBox);
                return;
            }
            if (!(carriedItems.Item is BarrelItem))
            {
                if (grid.EnergySupply > 0)
                {
                    MessageManager.SendMessage(action.Citizen, OxygenTankRefillMessageHandler.NEEDS_BARREL);

                    //action.Citizen.MsgLocStr("Carry empty barrels to refill your backpack tank", NotificationStyle.InfoBox);
                }
                else
                {
                    MessageManager.SendMessage(action.Citizen, OxygenTankRefillMessageHandler.NEEDS_BARREL_AND_POWER);

                    //action.Citizen.MsgLocStr("Connect to a power source and carry empty barrels to refill", NotificationStyle.InfoBox);
                }
                return;
            }
            if (grid.EnergySupply <= 0)
            {
                MessageManager.SendMessage(action.Citizen, OxygenTankRefillMessageHandler.NEEDS_POWER_CONNECTION);

                //action.Citizen.MsgLocStr("Connect to a power source", NotificationStyle.InfoBox);
                return;
            }

            //Try to use as many barrels as we can without overflowing and wasting any, but use at least one
            int maxBarrelsToApply = (int)Math.Max(1, Math.Floor((OxygenBackpack.OxygenTankCapacity(action.Citizen) - OxygenManager.OxygenRemaining(action.Citizen)) / OxygenLitresPerBarrel));
            int heldBarrels = carriedItems.Quantity;
            if (heldBarrels == 0)
            {
                MessageManager.SendMessage(action.Citizen, OxygenTankRefillMessageHandler.NEEDS_BARREL);
                return;
            }
            int numBarrelsUsed = Math.Min(maxBarrelsToApply, heldBarrels);
            carriedItems.TryModifyStack(action.Citizen, -numBarrelsUsed);

            OxygenManager.RefillOxygen(action.Citizen, OxygenLitresPerBarrel * numBarrelsUsed);
            MessageManager.SendMessage(action.Citizen, OxygenTankRefillMessageHandler.REFILL_SUCCESS);
        }
        public LazyResult ShouldOverrideAuth(GameAction action)
        {
            return LazyResult.FailedNoMessage;
        }
        private static bool IsTargetingOxygenRefillStation(GameAction baseAction, out WasteFilterObject oxygenTank)
        {
            WorldObjectInteractAction action = baseAction as WorldObjectInteractAction;
            if (action != null)
            {
                oxygenTank = action.WorldObject as WasteFilterObject;
                return oxygenTank != null;
            }

            oxygenTank = null;
            return false;
        }
    }
}