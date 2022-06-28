namespace EcoInSpace
{
    using Eco.Core.Utils;
    using Eco.Gameplay.Players;
    using System;
    using System.Collections.Generic;

    public class OnMovedManager : ILogInOutEventSubscriber
    {
        public static readonly OnMovedManager Obj = ASingleton<OnMovedManager>.Obj;
        protected static List<Action<User>> Actions { get; set; } = new List<Action<User>>();
        protected static ThreadSafeDictionary<User, List<Action>> OnMovedActions { get; set; } = new ThreadSafeDictionary<User, List<Action>>();
        private static readonly object locker = new object();
        public OnMovedManager()
        { }

        public static void Register(Action<User> onMoved)
        {
            lock (locker)
            {
                Actions.Add(onMoved);
                foreach (User user in OnMovedActions.Keys)
                {
                    AddActionToUser(user, () => onMoved(user));
                }
            }
        }

        public void OnUserLoggedIn(User user)
        {
            lock (locker)
            {
                foreach (Action<User> action in Actions)
                {
                    Action boxedUserAction = () => action(user);
                    AddActionToUser(user, boxedUserAction);
                }
            }
        }
        public void OnUserLoggedOut(User user)
        {
            lock (locker)
            {
                if (!OnMovedActions.ContainsKey(user))
                {
                    return;
                }
                foreach (Action action in OnMovedActions[user])
                {
                    user.OnMovedPlots.Remove(action);
                }
                OnMovedActions.Remove(user);
            }
        }
        protected static void AddActionToUser(User user, Action action)
        {
            if (OnMovedActions.ContainsKey(user))
            {
                OnMovedActions[user].Add(action);
                user.OnMovedPlots.Add(action);
            }
            else
            {
                OnMovedActions.Add(user, new List<Action>() { action });
            }
        }
    }
}