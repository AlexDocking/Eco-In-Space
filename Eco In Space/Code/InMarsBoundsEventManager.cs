namespace EcoInSpace
{
    using Eco.Core.Utils;
    using Eco.Gameplay.Players;
    using Eco.Shared.Math;

    /// <summary>
    /// When the player moves, check whether they are entering or leaving space
    /// </summary>
    public static class InMarsBoundsEventManager
    {
        public static readonly ThreadSafeAction<User, Vector3, Vector3> OnUserMovedIntoBounds = new ThreadSafeAction<User, Vector3, Vector3>();
        public static readonly ThreadSafeAction<User, Vector3, Vector3> OnUserMovedOutOfBounds = new ThreadSafeAction<User, Vector3, Vector3>();
        private static IBounds Bounds
        {
            get
            {
                return Settings.GetSpace();
            }
        }
        private static readonly UserDictionary<bool> InBounds = new UserDictionary<bool>((User user) => IsUserInBounds(user));
        private static readonly UserDictionary<Vector3> UserPosition = new UserDictionary<Vector3>((User user) => user.Position);
        static InMarsBoundsEventManager()
        {
            InBounds.OnValueChanged.Add(OnUserMovedIntoOrOutOfBounds);
        }
        /// <summary>
        /// When the user moves, fire off events for entering and leaving Mars
        /// </summary>
        /// <param name="user"></param>
        public static void CheckIfUserChangedZone(User user)
        {
            if (user == null)
            {
                return;
            }
            UserPosition[user] = user.Position;
            InBounds[user] = IsUserInBounds(user);
        }
        private static bool IsUserInBounds(User user)
        {
            return Bounds.InBounds(user.Position);
        }
        private static void OnUserMovedIntoOrOutOfBounds(User user, bool wasInBounds, bool nowInBounds)
        {
            //if no longer in oxygen zone
            if (!nowInBounds)
            {
                //send a message to tell the player they no longer need oxygen
                MessageManager.SendMessage(user, OxygenLocationMessagesHandler.MOVED_OUT_OF_ZONE);
                OnUserMovedOutOfBounds.Invoke(user, UserPosition[user], user.Position);
            }
            //if not previously in oxygen zone and now we are
            else
            {
                //send a message to tell the player they will need oxygen
                MessageManager.SendMessage(user, OxygenLocationMessagesHandler.MOVED_INTO_ZONE);

                OnUserMovedIntoBounds.Invoke(user, UserPosition[user], user.Position);
            }
        }
    }
}