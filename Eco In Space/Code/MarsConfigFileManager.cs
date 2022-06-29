namespace EcoInSpace
{
    using Eco.Shared.Utils;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public class MarsConfigFileManager : ConfigFileManager<MultiBounds<Bounds2D>>
    {
        public override string FileName { get; set; } = "MarsBounds";
        public static readonly MarsConfigFileManager Obj = ASingleton<MarsConfigFileManager>.Obj;
        public MarsConfigFileManager()
        { }
        /*public bool MarsBoundsFromString(string line, out Bounds2D marsBounds)
        {
            if (line == null || line.Trim().Length == 0)
            {
                marsBounds = default(Bounds2D);
                return false;
            }
            string[] parts = line.Split(',', StringSplitOptions.None).Select(p => p.Trim()).ToArray();
            if (parts.Length != 4 || parts.Any(p => !int.TryParse(p, out _)))
            {
                Log.WriteErrorLineLocStr("Skipped incorrectly formatted MarsBounds. Got \"" + line + "\". Format must be integers \"x,y,w,h\": x,y is lower left, w,h of rectangle");
                marsBounds = default(Bounds2D);
                return false;
            }
            int x = int.Parse(parts[0]);
            int y = int.Parse(parts[1]);
            int w = int.Parse(parts[2]);
            int h = int.Parse(parts[3]);
            marsBounds = new Bounds2D(x, y, w, h);
            return true;
        }
        public string MarsBoundsToString(Bounds2D marsBounds)
        {
            int[] values = new int[] { (int)marsBounds.lowerLeftCorner.x, (int)marsBounds.lowerLeftCorner.y, (int)marsBounds.dimensions.x, (int)marsBounds.dimensions.y };
            return string.Join(",", values.Select(v => v.ToString()));
        }
        public string MarsMultiBoundsToString(MultiBounds<Bounds2D> marsMultiBounds)
        {
            List<string> lines = new List<string>();
            foreach (IBounds bounds in marsMultiBounds)
            {
                if (bounds is Bounds2D bounds2D)
                {
                    lines.Add(MarsBoundsToString(bounds2D));
                }
            }
            string fileContents = string.Join("\n", lines);
            return fileContents;
        }
        public override bool ReadConfig(out MultiBounds<Bounds2D> mars)
        {
            CreateConfigFile();
            string[] lines = File.ReadAllLines(FileLocation);
            List<Bounds2D> marsBounds = new List<Bounds2D>();
            foreach (string line in lines)
            {
                Bounds2D newBounds;
                if (MarsBoundsFromString(line, out newBounds))
                {
                    marsBounds.Add(newBounds);
                }
            }
            mars = new MultiBounds<Bounds2D>(marsBounds);
            return true;
        }
        public override string Serialize(MultiBounds<Bounds2D> mars)
        {
            return MarsMultiBoundsToString(mars);
        }*/
    }
}