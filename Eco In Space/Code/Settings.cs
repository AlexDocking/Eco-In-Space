namespace EcoInSpace
{
    public static class Settings
    {
        private static MultiBounds<Bounds2D> space = new MultiBounds<Bounds2D>();
        public static void AddSaveSpace(Bounds2D newSpace)
        {
            MultiBounds<Bounds2D> totalSpace = new MultiBounds<Bounds2D>(space);
            totalSpace.Add(newSpace);
            SetSaveSpace(totalSpace);
        }
        public static IBounds GetSpace()
        {
            return space;
        }
        /// <summary>
        /// Set the oxygen zone and save it to file, so that you won't have to set it again after a server restart
        /// </summary>
        /// <param name="newSpace"></param>
        public static void SetSaveSpace(MultiBounds<Bounds2D> newSpace)
        {
            SetSpace(newSpace);
            MarsConfigFileManager.Obj.WriteConfig(newSpace);
        }
        /// <summary>
        /// Set the oxygen zone. It won't automatically be saved to file
        /// </summary>
        /// <param name="newSpace"></param>
        public static void SetSpace(MultiBounds<Bounds2D> newSpace)
        {
            space = newSpace;
        }
    }
}