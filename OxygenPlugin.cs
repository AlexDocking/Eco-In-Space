namespace Eco
{
    using System;
    using System.Collections;
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
    using Eco.Gameplay.Systems.Messaging.Chat.Commands;
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
                    user.MsgLocStr("No oxygen outside", NotificationStyle.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.NotHighTier:
                    user.MsgLocStr("This room needs to average at least Tier 3. Oxygen unavailable", NotificationStyle.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.NoPressure:
                    user.MsgLocStr("This room has holes. Oxygen unavailable", NotificationStyle.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.NoRefillTank:
                    user.MsgLocStr("No waste filter in the room. Oxygen unavailable", NotificationStyle.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.RefillTankNoPower:
                    user.MsgLocStr("Waste filter needs a power source", NotificationStyle.InfoBox);
                    break;
                case OxygenRooms.RoomStatus.Valid:
                    user.MsgLocStr("Room is correct. Oxygen available", NotificationStyle.InfoBox);
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
    /// <summary>
    /// Simulate oxygen requirement for player, by dealing damage when they are outside a valid pressurised room and their oxygen tank (backpack) is empty
    /// Every player has an oxygen tank aka backpack (no new item, it's purely a number for the litres of oxgyen each player has)
    /// Swapping backpack type or removing it empties the current tank. This is just done for convience, although it may be possible to store the oxygen number against the specific backpack item if the reference to the backpack item is unique
    /// Fill the backpack up with oxygen at any waste filter that has a power source connected.
    /// Carry empty barrels and open the waste filter UI
    /// Each barrel tops up 5 minutes, and gets consumed
    /// 
    /// Every server restart the amount of oxygen the player has in their backpack gets forgotten
    /// </summary>
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
            if (user == null)
            {
                return;
            }
            Register(user);
        }
        public static void OnUserLoggedOut(User user)
        {
            if (user == null)
            {
                return;
            }
            Deregister(user);
        }
        private static readonly Dictionary<User, float> PersonalOxygen = new Dictionary<User, float>();
        private static readonly Dictionary<User, DateTime> LastUpdateTime = new Dictionary<User, DateTime>();
        private static readonly Dictionary<User, DateTime> LastMessageTime = new Dictionary<User, DateTime>();
        private static readonly Dictionary<User, int> LastMessagePercent = new Dictionary<User, int>();
        private static readonly Dictionary<User, DateTime> NextMessageTime = new Dictionary<User, DateTime>();
        private static IntervalActionWorker UpdateTicker;
        private static readonly Dictionary<User, bool> InBounds = new Dictionary<User, bool>();
        private static readonly Dictionary<User, Action> OnMovedHandlers = new Dictionary<User, Action>();
        //not implemented
        private static readonly Dictionary<User, bool> NotificationsOn = new Dictionary<User, bool>();
        private static MarsMultiBounds OxygenRequiredZone
        {
            get
            {
                return MarsMultiBounds.Mars;
            }
        }
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
            NotHighTier,
            MovedIntoBounds,
            MovedOutOfBounds
        }
        private static Action MakeOnMoved(User user)
        {
            return () =>
            {
                if (user == null)
                {
                    return;
                }
                if (InBounds[user])
                {
                    if (OxygenRequiredZone.InBounds(user.Position.XZ))
                    {
                        OxygenRooms.Tick(user);
                    }
                    else
                    {
                        InBounds[user] = false;
                        SendMessage(user, UpdateType.MovedOutOfBounds, true);
                    }
                }
                else if (OxygenRequiredZone.InBounds(user.Position.XZ))
                {
                    InBounds[user] = true;
                    SendMessage(user, UpdateType.MovedIntoBounds, true);
                }
            };
        }
        private static void OnOxygenLocationChanged(User user, OxygenRooms.RoomStatus previousLocation, OxygenRooms.RoomStatus newLocation)
        {
            if (user == null)
            {
                return;
            }
            if (!OxygenRequiredZone.InBounds(user.Position.XZ))
            {
                return;
            }
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
            if (user == null)
            {
                return false;
            }
            if (!Registered(user))
            {
                PersonalOxygen.Add(user, 0f);
                LastUpdateTime.Add(user, DateTime.Now);
                LastMessageTime.Add(user, DateTime.Now);
                NextMessageTime.Add(user, DateTime.Now.AddSeconds(SecondsUntilNextMessage(user)));
                InBounds.Add(user, OxygenRequiredZone.InBounds(user.Position.XZ));

                NotificationsOn.Add(user, true);
                OxygenRooms.Register(user);
                Action onMovedHandler = MakeOnMoved(user);
                user.OnMovedPlots.Add(onMovedHandler);
                OnMovedHandlers.Add(user, onMovedHandler);
                Log.WriteErrorLineLocStr("Registered " + user.Name);
                return true;
            }
            return false;
        }
        public static void Deregister(User user)
        {
            if (user == null)
            {
                return;
            }
            if (Registered(user))
            {
                PersonalOxygen.Remove(user);
                LastUpdateTime.Remove(user);
                LastMessageTime.Remove(user);
                NextMessageTime.Remove(user);
                InBounds.Remove(user);
                NotificationsOn.Remove(user);
                OxygenRooms.Deregister(user);
                user.OnMovedPlots.Remove(OnMovedHandlers[user]);
                OnMovedHandlers.Remove(user);
            }
        }
        public static bool Registered(User user)
        {
            if (user == null)
            {
                return false;
            }
            return PersonalOxygen.ContainsKey(user);
        }
        private static void SetOxygen(User user, float amount)
        {
            if (user == null)
            {
                return;
            }
            Register(user);
            amount = Math.Max(0, amount);
            PersonalOxygen[user] = amount;
        }
        public static void RefillOxygen(User user)
        {
            if (user == null)
            {
                return;
            }
            SetOxygen(user, OxygenBackpack.OxygenTankCapacity(user));
            StatusUpdate(user, UpdateType.Refilled, forceMessage: true);
        }
        public static void RefillOxygen(User user, float amount)
        {
            if (user == null)
            {
                return;
            }
            float totalAmount = Math.Min(OxygenRemaining(user) + amount, OxygenBackpack.OxygenTankCapacity(user));
            SetOxygen(user, totalAmount);
            StatusUpdate(user, UpdateType.Refilled, forceMessage: true);
        }
        public static void EmptyOxygenTank(User user)
        {
            if (user == null)
            {
                return;
            }
            user.MsgLocStr("Oxygen tank removed\nYou will need to refill whilst wearing a backpack");
            SetOxygen(user, 0f);
            LastUpdateTime[user] = DateTime.Now;
            StatusUpdate(user, UpdateType.Depleted, forceMessage: true);
        }
        public static void UseOxygenOverTime(User user, bool forceMessage = false)
        {
            if (user == null || !user.LoggedIn)
            {
                return;
            }
            Register(user);
            //don't do anything if we're not in the zone where oxygen is managed
            if (NeedsOxygen(user))
            {
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
            }
            //whatever happens, the last update was now
            LastUpdateTime[user] = DateTime.Now;
        }
        public static float OxygenRemaining(User user, bool register = true)
        {
            if (user == null)
            {
                return 0f;
            }
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
        public static bool HasOxygen(User user)
        {
            if (user == null)
            {
                return false;
            }
            return !NeedsOxygen(user) || OxygenRemaining(user) > 0 || OxygenRooms.OxygenAvailable(user);
        }
        public static bool NeedsOxygen(User user)
        {
            return OxygenRequiredZone.InBounds(user.Position.XZ);
        }
        public static string FormattedOxygenRemaining(User user, bool register = true)
        {
            if (user == null)
            {
                return "NULL USER ERROR";
            }
            float oxygenRemaining = OxygenRemaining(user, register);
            return oxygenRemaining.ToString("F0");
        }
        public static int PercentOxygenRemaining(User user, bool register = true, bool roundUp = true)
        {
            if (user == null)
            {
                return 0;
            }
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
            if (user == null)
            {
                return "NULL USER ERROR";
            }
            float oxygenPercentRemaining = PercentOxygenRemaining(user, register);
            return oxygenPercentRemaining.ToString("F0") + "%";
        }
        /// <summary>
        /// Send a message to the player and deal damage if necessary
        /// </summary>
        /// <param name="user"></param>
        /// <param name="updateType"></param>
        /// <param name="updateTime"></param>
        /// <param name="forceMessage"></param>
        private static void StatusUpdate(User user, UpdateType updateType, DateTime? updateTime = null, bool forceMessage = false)
        {
            if (user == null)
            {
                return;
            }
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
            if (user == null)
            {
                return 0f;
            }
            float oxygenTankCapacity = OxygenBackpack.OxygenTankCapacity(user);
            float oxygenRemaining = OxygenRemaining(user);
            //if we have no tank, send a message every 20s
            if (oxygenTankCapacity == 0 || oxygenRemaining == 0)
            {
                return 20f;
            }
            float[] messagePercents = new float[] { 90, 80, 70, 60, 50, 40, 30, 20, 10, 5, 4, 3, 2, 1, 0 };
            float currentPercent = oxygenRemaining / oxygenTankCapacity * 100;
            //how many seconds until each of the next percent message
            float[] timeUntilPercents = messagePercents.Select(percent => 0.01f * (currentPercent - percent) * oxygenTankCapacity).ToArray();

            //Pick the next one to occur, if it's not a number we've printed most recently. When a player types /ox, we can skip over this number if we would have otherwise printed it again
            return timeUntilPercents.Where(t => t >= 0 && t != LastMessagePercent[user]).MinOrDefault();
        }
        public static void SendMessage(User user, UpdateType updateType, bool forceMessage = false)
        {
            if (user == null)
            {
                return;
            }
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
                                user.MsgLocStr("Oxygen depleted!\nRefill your oxygen tank at a waste filter", NotificationStyle.InfoBox);
                            }
                            else
                            {
                                user.MsgLocStr("Oxygen tank not installed! Wear a backpack and refill at a waste filter", NotificationStyle.InfoBox);
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
                        user.MsgLocStr("Oxygen refilled to max:" + FormattedOxygenRemaining(user), NotificationStyle.InfoBox);
                        user.MsgLocStr("Oxygen refilled to max:" + FormattedOxygenRemaining(user), NotificationStyle.Info);
                    }
                    else
                    {
                        user.MsgLocStr("Oxygen refilled to " + FormattedOxygenRemaining(user) + "/" + OxygenBackpack.FormattedOxygenTankCapacity(user), NotificationStyle.InfoBox);
                        user.MsgLocStr("Oxygen refilled to " + FormattedOxygenRemaining(user) + "/" + OxygenBackpack.FormattedOxygenTankCapacity(user), NotificationStyle.Info);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.Used:
                    if (forceMessage || SecondsSince(DateTime.Now, NextMessageTime[user]) <= 0)
                    {
                        user.MsgLocStr("Oxygen level: " + FormattedPercentOxygenRemaining(user), NotificationStyle.InfoBox);
                        LastMessageTime[user] = DateTime.Now;
                        LastMessagePercent[user] = PercentOxygenRemaining(user);
                        NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    }
                    break;
                case UpdateType.TankRemoved:
                    if (forceMessage || SecondsSince(DateTime.Now, NextMessageTime[user]) <= 0)
                    {
                        user.MsgLocStr("Oxygen tank removed.\nYou will need to refill whilst wearing your backpack", NotificationStyle.InfoBox);
                        user.MsgLocStr("Oxygen tank removed. You will need to refill whilst wearing your backpack", NotificationStyle.Info);
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
                        user.MsgLocStr("Pressurised Room.\nOxygen tank off.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("Pressurised Room.\nYou can breathe again", NotificationStyle.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.DepressurisedRoom:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("Depressurised Room.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("Depressurised Room", NotificationStyle.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.NotHighTier:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("Room tier too low.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("Room tier too low", NotificationStyle.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.Outside:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("Oxygen tank on. " + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);

                        LastMessageTime[user] = DateTime.Now;
                        LastMessagePercent[user] = PercentOxygenRemaining(user);
                        NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    }
                    break;
                case UpdateType.NoWasteFilter:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("No waste filter in the room.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("No waste filter in the room", NotificationStyle.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.NoPower:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        user.MsgLocStr("Waste filter has no power.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("Waste filter has no power", NotificationStyle.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.MovedIntoBounds:
                    if (OxygenBackpack.WearingBackpack(user))
                    {
                        if (OxygenRemaining(user) > 0)
                        {
                            user.MsgLocStr("Leaving Earth.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                        }
                        else
                        {
                            user.MsgLocStr("Leaving Earth.\nOxygen tank empty!\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                        }
                        LastMessagePercent[user] = PercentOxygenRemaining(user);
                    }
                    else
                    {
                        user.MsgLocStr("Leaving Earth.\nNo oxygen tank!", NotificationStyle.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
                case UpdateType.MovedOutOfBounds:
                    if (OxygenBackpack.WearingBackpack(user) && OxygenRemaining(user) > 0)
                    {
                        user.MsgLocStr("Returning to Earth.\nOxygen tank off.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                    }
                    else
                    {
                        user.MsgLocStr("Returning to Earth.\nYou can stop holding your breath now", NotificationStyle.InfoBox);
                    }
                    LastMessageTime[user] = DateTime.Now;
                    LastMessagePercent[user] = PercentOxygenRemaining(user);
                    NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
                    break;
            }
        }
        /// <summary>
        /// Deal 50 damage to the player for every second without oxygen, and send a message if enough time has passed since the last one
        /// </summary>
        /// <param name="user"></param>
        /// <param name="secondsOfDepletion"></param>
        private static void TryDamage(User user, float secondsOfDepletion)
        {
            if (user != null && OxygenRemaining(user) <= 0)
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
    /// <summary>
    /// Used for checking if a user is in a room with oxygen available.
    /// The criteria for a valid room are : Tier3+, no holes, waste filter in the room with a power source of any supply/demand
    /// </summary>
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
            if (user == null || user.CurrentRoom == null || user.CurrentRoom.RoomStats == null || user.CurrentRoom.RoomStats.Volume == 0 || !user.CurrentRoom.RoomStats.Contained)
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
            if (user != null)
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
                                action.Citizen.MsgLocStr("Wear a backpack to refill oxygen", NotificationStyle.InfoBox);
                            }
                            else
                            {
                                if (OxygenManager.OxygenRemaining(action.Citizen) == OxygenBackpack.OxygenTankCapacity(action.Citizen))
                                {
                                    action.Citizen.MsgLocStr("Oxygen full", NotificationStyle.InfoBox);
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
                                                action.Citizen.MsgLocStr("No barrels left", NotificationStyle.InfoBox);
                                            }
                                        }

                                        else
                                        {
                                            action.Citizen.MsgLocStr("Connect to a power source", NotificationStyle.InfoBox);
                                        }
                                    }
                                    else
                                    {
                                        if (grid.EnergySupply > 0)
                                        {
                                            action.Citizen.MsgLocStr("Carry empty barrels to refill your backpack tank", NotificationStyle.InfoBox);
                                        }
                                        else
                                        {
                                            action.Citizen.MsgLocStr("Connect to a power source and carry empty barrels to refill", NotificationStyle.InfoBox);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        public LazyResult ShouldOverrideAuth(GameAction action)
        {
            return LazyResult.Succeeded;
        }
    }
    public class OxygenBackpack : IInitializablePlugin, IModKitPlugin
    {
        public string GetStatus() => "Active";

        public void Initialize(TimedTask timer)
        {
            UserManager.OnNewUserJoined.Add(OnUserLoggedIn);
            UserManager.OnUserInit.Add(OnUserLoggedIn);
            UserManager.OnUserLoggedIn.Add(OnUserLoggedIn);
            UserManager.OnUserLoggedOut.Add(OnUserLoggedOut);
        }
        //public static Dictionary<User, Action<User, IEnumerable<KeyValuePair<System.Type, int>>, Dictionary<ItemStack, ChangedStack>>> OnClothingChangedHandlers = new Dictionary<User, Action<User, IEnumerable<KeyValuePair<System.Type, int>>, Dictionary<ItemStack, ChangedStack>>>();
        public static Dictionary<User, Action<User>> OnClothingChangedHandlers = new Dictionary<User, Action<User>>();
        public static Dictionary<User, Type> WornBackpacks = new Dictionary<User, Type>();
        public static Type CurrentBackpack(User user)
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
        public static bool WearingBackpack(User user)
        {
            return CurrentBackpack(user) != null;
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
        /// Makes the personalised function i.e stores a reference to the player, so every player has their own unique version.
        /// It is the callback for when a player changes their clothes, where we can check whether the backpack type got changed or removed
        /// </summary>
        /// <returns></returns>
        public static Action<User> MakeOnClothingChanged()
        {
            return (User user) =>
            {
                if (user != null && WornBackpacks.ContainsKey(user))
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
                            user.MsgLocStr("Oxygen tank capacity: " + OxygenTankCapacity(currentBackpack), NotificationStyle.InfoBox);
                        }
                    }
                    WornBackpacks[user] = currentBackpack;
                    UpdateMovementSpeed(user);
                }
            };
        }
        public static bool Registered(User user)
        {
            return WornBackpacks.ContainsKey(user);
        }
        private static bool HasClothingInventory(User user)
        {
            return user != null && user.Inventory != null && user.Inventory.Clothing != null;
        }
        //Doesn't work
        public static void UpdateMovementSpeed(User user)
        {
            if (user != null)
            {
                user.ModifiedStats.Stats[UserStatType.MovementSpeed].ModifierClothing = MovementSpeedMultiplier(CurrentBackpack(user));
            }
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
    }
    public struct MarsBounds
    {
        public Vector2 lowerLeftCorner;
        public Vector2 dimensions;
        public Vector2 TopCorner
        {
            get
            {
                return lowerLeftCorner + dimensions;
            }
        }
        public Vector2 WrappedTopCorner
        {
            get
            {
                return World.World.GetWrappedWorldPosition(TopCorner);
            }
        }
        public MarsBounds(Vector2 lowerLeftCorner, Vector2 dimensions)
        {
            this.lowerLeftCorner = lowerLeftCorner;
            this.dimensions = dimensions;
        }
        public bool InBounds(Vector2 position)
        {
            bool inX = false;
            bool inY = false;
            Vector2 wrappedPosition = World.World.GetWrappedWorldPosition(position);

            if (WrappedTopCorner.x == TopCorner.x)
            {
                inX = (position.x >= lowerLeftCorner.x) && (position.x <= TopCorner.x);
            }
            else
            {
                inX = (position.x >= lowerLeftCorner.x) || (position.x <= WrappedTopCorner.x);
            }
            if (WrappedTopCorner.y == TopCorner.y)
            {
                inY = (position.y >= lowerLeftCorner.y) && (position.y <= TopCorner.y);
            }
            else
            {
                inY = (position.y >= lowerLeftCorner.y) || (position.y <= WrappedTopCorner.y);
            }
            return inX && inY;
        }
    }
    public struct MarsMultiBounds : IEnumerable<MarsBounds>
    {
        public static MarsMultiBounds Mars = new MarsMultiBounds(new List<MarsBounds>()
        {
            new MarsBounds()
        });
        private readonly List<MarsBounds> bounds;
        public List<MarsBounds> Bounds
        {
            get
            {
                return bounds ?? new List<MarsBounds>();
            }
        }
        public MarsMultiBounds(IEnumerable<MarsBounds> bounds)
        {
            this.bounds = bounds.ToList();
        }
        public bool InBounds(Vector2 position)
        {
            return Bounds.Count >= 1 && Bounds.Any(marsBounds => marsBounds.InBounds(position));
        }
        public IEnumerator<MarsBounds> GetEnumerator()
        {
            return Bounds.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Bounds.GetEnumerator();
        }
    }
}
