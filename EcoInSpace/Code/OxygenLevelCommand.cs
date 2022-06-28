namespace EcoInSpace
{
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Systems.Messaging.Chat.Commands;

    public class OxygenLevelCommand : IChatCommandHandler
    {
        /// <summary>
        /// Enable or disable oxygen requirement for all users
        /// Setting it true means everyone in the zone needs either tank or room oxygen
        /// Setting it false means everyone can breathe normally
        /// </summary>
        /// <param name="user"></param>
        [ChatCommand("Fill up your oxygen backpack to full", "fullox", ChatAuthorizationLevel.Admin)]
        public static void OxygenFull(User user)
        {
            if (!OxygenBackpack.WearingBackpack(user))
            {
                user.MsgLocStr("Wear a backpack first");
            }
            OxygenManager.RefillOxygen(user);
        }
        /// <summary>
        /// Command for printing the user's oxygen level
        /// </summary>
        /// <param name="user"></param>
        [ChatCommand("Informs you of your oxygen level", "ox", ChatAuthorizationLevel.User)]
        public static void OxygenLevel(User user)
        {
            if (OxygenManager.OxygenRemaining(user) == 0)
            {
                MessageManager.SendMessage(user, OxygenLevelOfUserMessagesHandler.DEPLETED);
            }
            else
            {
                MessageManager.SendMessage(user, OxygenLevelOfUserMessagesHandler.CHECK_PERCENT);
            }
        }
        /// <summary>
        /// Enable or disable oxygen requirement for all users
        /// Setting it true means everyone in the zone needs either tank or room oxygen
        /// Setting it false means everyone can breathe normally
        /// </summary>
        /// <param name="user"></param>
        [ChatCommand("Enable or disable oxygen", ChatAuthorizationLevel.Admin)]
        public static void SetOxygenEnabled(User user, string enabled)
        {
            enabled = enabled.ToLower().Trim();
            if (enabled == "true")
            {
                OxygenSettings.Obj.OxygenEnabled = true;
                OxygenSettings.Obj.SaveSettings();
                user.MsgLocStr("Oxygen disabled for all users");
            }
            else if (enabled == "false")
            {
                OxygenSettings.Obj.OxygenEnabled = false;
                OxygenSettings.Obj.SaveSettings();
                user.MsgLocStr("Oxygen enabled for all users");
            }
            else
            {
                user.MsgLocStr("Specify true or false");
            }
        }
    }
}