namespace EcoInSpace
{
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Property;
    using Eco.Gameplay.Systems.Messaging.Chat.Commands;
    using Eco.Shared.Utils;
    using System.Collections.Generic;
    using System.Linq;

    public class OxygenRoomCheckCommands : IChatCommandHandler
    {
        public const string FULL_REPORT_COMMAND = "atmreport";
        public const string SIMPLE_REPORT_COMMAND = "atm";
        public const string TIER_REPORT_COMMAND = "tierreport";
        public static void ProduceAirghtightnessTierReport(User user, IMessageManager messageManager)
        {
            OxygenRoomManager.OxygenRoom oxygenRoom = OxygenRoomManager.GetOxygenRoomForUser(user);
            if (oxygenRoom == null)
            {
                MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.OUTSIDE, messageManager);
                return;
            }
            MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.TIER_REPORT, messageManager, oxygenRoom.AverageTier, oxygenRoom.AirtightnessTier, oxygenRoom.RoomSystem.Rooms.Count, oxygenRoom.RoomSystem.WallCompositions);
        }
        public static async void ProduceFullReport(User user, IMessageManager messageManager)
        {
            await OxygenRoomManager.RecalculateOxygenRoomsAsync();
            OxygenRoomManager.RoomStatus roomStatus = OxygenRoomManager.CheckRoom(user);
            OxygenRoomManager.OxygenRoom oxygenRoom = OxygenRoomManager.GetOxygenRoomForUser(user);

            if (oxygenRoom == null)
            {
                MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.OUTSIDE, messageManager);
                return;
            }
            MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.ROOM_REPORT, messageManager, roomStatus, oxygenRoom);
        }
        public static void ProduceSimpleReport(User user, IMessageManager messageManager)
        {
            OxygenRoomManager.RoomStatus roomCheckResult = OxygenRoomManager.CheckRoom(user);
            if (roomCheckResult.IsOutside)
            {
                MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.OUTSIDE, messageManager);
                return;
            }
            if (roomCheckResult.IsLowTier)
            {
                OxygenRoomManager.OxygenRoom oxygenRoom = OxygenRoomManager.GetOxygenRoomForUser(user);
                int numRooms = oxygenRoom.RoomSystem.Rooms.Count;
                float currentAirtightnessTier = oxygenRoom.AirtightnessTier.RoundDown(2);
                oxygenRoom.RequiredTier(false, out float requiredTier);
                requiredTier = requiredTier.RoundUp(2);
                MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.LOW_TIER, messageManager, currentAirtightnessTier, requiredTier, numRooms);
                return;
            }
            if (roomCheckResult.IsDepressurised)
            {
                OxygenRoomManager.OxygenRoom oxygenRoom = OxygenRoomManager.GetOxygenRoomForUser(user);

                MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.DEPRESSURISED, messageManager, oxygenRoom);
                return;
            }
            if (roomCheckResult.IsLackingFilters)
            {
                MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.LACKING_FILTERS, messageManager);
                return;
            }
            if (roomCheckResult.IsLackingPower)
            {
                MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.LACKING_POWER, messageManager);
                return;
            }
            if (roomCheckResult.IsValid)
            {
                MessageManager.SendMessage(user, OxygenRoomReportMessagesHandler.VALID, messageManager);
                return;
            }
        }
        [ChatCommand("Full report on the oxygen status of the current room, including open windows, connected filters and adjoining rooms", FULL_REPORT_COMMAND, ChatAuthorizationLevel.User)]
        public static void RoomAtmosphereFullReport(User user)
        {
            ProduceFullReport(user, UserMessaging.Obj);
        }
        [ChatCommand("Checks what the current room needs in order to provide oxygen", SIMPLE_REPORT_COMMAND, ChatAuthorizationLevel.User)]
        public static void RoomAtmosphereSimpleReport(User user)
        {
            ProduceSimpleReport(user, UserMessaging.Obj);
        }
        [ChatCommand("Report on the airtightness of the blocks used in this room", TIER_REPORT_COMMAND, ChatAuthorizationLevel.User)]
        public static void RoomAtmosphereTierReport(User user)
        {
            ProduceAirghtightnessTierReport(user, UserMessaging.Obj);
        }
    }
}