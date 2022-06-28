namespace EcoInSpace
{
    using System.IO;

    public class OxygenConfigFileManager : ConfigFileManager<OxygenSettings>
    {
        public override string FileName { get; set; } = "Oxygen";
        public static readonly OxygenConfigFileManager Obj = ASingleton<OxygenConfigFileManager>.Obj;
        public OxygenConfigFileManager()
        { }
        /*public override bool ReadConfig(out OxygenSettings settings)
        {
            CreateConfigFile();
            string[] lines = File.ReadAllLines(FileLocation);
            if (lines.Length > 0)
            {
                if (lines[0] == "false")
                {
                    settings = false;
                }
                else
                {
                    settings = true;
                }
                return true;
            }
            settings = true;
            return false;
        }*/

        /*public override string Serialize(bool enabled)
        {
            return enabled ? "true" : "false";
        }*/
    }
}