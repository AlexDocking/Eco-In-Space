namespace EcoInSpace
{
    using Eco.Core.Plugins.Interfaces;
    using Eco.Core.Utils;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.GameActions;
    using Eco.Gameplay.Objects;
    using Eco.Gameplay.Players;
    using Eco.Mods.TechTree;
    using Eco.Shared.Utils;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;

    //new filters aren't registered until a pipe is connected to its input or output
    //old filters aren't deregistered until the player leaves the room
    //calculating average tier of connected rooms is wrong. Shared walls will count twice. Unsure if there's a solution as might not be able to access individual wall blocks
    public class OxygenPlugin : IInitializablePlugin, IModKitPlugin
    {
        public static void ReadMarsConfig()
        {
            if (!MarsConfigFileManager.Obj.ReadConfig(out MultiBounds<Bounds2D> mars))
            {
                Log.WriteErrorLineLocStr("Couldn't read mars config");
            }
            Settings.SetSpace(mars);
        }
        public static void ReadOxygenConfig()
        {
            if (!OxygenConfigFileManager.Obj.ReadConfig(out OxygenSettings settings))
            {
                Log.WriteErrorLineLocStr("Couldn't read oxygen config");
            }
            OxygenSettings.Obj = settings;
        }
        public static void ReadUserOxygenConfig()
        {
            if (!UserOxygenConfigFileManager.Obj.ReadConfig(out List<UserOxygenConfigFileManager.UserOxygen> allUserOxygen))
            {
                Log.WriteErrorLineLocStr("Couldn't read user oxygen config");
                return;
            }
            foreach (UserOxygenConfigFileManager.UserOxygen userOxygen in allUserOxygen)
            {
                User user = UserManager.FindUserByID(userOxygen.userId);
                if (user != null)
                {
                    OxygenManager.SetNoMessage(user, userOxygen.oxygenTankLitres);
                }
            }
        }
        public static void WriteUserOxygenConfig()
        {
            UserOxygenConfigFileManager.Obj.WriteConfig(UserManager.Users.Select(user => new UserOxygenConfigFileManager.UserOxygen(user.Id, OxygenManager.OxygenRemaining(user))).ToList());
        }
        public string GetStatus() => "Active";

        public void Initialize(TimedTask timer)
        {
            //User dictionaries won't instantiate automatically (C# feature to save computing power on objects that don't ever get called)
            //They will miss log in events unless we wake them up
            WakeUpUserDictionaries();
            //OnMovedManager must know when players log in so it can track their movement
            LogInOutEventSubscriptionManager.Register(OnMovedManager.Obj);
            //InMarsBoundsEventManager must know when players move so it can check whether they are still in the zone
            OnMovedManager.Register(InMarsBoundsEventManager.CheckIfUserChangedZone);
            //OxygenRoomManager must know when players move so it can check whether they move into a different room
            OnMovedManager.Register(OxygenRoomManager.RecheckUserLocation);
            //OxygenManager must know when the player changes backpack so it can empty the tank
            OxygenBackpack.OnBackpackChangedEvent.Add(OxygenManager.OnBackpackChanged);
            //OxygenRoomManager wants to send messages to the user when their location changes
            OxygenRoomManager.OnOxygenLocationStatusChangedEvent.Add(OxygenRoomManager.OnOxygenLocationChanged);
            OxygenManager.OnUpdateTick.Add(WriteUserOxygenConfig);
            //OxygenRefillGameActionListener singleton needs registering to listen out for game actions
            ActionUtil.AddListener(ASingleton<OxygenRefillGameActionListener>.Obj);
            OxygenRoomManager.Initialize();
            OxygenManager.Initialize();
            OxygenBackpack.Initialize();

            //Read in the user oxygen config file to get the user oxygen level from the last time the server ran
            ReadUserOxygenConfig();
            //Read in the mars config file to get the zone locations
            ReadMarsConfig();
            //Read in the oxygen config file to get whether oxygen is enabled or not
            ReadOxygenConfig();
        }
        /// <summary>
        /// The static UserDictionary fields that many classes have don't get created until the class or field get called.
        /// This explicitly asks for their value, which causes them to get instantiated
        /// Beforehand, they would miss user log in events because the field or class hadn't been called yet, and so wouldn't have registered itself to the log in event
        /// It checks all static fields in all declared types in the Eco namespace, and jumps on those fields whose type has a StaticInitializeAttribute (custom attribute applied to the UserDictionary class purely for this purpose)
        /// </summary>
        private static void WakeUpUserDictionaries()
        {
            foreach (TypeInfo typeInfo in Assembly.GetAssembly(typeof(OxygenPlugin)).DefinedTypes)
            {
                foreach (FieldInfo fieldInfo in typeInfo.DeclaredFields)
                {
                    if (fieldInfo.IsStatic && fieldInfo.FieldType.GetCustomAttributes().Any(attribute => attribute is StaticInitializeAttribute))
                    {
                        //Calling GetValue instantiates the dictionary, giving it the chance to register itself to log in/out events
                        object _ = fieldInfo.GetValue(null);
                    }
                }
            }
        }
    }
}