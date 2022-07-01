namespace EcoInSpace
{
    using Eco.Core.Utils;
    using Eco.Shared.Utils;
    using System.IO;

    public abstract class ConfigFileManager<T> where T : class, new()
    {
        public virtual string FileExtension { get; set; } = ".config";
        public virtual string FileLocation
        {
            get
            {
                return Path.Combine(FolderLocation, FileName) + FileExtension;
            }
        }
        public abstract string FileName { get; set; }
        public virtual string FolderLocation
        {
            get
            {
                return Path.Combine(Directory.GetCurrentDirectory(), RelativeFolderLocation);
            }
        }
        public virtual string RelativeFileLocation
        {
            get
            {
                //Directory.GetCurrentDirectory();
                string fileLocation = Path.Combine(RelativeFolderLocation, FileName) + FileExtension;
                return fileLocation;
            }
        }
        public virtual string RelativeFolderLocation
        {
            get
            {
                return "Configs\\Mods\\EcoInSpace";
            }
        }
        protected object initializeLock = new object();
        protected bool isInitialized = false;
        public virtual bool ReadConfig(out T data)
        {
            CreateConfigFile();
            try
            {
                data = Eco.EM.Framework.FileManager.FileManager<T>.ReadTypeHandledFromFile(RelativeFolderLocation, FileName, FileExtension);
                return true;
            }
            catch
            {
                data = default;
                return false;
            }
        }
        /*public bool ReadIfNecessary(out T data)
        {
            lock (initializeLock)
            {
                if (!isInitialized)
                {
                    isInitialized = true;
                    return ReadConfig(out data);
                }
            }
            data = default(T);
            return false;
        }*/
        //public abstract string Serialize(T data);
        public virtual void WriteConfig(T data)
        {
            CreateConfigFile();
            try
            {
                Eco.EM.Framework.FileManager.FileManager<T>.WriteTypeHandledToFile(data, RelativeFolderLocation, FileName, FileExtension);
            }
            catch
            {
                Log.WriteErrorLineLocStr("Config file write failed in " + this.GetType().Name);
            }
        }
        /// <summary>
        /// Create an empty config file if it doesn't exist
        /// </summary>
        protected void CreateConfigFile()
        {
            if (File.Exists(FileLocation))
            {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(FileLocation));
            File.Create(FileLocation).Close();
        }
    }
}