namespace Eco
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Eco.Core.Plugins.Interfaces;
    using Eco.Core.Utils;
    using Eco.Core.Utils.Async;
    using Eco.Core.Utils.Threading;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.GameActions;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Objects;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Systems.Chat;
    using Eco.Mods.TechTree;
    using Eco.Shared.Localization;
    using Eco.Shared.Math;
    using Eco.Shared.Networking;
    using Eco.Shared.Services;
    using Eco.Shared.Utils;
    using Eco.World.Blocks;
    using Eco.World.Utils;

    public class OxygenLevelCommand : IChatCommandHandler
    {
        /// <summary>
        /// Chat command for server administrators for repeating a message back to the sender
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Informs you of your oxygen level", "ox", ChatAuthorizationLevel.User)]
        public static void OxygenLevel(User user)
        {
            OxygenManager.SendMessage(user, OxygenManager.UpdateType.Check, true);
            //ChatManager.ServerMessageToPlayer(Localizer.DoStr("Oxygen level: " + OxygenManager.OxygenRemaining(user)), user, category: MessageCategory.InfoBox);
        }
    }
    public class OxygenRoomCheckCommand : IChatCommandHandler
    {
        /// <summary>
        /// Chat command for server administrators for repeating a message back to the sender
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Checks if the room has oxygen", "atm", ChatAuthorizationLevel.User)]
        public static void CheckRoomOxygen(User user)
        {
            OxygenRooms.RoomStatus roomCheckResult = OxygenRooms.CheckRoom(user);
            switch (roomCheckResult)
            {
                case OxygenRooms.RoomStatus.Outside:
                    ChatManager.ServerMessageToPlayer(Localizer.DoStr("No oxygen outside"), user, category: MessageCategory.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.NotHighTier:
                    ChatManager.ServerMessageToPlayer(Localizer.DoStr("This room needs to average at least Tier 3. Oxygen unavailable"), user, category: MessageCategory.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.NoPressure:
                    ChatManager.ServerMessageToPlayer(Localizer.DoStr("This room has holes. Oxygen unavailable"), user, category: MessageCategory.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.NoRefillTank:
                    ChatManager.ServerMessageToPlayer(Localizer.DoStr("No waste filter in the room. Oxygen unavailable"), user, category: MessageCategory.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.RefillTankNoPower:
                    ChatManager.ServerMessageToPlayer(Localizer.DoStr("Waste filter needs a power source"), user, category: MessageCategory.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.Valid:
                    ChatManager.ServerMessageToPlayer(Localizer.DoStr("Room is correct. Oxygen available"), user, category: MessageCategory.InfoBox);
                    break;
            }
        }
    }
    public class OxygenPlugin : IInitializablePlugin, IModKitPlugin
    {
        public string GetStatus() => "Active";

        public void Initialize(TimedTask timer)
        {
            ActionUtil.AddListener(new OxygenRefillGameActionListener());
        }
    }
    public class OxygenManager : IInitializablePlugin, IModKitPlugin
    {
        public string GetStatus() => "Active";
        public void Initialize(TimedTask timer)
        {
            UpdateTicker = PeriodicWorkerFactory.CreateWithInterval(TimeSpan.FromSeconds(1), () =>
            {
                IEnumerable<User> registeredUsers = PersonalOxygen.Keys;
                foreach (User user in registeredUsers)
                {
                    UseOxygenOverTime(user);
                }
            });
            UpdateTicker.Start();
            OxygenRooms.OnOxygenLocationStatusChanged += OnOxygenLocationChanged;
            UserManager.OnUserLoggedIn.Add(OnUserLoggedIn);
            UserManager.OnUserLoggedOut.Add(OnUserLoggedOut);
        }
        public static void OnUserLoggedIn(User user)
        {
            Register(user);
        }
        public static void OnUserLoggedOut(User user)
        {
            Deregister(user);
        }
        private static readonly Dictionary<User, float> PersonalOxygen = new Dictionary<User, float>();
        private static readonly Dictionary<User, DateTime> LastUpdateTime = new Dictionary<User, DateTime>();
        private static readonly Dictionary<User, DateTime> LastMessageTime = new Dictionary<User, DateTime>();
        private static readonly Dictionary<User, int> LastMessagePercent = new Dictionary<User, int>();
        private static readonly Dictionary<User, DateTime> NextMessageTime = new Dictionary<User, DateTime>();
        private static IntervalActionWorker UpdateTicker;
        private static readonly Dictionary<User, Action> OnMovedHandlers = new Dictionary<User, Action>();
        //not implemented
        private static readonly Dictionary<User, bool> NotificationsOn = new Dictionary<User, bool>();
        public enum UpdateType
        {
            Refilled,
            Used,
            Depleted,
            TankRemoved,
            Check,
            ValidRoom,
            DepressurisedRoom,
            Outside,
            NoWasteFilter,
            NoPower,
            NotHighTier
        }
        private static Action MakeOnMoved(User user)
        {
            return () =>
            {
                OxygenRooms.Tick(user);
            };
        }
        private static void OnOxygenLocationChanged(User user, OxygenRooms.RoomStatus previousLocation, OxygenRooms.RoomStatus newLocation)
        {
            if (newLocation == OxygenRooms.RoomStatus.Valid)
            {
                SendMessage(user, UpdateType.ValidRoom);
            }
            else if (newLocation == OxygenRooms.RoomStatus.NotHighTier)
            {
                SendMessage(user, UpdateType.NotHighTier);
            }
            else if (newLocation == OxygenRooms.RoomStatus.NoPressure)
            {
                SendMessage(user, UpdateType.DepressurisedRoom);
            }
            else if (newLocation == OxygenRooms.RoomStatus.Outside)
            {
                SendMessage(user, UpdateType.Outside);
            }
            else if (newLocation == OxygenRooms.RoomStatus.NoRefillTank)
            {
                SendMessage(user, UpdateType.NoWasteFilter);
            }
            else if (newLocation == OxygenRooms.RoomStatus.RefillTankNoPower)
            {
                SendMessage(user, UpdateType.NoPower);
            }
        }
        public static bool Register(User user)
        {
            if (!Registered(user))
            {
                PersonalOxygen.Add(user, 0f);
                LastUpdateTime.Add(user, DateTime.Now);
                LastMessageTime.Add(user, DateTime.Now);
                NextMessageTime.Add(user, DateTime.Now.AddSeconds(SecondsUntilNextMessage(user)));
                NotificationsOn.Add(user, true);
                OxygenRooms.Register(user);
                Action onMovedHandler = MakeOnMoved(user);
                user.OnMoved.Add(onMovedHandler);
                OnMovedHandlers.Add(user, onMovedHandler);
                Log.WriteErrorLineLocStr("Registered " + user.Name);
                return true;
            }
            return false;
        }
        public static void Deregister(User user)
        {
            if (Registered(user))
            {
                PersonalOxygen.Remove(user);
                LastUpdateTime.Remove(user);
                LastMessageTime.Remove(user);
                NextMessageTime.Remove(user);
                NotificationsOn.Remove(user);
                OxygenRooms.Deregister(user);
                user.OnMoved.Remove(OnMovedHandlers[user]);
                OnMovedHandlers.Remove(user);
            }
        }
        public static bool Registered(User user)
        {
            return PersonalOxygen.ContainsKey(user);
        }
        private static void SetOxygen(User user, float amount)
        {
            Register(user);
            amount = Math.Max(0, amount);
            PersonalOxygen[user] = amount;
        }
        public static void RefillOxygen(User user)
        {
            SetOxygen(user, OxygenBackpack.OxygenTankCapacity(user));
            StatusUpdate(user, UpdateType.Refilled, forceMessage: true);
        }
        public static void RefillOxygen(User user, float amount)
        {
            float totalAmount = Math.Min(OxygenRemaining(user) + amount, OxygenBackpack.OxygenTankCapacity(user));
            SetOxygen(user, totalAmount);
            StatusUpdate(user, UpdateType.Refilled, forceMessage: true);
        }
        public static void EmptyOxygenTank(User user)
        {
            user.MsgLocStr("Oxygen tank removed\nYou will need to refill whilst wearing a backpack");
            SetOxygen(user, 0f);
            LastUpdateTime[user] = DateTime.Now;
            StatusUpdate(user, UpdateType.Depleted, forceMessage: true);
        }
        public static void UseOxygenOverTime(User user, bool forceMessage = false)
        {
            Register(user);
            //only use oxygen when outside or in a depressurised room
            if (!OxygenRooms.OxygenAvailable(user))
            {
                float secondsSinceLastUpdate = SecondsSince(LastUpdateTime[user]);
                float secondsOfOxygenLeft = OxygenRemaining(user);
                bool isAlreadyDepleted = secondsOfOxygenLeft <= 0;
                SetOxygen(user, PersonalOxygen[user] - secondsSinceLastUpdate);
                //if we should have run out at some point since the last update
                if (secondsOfOxygenLeft < secondsSinceLastUpdate)
                {
                    //jump to the time it would have run out
                    LastUpdateTime[user] = LastUpdateTime[user].AddSeconds(secondsOfOxygenLeft);
                    StatusUpdate(user, UpdateType.Depleted, forceMessage: !isAlreadyDepleted || forceMessage);
                }
                secondsOfOxygenLeft = OxygenRemaining(user);
                if (secondsOfOxygenLeft > 0)
                {
                    StatusUpdate(user, UpdateType.Used, forceMessage: forceMessage);
                }
            }
            //whatever happens, the last update is now
            LastUpdateTime[user] = DateTime.Now;
        }
        public static float OxygenRemaining(User user, bool register = true)
        {
            if (register)
            {
                Register(user);
            }
            if (Registered(user))
            {
                return PersonalOxygen[user];
            }
            return 0f;
        }
        public static bool HasOxygen(User user, bool register = true)
        {
            return OxygenRemaining(user) > 0 || OxygenRooms.OxygenAvailable(user);
        }
        public static string FormattedOxygenRemaining(User user, bool register = true)
        {
            float oxygenRemaining = OxygenRemaining(user, register);
            return oxygenRemaining.ToString("F0");
        }
        public static int PercentOxygenRemaining(User user, bool register = true, bool roundUp = true)
        {
            float oxygenRemaining = OxygenRemaining(user, register);
            float oxygenCapacity = OxygenBackpack.OxygenTankCapacity(user);
            if (oxygenCapacity > 0)
            {
                if (roundUp)
                {
                    return (int)Math.Ceiling(oxygenRemaining / oxygenCapacity * 100);
                }
                return (int)(oxygenRemaining / oxygenCapacity * 100);
            }
            return 100;
        }
        public static string FormattedPercentOxygenRemaining(User user, bool register = true)
        {
            float oxygenPercentRemaining = PercentOxygenRemaining(user, register);
            return oxygenPercentRemaining.ToString("F0") + "%";
        }
        private static void StatusUpdate(User user, UpdateType updateType, DateTime? updateTime = null, bool forceMessage = false)
        {
            updateTime ??= DateTime.Now;
            SendMessage(user, updateType, forceMessage);
            if (!HasOxygen(user))
            {
                TryDamage(user, SecondsSince(LastUpdateTime[user], updateTime.Value));
            }
            LastUpdateTime[user] = updateTime.Value;
        }
        private static float SecondsUntilNextMessage(User user)
        {
            float oxygenTankCapacity = OxygenBackpack.OxygenTankCapacity(user);
            float oxygenRemaining = OxygenRemaining(user);
            //if we have no tank, send a message every 20s
            if (oxygenTankCapacity == 0 || oxygenRemaining == 0)
            {
                return 20f;
            }
            float[] messagePercents = new float[] { 90, 80, 70, 60, 50, 40, 30, 20, 10, 5, 4, 3, 2, 1, 0 };
            float currentPercent = oxygenRemaining / oxygenTankCapacity * 100;
            //how many seconds until each of the next percent messages
            float[] timeUntilPercents = messagePercents.Select(percent => 0.01f * (currentPercent - percent) * oxygenTankCapacity).ToArray();

            //if we haven't sent a message in the last 10 seconds, the next one will be when the percentage hits one of the above numbers

            return timeUntilPercents.Where(t => t >= 0 && t != LastMessagePercent[user]).MinOrDefault();
        }
        public static void SendMessage(User user, UpdateType updateType, bool forceMessage = false)
        {
            Register(user);
            switch (updateType)
            {
                case UpdateType.Depleted:
                    if (OxygenRemaining(user) <= 0)
                    {
                        if (forceMessage || SecondsSince(DateTime.Now, NextMessageTime[user]) <= 0)
                        {
                            if (OxygenBackpack.OxygenTankCapacity(user) > 0)
                            {
                                user.MsgLocStr("Oxygen depleted!\nRefill your oxygen tank at a waste filter", MessageCategory.InfoBox);
                            }
                            else
                            {
                                user.MsgLocStr("Oxygen tank not installed! Wear a backpack and refill at a waste filter", MessageCategory.InfoBox);
                            }
                            LastMessageTime[user] = DateTime.Now;
                            LastMessagePercent[user] = PercentOxygenRemaining(user);
                            NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                        }
                    }
                    break;
                case UpdateType.Refilled:
                    if (OxygenRemaining(user) == OxygenBackpack.OxygenTankCapacity(user))
                    {
                        user.MsgLocStr("Oxygen refilled to max:" + FormattedOxygenRemaining(user), MessageCategory.InfoBox);
                        user.MsgLocStr("Oxygen refilled to max:" + FormattedOxygenRemaining(user), MessageCategory.Info);
                    }
                    else
                    {
                        user.MsgLocStr("Oxygen refilled to " + FormattedOxygenRemaining(user) + "/" + OxygenBackpack.FormattedOxygenTankCapacity(user), MessageCategory.InfoBox);
                        user.MsgLocStr("Oxygen refilled to " + FormattedOxygenRemaining(user) + "/" + OxygenBackpack.FormattedOxygenTankCapacity(user), MessageCategory.Info);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.Used:
                    if (forceMessage || SecondsSince(DateTime.Now, NextMessageTime[user]) <= 0)
                    {
                        user.MsgLocStr("Oxygen level: " + FormattedPercentOxygenRemaining(user), MessageCategory.InfoBox);
                        LastMessageTime[user] = DateTime.Now;
                        LastMessagePercent[user] = PercentOxygenRemaining(user);
                        NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    }
                    break;
                case UpdateType.TankRemoved:
                    if (forceMessage || SecondsSince(DateTime.Now, NextMessageTime[user]) <= 0)
                    {
                        user.MsgLocStr("Oxygen tank removed.\nYou will need to refill whilst wearing your backpack", MessageCategory.InfoBox);
                        user.MsgLocStr("Oxygen tank removed. You will need to refill whilst wearing your backpack", MessageCategory.Info);
                        LastMessageTime[user] = DateTime.Now;
                        LastMessagePercent[user] = PercentOxygenRemaining(user);
                        NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    }
                    break;
                case UpdateType.Check:
                    if (OxygenRemaining(user) <= 0)
                    {
                        SendMessage(user, UpdateType.Depleted, forceMessage);
                    }
                    else
                    {
                        SendMessage(user, UpdateType.Used, forceMessage);
                    }
                    break;
                case UpdateType.ValidRoom:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("Pressurised Room.\nOxygen tank off.\n" + FormattedPercentOxygenRemaining(user) + " remaining", MessageCategory.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("Pressurised Room.\nYou can breathe again", MessageCategory.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.DepressurisedRoom:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("Depressurised Room.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", MessageCategory.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("Depressurised Room", MessageCategory.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.NotHighTier:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("Room tier too low.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", MessageCategory.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("Room tier too low", MessageCategory.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.Outside:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("Oxygen tank on. " + FormattedPercentOxygenRemaining(user) + " remaining", MessageCategory.InfoBox);

                        LastMessageTime[user] = DateTime.Now;
                        LastMessagePercent[user] = PercentOxygenRemaining(user);
                        NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    }
                    break;
                case UpdateType.NoWasteFilter:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("No waste filter in the room.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", MessageCategory.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("No waste filter in the room", MessageCategory.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.NoPower:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("Waste filter has no power.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", MessageCategory.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("Waste filter has no power", MessageCategory.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
            }
        }
        private static void TryDamage(User user, float secondsOfDepletion)
        {
            if (OxygenRemaining(user) <= 0)
            {
                user.Stomach.LoseCalories(secondsOfDepletion * 50, true);

                SendMessage(user, UpdateType.Depleted);
            }
        }
        private static float SecondsSince(DateTime pastTime, DateTime? nowTime = null)
        {
            nowTime ??= DateTime.Now;
            return (float)(nowTime.Value - pastTime).TotalSeconds;
    }
    }
    public class OxygenRooms
    {
        private static readonly Dictionary<User, RoomStatus> UsersOxygenLocationStatus = new Dictionary<User, RoomStatus>();
        public static event Action<User, RoomStatus, RoomStatus> OnOxygenLocationStatusChanged;
        public enum RoomStatus
        {
            Valid,
            Outside,
            NoPressure,
            NotHighTier,
            NoRefillTank,
            RefillTankNoPower
        }
        public static void Register(User user)
        {
            UsersOxygenLocationStatus.Add(user, CheckRoom(user));
        }
        public static void Deregister(User user)
        {
            UsersOxygenLocationStatus.Remove(user);
        }
        public static bool OxygenAvailable(User user)
        {
            return CheckRoom(user) == RoomStatus.Valid;
        }
        public static RoomStatus CheckRoom(User user)
        {
            if (user.CurrentRoom.RoomStats == null || user.CurrentRoom.RoomStats.Volume == 0 || !user.CurrentRoom.RoomStats.Contained)
            {
                return RoomStatus.Outside;
            }
            if (user.CurrentRoom.RoomStats.AverageTier < 3)
            {
                return RoomStatus.NotHighTier;
            }
            if (user.CurrentRoom.RoomStats.Windows.Count() > 0)
            {
                return RoomStatus.NoPressure;
            }
            if (user.CurrentRoom.RoomStats.ContainedWorldObjects.None((WorldObject worldObject) => worldObject is WasteFilterObject))
            {
                return RoomStatus.NoRefillTank;
            }
            if (user.CurrentRoom.RoomStats.ContainedWorldObjects.None((WorldObject worldObject) => { return (worldObject is WasteFilterObject) && WasteFilterHasPower(worldObject as WasteFilterObject); }))
            {
                return RoomStatus.RefillTankNoPower;
            }
            return RoomStatus.Valid;
        }
        public static bool WasteFilterHasPower(WasteFilterObject wasteFilter)
        {
            if (wasteFilter == null)
            {
                return false;
            }
            PowerGridComponent grid;
            if (wasteFilter.TryGetComponent<PowerGridComponent>(out grid))
            {
                return grid.PowerGrid.EnergySupply > 0;
            }
            return false;
        }
        public static void Tick(User user)
        {
            RoomStatus currentLocation = UsersOxygenLocationStatus[user];
            RoomStatus newLocation = CheckRoom(user);
            UsersOxygenLocationStatus[user] = newLocation;
            if (currentLocation != newLocation)
            {
                OnOxygenLocationStatusChanged(user, currentLocation, newLocation);
            }
        }
    }
    public class OxygenRefillGameActionListener : IGameActionAware
    {
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
        public void ActionPerformed(GameAction baseAction)
        {
            WorldObjectInteractAction action = baseAction as WorldObjectInteractAction;
            if (action != null)
            {
                if (IsTargetingOxygenRefillStation(baseAction, out WasteFilterObject refillSource))
                {
                    if (refillSource != null)
                    {
                        PowerGridComponent power = refillSource.GetComponent<PowerGridComponent>();
                        if (power != null)
                        {
                            PowerGrid grid = power.PowerGrid;
                            ItemStack carriedItems = action.Citizen.Carrying;
                            if (OxygenBackpack.OxygenTankCapacity(action.Citizen) == 0)
                            {
                                action.Citizen.MsgLocStr("Wear a backpack to refill oxygen", MessageCategory.InfoBox);
                            }
                            else
                            {
                                if (OxygenManager.OxygenRemaining(action.Citizen) == OxygenBackpack.OxygenTankCapacity(action.Citizen))
                                {
                                    action.Citizen.MsgLocStr("Oxygen full", MessageCategory.InfoBox);
                                }
                                else
                                {
                                    if (carriedItems.Item is BarrelItem)
                                    {
                                        if (grid.EnergySupply > 0)
                                        {
                                            if (action.Citizen.Carrying.TryModifyStack(action.Citizen, -1))
                                            {
                                                OxygenManager.RefillOxygen(action.Citizen, 300);
                                            }
                                            else
                                            {
                                                action.Citizen.MsgLocStr("No barrels left", MessageCategory.InfoBox);
                                            }
                                        }

                                        else
                                        {
                                            action.Citizen.MsgLocStr("Connect to a power source", MessageCategory.InfoBox);
                                        }
                                    }
                                    else
                                    {
                                        if (grid.EnergySupply > 0)
                                        {
                                            action.Citizen.MsgLocStr("Carry empty barrels to refill your backpack tank", MessageCategory.InfoBox);
                                        }
                                        else
                                        {
                                            action.Citizen.MsgLocStr("Connect to a power source and carry empty barrels to refill", MessageCategory.InfoBox);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        public Result ShouldOverrideAuth(GameAction action)
        {
            return Result.Succeeded;
        }
    }
    public class OxygenBackpack : IInitializablePlugin, IModKitPlugin
    {
        public string GetStatus() => "Active";

        public void Initialize(TimedTask timer)
        {
            UserManager.OnUserLoggedIn.Add(OnUserLoggedIn);
            UserManager.OnUserLoggedOut.Add(OnUserLoggedOut);
        }
        //public static Dictionary<User, Action<User, IEnumerable<KeyValuePair<System.Type, int>>, Dictionary<ItemStack, ChangedStack>>> OnClothingChangedHandlers = new Dictionary<User, Action<User, IEnumerable<KeyValuePair<System.Type, int>>, Dictionary<ItemStack, ChangedStack>>>();
        public static Dictionary<User, Action<User>> OnClothingChangedHandlers = new Dictionary<User, Action<User>>();
        public static Dictionary<User, Type> WornBackpacks = new Dictionary<User, Type>();
        public static Type CurrentBackpack(User user)
        {
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
                    return backpackTypes[i];
                }
            }
            return null;
        }
        public static string FormattedOxygenTankCapacity(User user)
        {
            float oxygenTankCapacity = OxygenTankCapacity(user);
            return oxygenTankCapacity.ToString("F0") + "L";
        }
        public static float OxygenTankCapacity(User user)
        {
            Type oxygenTankType = OxygenBackpack.CurrentBackpack(user);
            return OxygenTankCapacity(oxygenTankType);
        }
        public static float OxygenTankCapacity(Type oxygenTankType)
        {
            //can't fill when other types are equipped
            //oxygen never goes down
            //cannot detect outside
            //vehicle always arrive but player often teleports many times to reach threshold distance, because they get pushed away by the vehicle
            //vehicle and player often fall through ground before remount
            //always two attempts at remount
            //player never remounts vehicle properly
            Dictionary<Type, float> tankCapacities = new Dictionary<Type, float>()
            {
                { typeof(BasicBackpackItem), 120f },//2 minutes
                { typeof(LightBackpackItem), 300f },//5 minutes
                { typeof(WorkBackpackItem), 600f },//10 minutes
                { typeof(BigBackpackItem), 1200f },//20 minutes
                { typeof(BearpackItem), 1800f },//30 minutes
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
        public static bool WearingBackpack(User user)
        {
            return CurrentBackpack(user) != null;
        }
        public static float MovementSpeedMultiplier(Type oxygenTankType)
        {
            //can't fill when other types are equipped
            //oxygen never goes down
            //cannot detect outside
            //vehicle always arrive but player often teleports many times to reach threshold distance, because they get pushed away by the vehicle
            //vehicle and player often fall through ground before remount
            //always two attempts at remount
            //player never remounts vehicle properly
            Dictionary<Type, float> tankCapacities = new Dictionary<Type, float>()
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
            if (tankCapacities.ContainsKey(oxygenTankType))
            {
                return tankCapacities[oxygenTankType];
            }
            return 1f;
        }
        public static Action<User> MakeOnClothingChanged()
        {
            return (User user) =>
            {
                if (WornBackpacks.ContainsKey(user))
                {
                    Type previousBackpack = WornBackpacks[user];
                    Type currentBackpack = CurrentBackpack(user);
                    if (currentBackpack != previousBackpack)
                    {
                        if (previousBackpack != null)
                        {
                            OxygenManager.EmptyOxygenTank(user);
                        }
                        if (currentBackpack != null)
                        {
                            user.MsgLocStr("Oxygen tank capacity: " + OxygenTankCapacity(currentBackpack), MessageCategory.InfoBox);
                        }
                    }
                    WornBackpacks[user] = currentBackpack;
                    UpdateMovementSpeed(user);
                }
            };
        }
        public static void UpdateMovementSpeed(User user)
        {
            user.ModifiedStats.Stats[UserStatType.MovementSpeed].ModifierClothing = MovementSpeedMultiplier(CurrentBackpack(user));
        }
        public static void OnUserLoggedIn(User user)
        {
            WornBackpacks.Add(user, CurrentBackpack(user));
            UpdateMovementSpeed(user);
            //Action<User, IEnumerable<KeyValuePair<System.Type, int>>, Dictionary<ItemStack, ChangedStack>> onClothingChangedHandler = MakeOnClothingChanged();
            Action<User> onClothingChangedHandler = MakeOnClothingChanged();
            OnClothingChangedHandlers.Add(user, onClothingChangedHandler);
            user.Inventory.Clothing.OnChanged.Add(onClothingChangedHandler);
        }
        public static void OnUserLoggedOut(User user)
        {
            user.Inventory.Clothing.OnChanged.Remove(OnClothingChangedHandlers[user]);
            OnClothingChangedHandlers.Remove(user);
            WornBackpacks.Remove(user);
        }
    }
}