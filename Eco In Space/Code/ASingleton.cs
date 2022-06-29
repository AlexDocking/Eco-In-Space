namespace EcoInSpace
{
    public static class ASingleton<T> where T : class, new()
    {
        public static bool IsConstructed
        {
            get
            {
                lock (lockObject)
                {
                    return obj != null;
                }
            }
        }
        public static bool IsNotConstructed
        {
            get
            {
                return !IsConstructed;
            }
        }
        public static T Obj
        {
            get
            {
                lock (lockObject)
                {
                    if (obj == null)
                    {
                        obj = MakeSingleton();
                    }
                    return obj;
                }
            }
        }
        private static object lockObject = new object();
        private static T obj;
        private static T MakeSingleton()
        {
            //Type classT = typeof(T);
            return new T();// (T)classT.GetConstructor(Type.EmptyTypes).Invoke(new object[0]);
        }
    }
}