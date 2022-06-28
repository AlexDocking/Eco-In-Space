namespace EcoInSpace
{
    public class OxygenSettings
    {
        public static OxygenSettings Obj
        {
            get
            {
                return obj;
            }
            set
            {
                obj = value ?? new OxygenSettings();
            }
        }
        public float ConsumptionRate { get; set; } = 1f;
        public bool OxygenEnabled { get; set; } = true;
        private static OxygenSettings obj = new OxygenSettings();
        public OxygenSettings()
        {
        }
        public void SaveSettings()
        {
            OxygenConfigFileManager.Obj.WriteConfig(this);
        }
    }
}