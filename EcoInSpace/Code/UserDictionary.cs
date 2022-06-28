namespace EcoInSpace
{
    using Eco.Core.Utils;
    using Eco.Gameplay.Players;
    using Eco.Shared.Utils;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Automatically registers users to the dictionary when they log in, and removes their entry when they log out
    /// TODO: investigate whether ThreadSafeDictionary is a better choice as a backing type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [StaticInitialize]
    public class UserDictionary<T> : Dictionary<User, T>, ILogInOutEventSubscriber
    {
        public T DefaultValueOnRegister { get; set; } = default(T);
        public Func<User, T> ValueGetterOnRegister { get; set; } = null;

        /// <summary>
        /// Called when an entry is changed.
        /// Args: user, old value, new value
        /// </summary>
        public readonly ThreadSafeAction<User, T, T> OnValueChanged = new ThreadSafeAction<User, T, T>();
        private static readonly object lockSubscriberObject = new object();
        private static readonly List<ILogInOutEventSubscriber> Subscribers = new List<ILogInOutEventSubscriber>();
        private readonly object lockObject = new object();

        static UserDictionary()
        {
            UserManager.OnUserLoggedIn.Add(SendLogInEventToSubscribers);
            UserManager.OnUserLoggedOut.Add(SendLogOutEventToSubscribers);
        }

        public UserDictionary()
        {
            lock (lockSubscriberObject)
            {
                Subscribers.Add(this);
            }
        }
        public UserDictionary(T defaultValueOnRegister) : this()
        {
            DefaultValueOnRegister = defaultValueOnRegister;
        }
        public UserDictionary(Func<T> valueGetterOnRegister) : this()
        {
            ValueGetterOnRegister = (User _) => valueGetterOnRegister();
        }
        public UserDictionary(Func<User, T> valueGetterOnRegister) : this()
        {
            ValueGetterOnRegister = valueGetterOnRegister;
        }
        public T GetValueOnRegister(User user)
        {
            if (user == null || ValueGetterOnRegister == null)
            {
                return DefaultValueOnRegister;
            }
            return ValueGetterOnRegister(user);
        }
        public virtual void OnUserLoggedIn(User user)
        {
            if (user != null)
            {
                lock (lockObject)
                {
                    Add(user, GetValueOnRegister(user));
                }
            }
        }
        public virtual void OnUserLoggedOut(User user)
        {
            if (user != null)
            {
                lock (lockObject)
                {
                    Remove(user);
                }
            }
        }
        private static void SendLogInEventToSubscribers(User user)
        {
            lock (lockSubscriberObject)
            {
                foreach (ILogInOutEventSubscriber subscriber in Subscribers)
                {
                    subscriber.OnUserLoggedIn(user);
                }
            }
        }

        private static void SendLogOutEventToSubscribers(User user)
        {
            lock (lockSubscriberObject)
            {
                foreach (ILogInOutEventSubscriber subscriber in Subscribers)
                {
                    subscriber.OnUserLoggedOut(user);
                }
            }
        }
        public new T this[User key]
        {
            get
            {
                lock (lockObject)
                {
                    if (ContainsKey(key))
                    {
                        return base[key];
                    }
                    return default(T);
                }
            }
            set
            {
                bool changedValue = false;
                T oldValue = default(T);
                lock (lockObject)
                {
                    if (ContainsKey(key))
                    {
                        oldValue = base[key];
                        base[key] = value;
                        changedValue = !oldValue.Equals(value);
                    }
                }
                if (changedValue)
                {
                    OnValueChanged.Invoke(key, oldValue, value);
                }
            }
        }
    }
}