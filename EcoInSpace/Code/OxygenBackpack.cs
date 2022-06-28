namespace EcoInSpace
{
    using Eco.Core.Plugins.Interfaces;
    using Eco.Core.Utils;
    using Eco.Gameplay.DynamicValues;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Players;
    using Eco.Mods.TechTree;
    using Eco.Shared.Utils;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static class OxygenBackpack
    {
        public static readonly ThreadSafeAction<User, Type, Type> OnBackpackChangedEvent = new ThreadSafeAction<User, Type, Type>();

        //public static Dictionary<User, Action<User, IEnumerable<KeyValuePair<System.Type, int>>, Dictionary<ItemStack, ChangedStack>>> OnClothingChangedHandlers = new Dictionary<User, Action<User, IEnumerable<KeyValuePair<System.Type, int>>, Dictionary<ItemStack, ChangedStack>>>();
        public static Dictionary<User, Action<User>> OnClothingChangedHandlers = new Dictionary<User, Action<User>>();
        public static Dictionary<User, Type> WornBackpacks = new Dictionary<User, Type>();
        public static Type CurrentBackpack(User user)
        {
            ClothingItem backpack = CurrentBackpackItem(user);
            if (backpack == null)
            {
                return null;
            }
            return backpack.GetType();
        }
        public static ClothingItem CurrentBackpackItem(User user)
        {
            if (user.Inventory == null || user.Inventory.Clothing == null || user.Inventory.Clothing.Stacks == null)
            {
                return null;
            }
            Type[] backpackTypes = new Type[] {
                typeof(BasicBackpackItem),
                typeof(BigBackpackItem),
                typeof(LightBackpackItem),
                typeof(WorkBackpackItem),
                typeof(BearpackItem)
            };
            for (int i = 0; i < backpackTypes.Length; i++)
            {
                if (user.Inventory.Clothing.Stacks.Any(stack => stack.Item != null && stack.Item.GetType() == backpackTypes[i]))
                {
                    return user.Inventory.Clothing.Stacks.First(stack => stack.Item != null && stack.Item.GetType() == backpackTypes[i]).Item as ClothingItem;
                }
            }
            return null;
        }
        /// <summary>
        /// Litres as integer followed by "L" for litres, e.g. 103L
        /// </summary>
        /// <param name="user"></param>
        /// <returns></returns>
        public static string FormattedOxygenTankCapacity(User user)
        {
            float oxygenTankCapacity = OxygenTankCapacity(user);
            return oxygenTankCapacity.ToString("F0") + "L";
        }
        public static void Initialize()
        {
            UserManager.OnNewUserJoined.Add(OnUserLoggedIn);
            UserManager.OnUserInit.Add(OnUserLoggedIn);
            UserManager.OnUserLoggedIn.Add(OnUserLoggedIn);
            UserManager.OnUserLoggedOut.Add(OnUserLoggedOut);
        }
        /// <summary>
        /// Makes the personalised function i.e stores a reference to the player, so every player has their own unique version.
        /// It is the callback for when a player changes their clothes, where we can check whether the backpack type got changed or removed
        /// </summary>
        /// <returns></returns>
        public static Action<User> MakeOnClothingChanged()
        {
            return (User user) =>
            {
                if (user == null || !WornBackpacks.ContainsKey(user))
                {
                    return;
                }
                Type previousBackpack = WornBackpacks[user];
                Type currentBackpack = CurrentBackpack(user);
                if (currentBackpack == previousBackpack)
                {
                    return;
                }
                WornBackpacks[user] = currentBackpack;
                UpdateMovementSpeed(user);
                OnBackpackChangedEvent.Invoke(user, previousBackpack, currentBackpack);
            };
        }
        public static float MovementSpeedMultiplier(Type oxygenTankType)
        {
            Dictionary<Type, float> speedMultpliers = new Dictionary<Type, float>()
            {
                { typeof(BasicBackpackItem), 0.75f },
                { typeof(LightBackpackItem), 0.7f },
                { typeof(WorkBackpackItem), 0.6f },
                { typeof(BigBackpackItem), 0.55f },
                { typeof(BearpackItem), 0.5f },
            };
            if (oxygenTankType == null)
            {
                return 1f;
            }
            if (speedMultpliers.ContainsKey(oxygenTankType))
            {
                return speedMultpliers[oxygenTankType];
            }
            return 1f;
        }
        /// <summary>
        /// The game was crashing when joining as a new user, perhaps because their clothing didn't exist until they finished editing their avatar
        /// Now we subscribe to all the user joining events and hopefully one of them will work
        /// </summary>
        /// <param name="user"></param>
        public static void OnUserLoggedIn(User user)
        {
            if (user != null && !Registered(user) && HasClothingInventory(user))
            {
                WornBackpacks.Add(user, CurrentBackpack(user));
                UpdateMovementSpeed(user);
                //Action<User, IEnumerable<KeyValuePair<System.Type, int>>, Dictionary<ItemStack, ChangedStack>> onClothingChangedHandler = MakeOnClothingChanged();
                Action<User> onClothingChangedHandler = MakeOnClothingChanged();
                OnClothingChangedHandlers.Add(user, onClothingChangedHandler);
                user.Inventory.Clothing.OnChanged.Add(onClothingChangedHandler);
            }
        }
        public static void OnUserLoggedOut(User user)
        {
            if (user != null)
            {
                if (HasClothingInventory(user))
                {
                    user.Inventory.Clothing.OnChanged.Remove(OnClothingChangedHandlers[user]);
                }
                if (OnClothingChangedHandlers.ContainsKey(user))
                {
                    OnClothingChangedHandlers.Remove(user);
                }
                WornBackpacks.Remove(user);
            }
        }
        public static float OxygenTankCapacity(User user)
        {
            Type oxygenTankType = OxygenBackpack.CurrentBackpack(user);
            return OxygenTankCapacity(oxygenTankType);
        }
        public static float OxygenTankCapacity(Type oxygenTankType)
        {
            Dictionary<Type, float> tankCapacities = new Dictionary<Type, float>()
            {
                { typeof(BasicBackpackItem), 600f },//10 minutes
                { typeof(LightBackpackItem), 1200f },//20 minutes
                { typeof(WorkBackpackItem), 1200f },//20 minutes
                { typeof(BigBackpackItem), 1800f },//30 minutes
                { typeof(BearpackItem), 3600f },//60 minutes
            };
            if (oxygenTankType == null)
            {
                return 0f;
            }
            if (tankCapacities.ContainsKey(oxygenTankType))
            {
                return tankCapacities[oxygenTankType];
            }
            return 0f;
        }
        public static bool Registered(User user)
        {
            return WornBackpacks.ContainsKey(user);
        }

        //Doesn't work
        public static void UpdateMovementSpeed(User user)
        {
            if (user != null)
            {
                user.ModifiedStats.Stats[UserStatType.MovementSpeed].ModifierClothing = MovementSpeedMultiplier(CurrentBackpack(user));
                //user.ModifiedStats.Stats[UserStatType.MovementSpeed].ModifierSkill = new MultiDynamicValue(MultiDynamicOps.Multiply, user.ModifiedStats.Stats[UserStatType.MovementSpeed].ModifierSkill, new BackpackMovementSpeedDynamicValue(user));
                //CurrentBackpackItem(user).GetFlatStats()[UserStatType.MovementSpeed] = MovementSpeedMultiplier(CurrentBackpack(user));
                user.ModifiedStats.UpdateClothingStats();
            }
        }
        public static bool WearingBackpack(User user)
        {
            return CurrentBackpack(user) != null;
        }
        private static bool HasClothingInventory(User user)
        {
            return user != null && user.Inventory != null && user.Inventory.Clothing != null;
        }
    }
}