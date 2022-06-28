namespace EcoInSpace
{
    using Eco.Gameplay.Players;
    using Eco.Shared.Utils;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public class UserOxygenConfigFileManager : ConfigFileManager<List<UserOxygenConfigFileManager.UserOxygen>>
    {
        public override string FileName { get; set; } = "UserOxygen";
        public static readonly UserOxygenConfigFileManager Obj = ASingleton<UserOxygenConfigFileManager>.Obj;
        public UserOxygenConfigFileManager()
        { }

        /*public string AllUserOxygenToString(List<UserOxygen> allUserOxygen)
        {
            string[] lines = allUserOxygen.Select(userOxygen => UserOxygenToString(userOxygen)).ToArray();
            string fileContents = string.Join("\n", lines);
            return fileContents;
        }

        public override bool ReadConfig(out List<UserOxygen> allUserOxygen)
        {
            CreateConfigFile();
            string[] lines = File.ReadAllLines(FileLocation);
            List<UserOxygen> userOxygenList = new List<UserOxygen>();
            foreach (string line in lines)
            {
                if (UserOxygenFromString(line, out UserOxygen userOxygen))
                {
                    userOxygenList.Add(userOxygen);
                }
            }
            allUserOxygen = userOxygenList;
            return false;
        }

        public override string Serialize(List<UserOxygen> allUserOxygen)
        {
            return AllUserOxygenToString(allUserOxygen);
        }

        public bool UserOxygenFromString(string line, out UserOxygen userOxygen)
        {
            if (line == null || line.Trim().Length == 0)
            {
                userOxygen = default;
                return false;
            }
            string[] parts = line.Split(',', StringSplitOptions.None).Select(p => p.Trim()).ToArray();
            if (parts.Length != 2 || !int.TryParse(parts[0], out int id) || !float.TryParse(parts[1], out float oxygenTankLitres))
            {
                Log.WriteErrorLineLocStr("Skipped incorrectly formatted UserOxygen Got \"" + line + "\". Format must be integer ID, float litres \"id, litres\"");
                userOxygen = default;
                return false;
            }
            userOxygen = new UserOxygen(id, oxygenTankLitres);
            return true;
        }

        public string UserOxygenToString(UserOxygen userOxygen)
        {
            return userOxygen.userId.ToString() + "," + userOxygen.oxygenTankLitres;
        }
        */

        public struct UserOxygen
        {
            public float oxygenTankLitres;
            public int userId;
            public UserOxygen(int userId, float oxygenTankLitres)
            {
                this.userId = userId;
                this.oxygenTankLitres = oxygenTankLitres;
            }
        }
    }
}