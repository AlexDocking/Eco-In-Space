namespace EcoInSpace
{
    using Eco.Gameplay.Players;
    using System;
    using System.Collections.Generic;

    public static class LogInOutEventSubscriptionManager
    {
        private static readonly object lockObject = new object();
        private static readonly List<ILogInOutEventSubscriber> Subscribers = new List<ILogInOutEventSubscriber>();
        static LogInOutEventSubscriptionManager()
        {
            UserManager.OnUserLoggedIn.Add(SendLogInEventToSubscribers);
            UserManager.OnUserLoggedOut.Add(SendLogOutEventToSubscribers);
        }

        public static void Register(Action<User> onUserLoggedIn, Action<User> onUserLoggedOut)
        {
            lock (lockObject)
            {
                Subscribers.Add(new ActionSubscriber(onUserLoggedIn, onUserLoggedOut));
            }
        }

        public static void Register(ILogInOutEventSubscriber subscriber)
        {
            lock (lockObject)
            {
                Subscribers.Add(subscriber);
            }
        }

        private static void SendLogInEventToSubscribers(User user)
        {
            lock (lockObject)
            {
                foreach (ILogInOutEventSubscriber subscriber in Subscribers)
                {
                    subscriber.OnUserLoggedIn(user);
                }
            }
        }

        private static void SendLogOutEventToSubscribers(User user)
        {
            lock (lockObject)
            {
                foreach (ILogInOutEventSubscriber subscriber in Subscribers)
                {
                    subscriber.OnUserLoggedOut(user);
                }
            }
        }

        private struct ActionSubscriber : ILogInOutEventSubscriber
        {
            private Action<User> onUserLoggedInAction;
            private Action<User> onUserLoggedOutAction;

            public ActionSubscriber(Action<User> onUserLoggedInAction, Action<User> onUserLoggedOutAction)
            {
                this.onUserLoggedInAction = onUserLoggedInAction;
                this.onUserLoggedOutAction = onUserLoggedOutAction;
            }

            public void OnUserLoggedIn(User user)
            {
                onUserLoggedInAction(user);
            }

            public void OnUserLoggedOut(User user)
            {
                onUserLoggedOutAction(user);
            }
        }
    }
}