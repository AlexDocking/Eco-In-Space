namespace EcoInSpace
{
    using Eco.Core.Utils;
    using Eco.Core.Utils.Threading;
    using Eco.Gameplay.Players;
    using System;
    using System.Collections.Generic;

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
    public static class OxygenManager
    {
        public static float DamagePerSecond { get; set; } = 50f;
        public static UserDictionary<DateTime> LastUpdateTime { get; private set; } = new UserDictionary<DateTime>((User _) => DateTime.Now);
        public static bool OxygenEnabled
        {
            get
            {
                return OxygenSettings.Obj.OxygenEnabled;
            }
        }
        public static IBounds OxygenRequiredZone
        {
            get
            {
                return Settings.GetSpace();
            }
        }
        public static readonly ThreadSafeAction OnUpdateTick = new ThreadSafeAction();
        private static readonly ThreadSafeDictionary<User, float> PersonalOxygen = new ThreadSafeDictionary<User, float>();
        private static IntervalActionWorker UpdateTicker;
        public static void EmptyOxygenTank(User user)
        {
            if (user == null)
            {
                return;
            }
            EmptyOxygenTankNoMessage(user);
            MessageManager.SendMessage(user, OxygenLevelOfUserMessagesHandler.DEPLETED);
        }
        public static bool HasOxygen(User user)
        {
            if (user == null)
            {
                return false;
            }
            return !NeedsOxygen(user) || OxygenRemaining(user) > 0 || OxygenRoomManager.OxygenAvailable(user);
        }
        public static void Initialize()
        {
            UserManager.OnUserLoggedIn.Add(OnUserLoggedIn);
            //every second, update everyone's oxygen and write the values to a file so that they
            //can be read in again on the next server boot
            UpdateTicker = PeriodicWorkerFactory.CreateWithInterval(TimeSpan.FromSeconds(1), DoTick);
            UpdateTicker.Start();
        }
        public static bool NeedsOxygen(User user)
        {
            return OxygenRequiredZone.InBounds(user.Position);
        }
        /// <summary>
        /// When a player changes their backpack type, empty the oxygen tank
        /// </summary>
        /// <param name="user"></param>
        /// <param name="previousBackpack"></param>
        /// <param name="currentBackpack"></param>
        public static void OnBackpackChanged(User user, Type previousBackpack, Type currentBackpack)
        {
            //If we didn't change backpack type, nothing to do
            if (previousBackpack == currentBackpack)
            {
                return;
            }
            float oxygenAmountInTank = OxygenRemaining(user);
            EmptyOxygenTankNoMessage(user);

            //If we put on a backpack
            if (previousBackpack == null && currentBackpack != null || oxygenAmountInTank <= 0)
            {
                MessageManager.SendMessage(user, OxygenBackpackMessagesHandler.TANK_ADDED);
                return;
            }
            if (previousBackpack != null && currentBackpack == null && oxygenAmountInTank > 0)
            {
                MessageManager.SendMessage(user, OxygenBackpackMessagesHandler.TANK_REMOVED);
                return;
            }
            if (oxygenAmountInTank > 0)
            {
                MessageManager.SendMessage(user, OxygenBackpackMessagesHandler.TANK_CHANGED);
            }
        }
        public static float OxygenRemaining(User user)
        {
            if (user == null)
            {
                return 0f;
            }
            return PersonalOxygen.GetOr(user, 0f);
        }
        /// <summary>
        /// Fills up a user's tank to maximum
        /// </summary>
        /// <param name="user"></param>
        public static void RefillOxygen(User user)
        {
            if (user == null)
            {
                return;
            }
            RefillOxygen(user, OxygenBackpack.OxygenTankCapacity(user));
        }
        public static void RefillOxygen(User user, float amount)
        {
            if (user == null)
            {
                return;
            }
            float totalAmount = Math.Min(OxygenRemaining(user) + amount, OxygenBackpack.OxygenTankCapacity(user));
            SetOxygen(user, totalAmount);
        }
        public static bool Registered(User user)
        {
            if (user == null)
            {
                return false;
            }
            return PersonalOxygen.ContainsKey(user);
        }
        public static void SetNoMessage(User user, float amount)
        {
            SetOxygen(user, amount);
        }
        public static void UseOxygenOverTime(User user)
        {
            if (user == null || !user.LoggedIn)
            {
                return;
            }
            //don't do anything if we're not in the zone where oxygen is managed
            if (NeedsOxygen(user))
            {
                //only use oxygen when outside or in a depressurised room
                if (!OxygenRoomManager.OxygenAvailable(user))
                {
                    lock (LastUpdateTime)
                    {
                        float secondsSinceLastUpdate = LastUpdateTime[user].SecondsSince();
                        float secondsOfOxygenLeft = OxygenRemaining(user);
                        SetOxygen(user, OxygenRemaining(user) - secondsSinceLastUpdate);
                        //if we should have run out at some point since the last update
                        if (secondsOfOxygenLeft < secondsSinceLastUpdate)
                        {
                            //jump to the time it would have run out
                            LastUpdateTime[user] = LastUpdateTime[user].AddSeconds(secondsOfOxygenLeft);

                            if (secondsOfOxygenLeft > 0)
                            {
                                //if we ran out during this update, send a depleted message to the user
                                MessageManager.SendMessage(user, OxygenLevelOfUserMessagesHandler.DEPLETED);
                            }
                            else
                            {
                                //if we had already run out, send a request for a depleted message
                                //The message manager will ignore it if it's too soon since the last message
                                MessageManager.SendMessage(user, OxygenLevelOfUserMessagesHandler.DEPLETED_NOTIFICATION);
                            }
                            TryDamage(user, LastUpdateTime[user].SecondsSince());
                        }
                        secondsOfOxygenLeft = OxygenRemaining(user);
                        if (secondsOfOxygenLeft > 0)
                        {
                            MessageManager.SendMessage(user, OxygenLevelOfUserMessagesHandler.PERCENT_NOTIFICATION);
                        }
                    }
                }
            }
            //whatever happens, the last update was now
            LastUpdateTime[user] = DateTime.Now;
        }
        private static void DoTick()
        {
            if (!OxygenEnabled)
            {
                return;
            }
            IEnumerable<User> registeredUsers = PersonalOxygen.Keys;
            foreach (User user in registeredUsers)
            {
                UseOxygenOverTime(user);
            }
            OnUpdateTick.Invoke();
        }
        private static void EmptyOxygenTankNoMessage(User user)
        {
            if (user == null)
            {
                return;
            }
            SetOxygen(user, 0f);
            LastUpdateTime[user] = DateTime.Now;
        }
        private static void OnUserLoggedIn(User user)
        {
            PersonalOxygen.GetOrAdd(user);
        }
        private static void SetOxygen(User user, float amount)
        {
            if (user == null)
            {
                return;
            }
            amount = Math.Max(0, amount);
            PersonalOxygen.AddOrUpdate(user, amount, (User _, float __) => amount);
        }
        /// <summary>
        /// Deal damage to the player for every second without oxygen, and send a message if enough time has passed since the last one
        /// </summary>
        /// <param name="user"></param>
        /// <param name="secondsOfDepletion"></param>
        private static void TryDamage(User user, float secondsOfDepletion)
        {
            if (user != null && OxygenRemaining(user) <= 0)
            {
                user.Stomach.LoseCalories(secondsOfDepletion * DamagePerSecond, true);
                MessageManager.SendMessage(user, OxygenLevelOfUserMessagesHandler.DEPLETED_NOTIFICATION);
            }
        }
    }
}