namespace EcoInSpace
{
    using Eco.Core.Utils.Async;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Property;
    using Eco.Gameplay.Rooms;
    using Eco.Gameplay.Systems.TextLinks;
    using Eco.Mods.TechTree;
    using Eco.Shared.Localization;
    using Eco.Shared.Math;
    using Eco.Shared.Services;
    using Eco.Shared.Utils;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public delegate void MessageHandlerMethod(User user, string messageType, IMessageManager messageManager, params object[] parameters);
    public interface IMessageHandler
    {
        bool CanProcess(User user, string messageType, IMessageManager messageManager, params object[] parameters);
        void Send(User user, string messageType, IMessageManager messageManager, params object[] parameters);
    }
    public interface IMessageManager
    {
        void SendMsg(User user, LocString message, NotificationStyle style);
    }
    public static class MessageManager
    {
        public static List<IMessageHandler> MessageHandlers { get; private set; } = new List<IMessageHandler>();
        static MessageManager()
        {
            MessageHandlers.AddRange(new List<IMessageHandler>()
            {
                new OxygenTankRefillMessageHandler(),
                new OxygenLevelOfUserMessagesHandler(),
                new OxygenRoomReportMessagesHandler(),
                new OxygenLocationMessagesHandler(),
                new OxygenBackpackMessagesHandler()
            });
        }
        public static void SendMessage(User user, string messageType, params object[] parameters)
        {
            SendMessage(user, messageType, UserMessaging.Obj, parameters);
        }
        public static void SendMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            foreach (IMessageHandler messageHandler in MessageHandlers)
            {
                messageHandler.Send(user, messageType, messageManager, parameters);
            }
        }
    }
    /// <summary>
    /// Helper methods
    /// </summary>
    public static class OxygenMessaging
    {
        public static Color blackBarEmptyColour = new Color("dedede");
        public static Color blockBarFullColour = Color.Black;
        public static Color emptyColour = new Color("666666");
        private const string bar = "|";
        public static Message AddPercentUpdateFlag(Message message, int percent)
        {
            message.AddFlag(MessageTimeManager.UPDATE_LAST_MESSAGE_PERCENT_FLAG, percent);
            return message;
        }
        public static Message AddUpdateFlag(Message message)
        {
            message.AddFlag(MessageTimeManager.UPDATE_LAST_MESSAGE_TIME_FLAG, null);
            return message;
        }
        public static Color BatteryColour(float percent)
        {
            if (percent >= 50f)
            {
                return Color.Green;
            }
            if (percent >= 25f)
            {
                return Color.Orange;
            }
            return Color.Red;
        }
        public static string DrawBatteryBlock(int blockNum, float percent)
        {
            int barsInBlock = 5;
            string str = "";
            int numColouredBars = Math.Max(0, Math.Min(barsInBlock, (int)Math.Ceiling((percent - blockNum * 25) / barsInBlock)));
            int numEmptyBars = barsInBlock - numColouredBars;
            Color batteryColour = BatteryColour(percent);
            str += bar.RepeatString(numColouredBars).Color(batteryColour);
            str += bar.RepeatString(numEmptyBars).Color(emptyColour);
            str += bar.Color(numColouredBars == barsInBlock ? blockBarFullColour : blackBarEmptyColour);
            return str;
        }
        public static void EvaluateMessage(User user, Message message)
        {
            if (message.ContainsFlag(MessageTimeManager.UPDATE_LAST_MESSAGE_TIME_FLAG))
            {
                MessageTimeManager.OnMessageSent(user);
            }
            if (message.ContainsFlag(MessageTimeManager.UPDATE_LAST_MESSAGE_PERCENT_FLAG))
            {
                MessageTimeManager.OnPercentMessageSent(user, message.GetFlag<int>(MessageTimeManager.UPDATE_LAST_MESSAGE_PERCENT_FLAG));
            }
        }
        public static string FormattedOxygenLitresFractionRemaining(float litres, float capacity)
        {
            return litres.ToString("F0") + "/" + capacity.ToString("F0") + "L";
        }
        public static string FormattedOxygenPercentRemaining(float floatPercent)
        {
            return string.Format("{0}%", floatPercent.ToString("F0"));
        }
        public static Message LitresFractionRemainingMessage(User user)
        {
            if (user == null)
            {
                return "NULL USER ERROR";
            }
            return FormattedOxygenLitresFractionRemaining(OxygenManager.OxygenRemaining(user), OxygenBackpack.OxygenTankCapacity(user));
        }
        public static Message OxygenBatteryBar(float percent)
        {
            string batteryStr = bar.Color(blackBarEmptyColour) + DrawBatteryBlock(0, percent) + DrawBatteryBlock(1, percent) + DrawBatteryBlock(2, percent) + DrawBatteryBlock(3, percent);
            batteryStr += "=".VOffset(0.17f).Color(blackBarEmptyColour);
            batteryStr = batteryStr.MSpace(0.1f);
            return batteryStr;
        }
        public static Message OxygenBatteryBar(User user)
        {
            if (user == null)
            {
                return "NULL USER ERROR";
            }
            int oxygenPercentRemaining = PercentOxygenRemaining(user);
            return OxygenBatteryBar(oxygenPercentRemaining);
        }
        public static Color PercentColour(float percent)
        {
            if (percent >= 100f * 2f / 3f)
            {
                return Color.Green;
            }
            if (percent >= 100f / 3f)
            {
                return Color.Yellow;
            }
            return Color.Red;
        }
        public static int PercentOxygenRemaining(User user, bool roundUp = true)
        {
            if (user == null)
            {
                return 0;
            }
            float oxygenRemaining = OxygenManager.OxygenRemaining(user);
            float oxygenCapacity = OxygenBackpack.OxygenTankCapacity(user);
            if (oxygenCapacity > 0)
            {
                if (roundUp)
                {
                    return (int)Math.Ceiling(oxygenRemaining / oxygenCapacity * 100);
                }
                return (int)(oxygenRemaining / oxygenCapacity * 100);
            }
            return 100;
        }
        public static Message PercentRemainingMessage(User user)
        {
            if (user == null)
            {
                return "NULL USER ERROR";
            }
            int oxygenPercentRemaining = PercentOxygenRemaining(user);
            return PercentRemainingMessage(user, oxygenPercentRemaining);
        }
        public static Message PercentRemainingMessage(User user, int percent)
        {
            if (user == null)
            {
                return "NULL USER ERROR";
            }
            Message percentRemainingMessage = FormattedOxygenPercentRemaining(percent);
            AddPercentUpdateFlag(percentRemainingMessage, percent);
            return percentRemainingMessage;
        }
        public static string RoomStatusHeader(OxygenRoomManager.RoomStatus roomStatus)
        {
            return ("Room Report".Underline().Bold() + "\nStatus: " + roomStatus.ToString().Color(roomStatus.IsValid ? Color.LightGreen : Color.LightRed)).Bold();
        }
    }
    /// <summary>
    /// Shortcut strings
    /// </summary>
    public static class SC
    {
        /// <summary>
        /// Metres cubed with space
        /// </summary>
        public const string M3 = " m<sup>3</sup>";
    }
    public static class TagExtensions
    {
        public static string Bold(this string str)
        {
            return string.Format("<b>{0}</b>", str);
        }
        public static Message Bold(this Message message)
        {
            return string.Format("<b>") + message + string.Format("</b>");
        }
        public static string Color(this string str, Color colour)
        {
            return str.Color(colour.HexRGBA);
        }
        public static string Color(this string str, string hex)
        {
            return string.Format("<color={0}>{1}</color>", hex, str);
        }
        public static Message Color(this Message message, Color colour)
        {
            return message.Color(colour.HexRGBA);
        }
        public static Message Color(this Message message, string hex)
        {
            return string.Format("<color={0}>", hex) + message + string.Format("</color>");
        }
        public static string Italics(this string str)
        {
            return string.Format("<i>{0}</i>", str);
        }
        public static Message Italics(this Message message)
        {
            return string.Format("<i>") + message + string.Format("</i>");
        }
        public static string MSpace(this string str, float mSpace)
        {
            return string.Format("<mspace={0}em>{1}</mspace>", mSpace, str);
        }
        public static Message MSpace(this Message message, float mSpace)
        {
            return string.Format("<mspace={0}em>", mSpace) + message + string.Format("</mspace>");
        }
        public static string RemoveSize(this string str, int size)
        {
            str = str.Replace("<size=" + size + ">", "");
            str = str.Replace("</size>", "");
            return str;
        }
        public static string Strikethrough(this string str)
        {
            return string.Format("<s>{0}</s>", str);
        }
        public static Message Strikethrough(this Message message)
        {
            return string.Format("<s>") + message + string.Format("</s>");
        }
        public static string Sup(this string str)
        {
            return string.Format("<sup>{0}</sup>", str);
        }
        public static Message Sup(this Message message)
        {
            return string.Format("<sup>") + message + string.Format("</sup>");
        }
        public static string Underline(this string str)
        {
            return string.Format("<u>{0}</u>", str);
        }
        public static Message Underline(this Message message)
        {
            return string.Format("<u>") + message + string.Format("</u>");
        }
        public static string VOffset(this string str, float vOffset)
        {
            return string.Format("<voffset={0}em>{1}</voffset>", vOffset, str);
        }
        public static Message VOffset(this Message message, float vOffset)
        {
            return string.Format("<voffset={0}em>", vOffset) + message + string.Format("</voffset>");
        }
    }
    public abstract class CategoryMessageHandler : IMessageHandler
    {
        public List<string> MessageNames
        {
            get
            {
                return Handlers.Keys.ToList();
            }
        }
        protected abstract Dictionary<string, MessageHandlerMethod> Handlers { get; }
        public virtual bool CanProcess(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            return MessageNames.Contains(messageType);
        }
        public virtual void Send(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (Handlers.ContainsKey(messageType))
            {
                if (IsTimeForNewMessage(user, messageType, messageManager, parameters))
                {
                    Handlers[messageType].Invoke(user, messageType, messageManager, parameters);
                }
            }
        }
        protected virtual void EvaluateMessage(User user, IMessageManager messageManager, Message message, NotificationStyle style)
        {
            OxygenMessaging.EvaluateMessage(user, message);
        }
        protected virtual bool IsTimeForNewMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            return true;
        }
        protected virtual void SendMessage(User user, IMessageManager messageManager, Message message, NotificationStyle style)
        {
            EvaluateMessage(user, messageManager, message, style);
            messageManager.SendMsg(user, message.Str, style);
        }
        protected virtual void SendMessageAsUpdate(User user, IMessageManager messageManager, Message message, NotificationStyle style)
        {
            OxygenMessaging.AddUpdateFlag(message);
            SendMessage(user, messageManager, message, style);
        }
    }
    public class ConsoleMessaging : IMessageManager
    {
        public static readonly ConsoleMessaging Obj = ASingleton<ConsoleMessaging>.Obj;
        public ConsoleMessaging()
        { }
        public void SendMsg(User user, LocString message, NotificationStyle style)
        {
            if (style == NotificationStyle.Error)
            {
                Log.WriteErrorLineLocStr(message.ToString());
            }
            else
            {
                Log.WriteWarningLineLocStr(message.ToString());
            }
        }
    }
    public class Message
    {
        public LocString Str { get; protected set; }
        protected Dictionary<string, object> FlagValues { get; set; } = new Dictionary<string, object>();
        public Message(string str)
        {
            Str = new LocString(str);
        }
        public Message(string str, string flag, object value)
        {
            Str = new LocString(str);
            AddFlag(flag, value);
        }
        public Message(string str, IEnumerable<KeyValuePair<string, object>> flagValues)
        {
            Str = new LocString(str);
            foreach (KeyValuePair<string, object> flagValue in flagValues)
            {
                AddFlag(flagValue.Key, flagValue.Value);
            }
        }
        public Message(LocString s)
        {
            Str = s;
        }
        public Message(LocString str, string flag, object value)
        {
            Str = str;
            AddFlag(flag, value);
        }
        public Message(LocString str, IEnumerable<KeyValuePair<string, object>> flagValues)
        {
            Str = str;
            foreach (KeyValuePair<string, object> flagValue in flagValues)
            {
                AddFlag(flagValue.Key, flagValue.Value);
            }
        }
        private Message(Message m1, Message m2)
        {
            Str = m1.Str + m2.Str;
            foreach (KeyValuePair<string, object> flagValue in m1.FlagValues)
            {
                AddFlag(flagValue.Key, flagValue.Value);
            }
            foreach (KeyValuePair<string, object> flagValue in m2.FlagValues)
            {
                AddFlag(flagValue.Key, flagValue.Value);
            }
        }
        public static implicit operator Message(string s)
        {
            return new Message(s);
        }
        public static implicit operator Message(LocString str)
        {
            return new Message(str);
        }
        public static Message operator +(string s, Message m)
        {
            return new Message(s, m);
        }
        public static Message operator +(Message m, string s)
        {
            return new Message(m, s);
        }
        public static Message operator +(Message m1, Message m2)
        {
            return new Message(m1, m2);
        }
        public bool AddFlag(string flag, object value)
        {
            return FlagValues.TryAdd(flag, value);
        }
        public bool ContainsFlag(string flag)
        {
            return FlagValues.ContainsKey(flag);
        }
        public object GetFlag(string flag)
        {
            return FlagValues[flag];
        }
        public T GetFlag<T>(string flag)
        {
            return (T)GetFlag(flag);
        }
    }

    /*public class OxygenMessagingManager
    {
        public static Message FormattedUserOxygenPercentRemaining(User user)
        {
            if (user == null)
            {
                return "NULL USER ERROR";
            }
            float oxygenRemaining = OxygenManager.OxygenRemaining(user);
            return FormattedOxygenPercentRemaining(oxygenRemaining);
        }
        public static string FormattedOxygenPercentRemaining(float floatPercent)
        {
            return floatPercent.ToString("F0");
        }
        public static int PercentOxygenRemaining(User user, bool roundUp = true)
        {
            if (user == null)
            {
                return 0;
            }
            float oxygenRemaining = OxygenManager.OxygenRemaining(user);
            float oxygenCapacity = OxygenBackpack.OxygenTankCapacity(user);
            if (oxygenCapacity > 0)
            {
                if (roundUp)
                {
                    return (int)Math.Ceiling(oxygenRemaining / oxygenCapacity * 100);
                }
                return (int)(oxygenRemaining / oxygenCapacity * 100);
            }
            return 100;
        }
        public static Message GetFormattedPercentOxygenRemainingForMessage(User user)
        {
            Message percentString = FormattedUserOxygenPercentRemaining(user);
            int percent = PercentOxygenRemaining(user);
            Action onEvaluate = () => { MessageTimeManager.OnPercentMessageSent(user, percent); };
            percentString.OnEvaluate += onEvaluate;
            return percentString;
        }
    }*/
    public class MessageTimeManager
    {
        public static float TimeBetweenNotifications { get; set; } = 20f;
        public static readonly string UPDATE_LAST_MESSAGE_PERCENT_FLAG = "UpdateLastMessagePercent";
        public static readonly string UPDATE_LAST_MESSAGE_TIME_FLAG = "UpdateLastMessageTime";
        protected static readonly UserDictionary<int> LastMessagePercent = new UserDictionary<int>(100);

        protected static readonly UserDictionary<DateTime> LastMessageTime = new UserDictionary<DateTime>((User _) => DateTime.Now);

        public static bool IsNotificationTime(User user, out int percentToShow)
        {
            if (user == null)
            {
                percentToShow = 0;
                return false;
            }
            float oxygenTankCapacity = OxygenBackpack.OxygenTankCapacity(user);
            float oxygenRemaining = OxygenManager.OxygenRemaining(user);
            //if we have no oxygen, send a message every 20s
            if (oxygenRemaining == 0)
            {
                percentToShow = 0;
                return SecondsSinceLastMessage(user) >= TimeBetweenNotifications;
            }
            int[] messagePercents = new int[] { 90, 80, 70, 60, 50, 40, 30, 20, 10, 5, 4, 3, 2, 1, 0 };
            float currentPercent = oxygenRemaining / oxygenTankCapacity * 100;

            IEnumerable<int> percentsToShow = messagePercents.Where(p => currentPercent <= p && p < PercentOfLastMessage(user));
            if (percentsToShow.Count() > 0)
            {
                percentToShow = percentsToShow.Last();
                return true;
            }
            percentToShow = 0;
            return false;
        }
        //public static Dictionary<User, DateTime> NextMessageTime { get; private set; }
        public static void OnMessageSent(User user)
        {
            LastMessageTime[user] = DateTime.Now;
        }
        public static void OnPercentMessageSent(User user, int percent)
        {
            if (LastMessagePercent.ContainsKey(user))
            {
                LastMessagePercent[user] = percent;
            }
            else
            {
                LastMessagePercent.Add(user, percent);
            }
        }
        protected static int PercentOfLastMessage(User user)
        {
            if (LastMessagePercent.ContainsKey(user))
            {
                return LastMessagePercent[user];
            }
            return 100;
        }
        protected static float SecondsSinceLastMessage(User user)
        {
            return TimeOfLastMessage(user).SecondsSince();
        }
        protected static DateTime TimeOfLastMessage(User user)
        {
            if (LastMessageTime.ContainsKey(user))
            {
                return LastMessageTime[user];
            }
            return DateTime.Now;
        }
    }
    public class OxygenBackpackMessagesHandler : CategoryMessageHandler
    {
        public static readonly string TANK_ADDED = "TankAdded";
        public static readonly string TANK_CHANGED = "TankChanged";
        public static readonly string TANK_REMOVED = "TankRemoved";
        protected override Dictionary<string, MessageHandlerMethod> Handlers { get; }
        public OxygenBackpackMessagesHandler()
        {
            Handlers = new Dictionary<string, MessageHandlerMethod>()
            {
                { TANK_ADDED, TankAddedMessage },
                { TANK_CHANGED, TankChangedMessage },
                { TANK_REMOVED, TankRemovedMessage },
            };
        }
        protected void TankAddedMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessage(user, messageManager, "Oxygen tank capacity: " + OxygenBackpack.FormattedOxygenTankCapacity(user), NotificationStyle.InfoBox);
        }
        protected void TankChangedMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessage(user, messageManager, "Old tank emptied. New tank capacity: " + OxygenBackpack.FormattedOxygenTankCapacity(user), NotificationStyle.InfoBox);
        }
        protected void TankRemovedMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessage(user, messageManager, "Oxygen tank removed.\nYou will need to refill whilst wearing your backpack", NotificationStyle.InfoBox);
            SendMessage(user, messageManager, "Oxygen tank removed. You will need to refill whilst wearing your backpack", NotificationStyle.Info);
        }
    }
    public class OxygenLevelOfUserMessagesHandler : CategoryMessageHandler
    {
        public static readonly string CHECK_DEPLETED = "CheckDepleted";
        public static readonly string CHECK_PERCENT = "CheckPercent";
        public static readonly string DEPLETED = "Depleted";
        public static readonly string DEPLETED_NOTIFICATION = "DepletedNotification";
        public static readonly string PERCENT_NOTIFICATION = "PercentNotification";
        protected override Dictionary<string, MessageHandlerMethod> Handlers { get; }
        public OxygenLevelOfUserMessagesHandler()
        {
            Handlers = new Dictionary<string, MessageHandlerMethod>()
            {
                { CHECK_PERCENT, CheckPercentMessage },
                { CHECK_DEPLETED, DepletedNotificationMessage },
                { DEPLETED, DepletedMessage },
                { PERCENT_NOTIFICATION, PercentNotificationMessage },
                { DEPLETED_NOTIFICATION, DepletedNotificationMessage }
            };
        }
        protected void CheckPercentMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessageAsUpdate(user, messageManager, "Oxygen level: " + OxygenMessaging.FormattedOxygenLitresFractionRemaining(OxygenManager.OxygenRemaining(user), OxygenBackpack.OxygenTankCapacity(user)) + "\n" + OxygenMessaging.PercentRemainingMessage(user) + " " + OxygenMessaging.OxygenBatteryBar(user), NotificationStyle.InfoBox);
        }
        protected void DepletedMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                SendMessageAsUpdate(user, messageManager, "Oxygen tank empty!\nRefill your oxygen tank at a waste filter", NotificationStyle.InfoBox);
            }
            else
            {
                SendMessageAsUpdate(user, messageManager, "Oxygen tank not installed! Wear a backpack and refill at a waste filter", NotificationStyle.InfoBox);
            }
        }
        protected void DepletedNotificationMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                SendMessageAsUpdate(user, messageManager, "No Oxygen!\nRefill your oxygen tank at a waste filter", NotificationStyle.InfoBox);
            }
            else
            {
                SendMessageAsUpdate(user, messageManager, "No Oxygen!\nWear a backpack and fill up at a waste filter", NotificationStyle.InfoBox);
            }
        }
        protected override bool IsTimeForNewMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (messageType == PERCENT_NOTIFICATION || messageType == DEPLETED_NOTIFICATION)
            {
                return MessageTimeManager.IsNotificationTime(user, out _);
            }
            return true;
        }
        protected void PercentNotificationMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            MessageTimeManager.IsNotificationTime(user, out int percent);
            SendMessageAsUpdate(user, messageManager, "Oxygen level: " + OxygenMessaging.PercentRemainingMessage(user, percent) + "\n" + OxygenMessaging.OxygenBatteryBar(user), NotificationStyle.InfoBox);
        }
    }
    public class OxygenLocationMessagesHandler : CategoryMessageHandler
    {
        public static readonly string MOVED_INTO_INVALID_ROOM = "MovedOutOfValidRoom";
        public static readonly string MOVED_INTO_VALID_ROOM = "MovedIntoValidRoom";
        public static readonly string MOVED_INTO_ZONE = "MovedIntoZone";
        public static readonly string MOVED_OUT_OF_ZONE = "MovedOutOfZone";
        public static readonly string MOVED_OUTSIDE = "MovedOutside";
        protected override Dictionary<string, MessageHandlerMethod> Handlers { get; }
        public OxygenLocationMessagesHandler()
        {
            Handlers = new Dictionary<string, MessageHandlerMethod>()
            {
                { MOVED_INTO_ZONE, MovedIntoZoneMessage },
                { MOVED_OUT_OF_ZONE, MovedOutOfZoneMessage },
                { MOVED_INTO_VALID_ROOM, MovedIntoValidRoomMessage },
                { MOVED_INTO_INVALID_ROOM, MovedIntoInvalidRoomMessage },
                { MOVED_OUTSIDE, MovedOutsideMessage },
            };
        }
        protected void MovedIntoInvalidRoomMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                SendMessageAsUpdate(user, messageManager, "Room has no oxygen.\nUse command " + string.Format("/{0}", OxygenRoomCheckCommands.SIMPLE_REPORT_COMMAND).Bold() + " for more details.\nOxygen tank on.\n" + OxygenMessaging.PercentRemainingMessage(user) + " remaining " + OxygenMessaging.OxygenBatteryBar(user), NotificationStyle.InfoBox);
            }
            else
            {
                SendMessageAsUpdate(user, messageManager, "Room has no oxygen.\nUse command " + string.Format("/{0}", OxygenRoomCheckCommands.SIMPLE_REPORT_COMMAND).Bold() + " for more details.", NotificationStyle.InfoBox);
            }
        }
        protected void MovedIntoValidRoomMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                SendMessageAsUpdate(user, messageManager, "Oxygen tank off.\n" + OxygenMessaging.PercentRemainingMessage(user) + " remaining " + OxygenMessaging.OxygenBatteryBar(user), NotificationStyle.InfoBox);
            }
            else
            {
                SendMessageAsUpdate(user, messageManager, "Room has oxygen.\nYou can breathe again", NotificationStyle.InfoBox);
            }
        }
        protected void MovedIntoZoneMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                if (OxygenManager.OxygenRemaining(user) > 0)
                {
                    SendMessageAsUpdate(user, messageManager, "Leaving Earth.\nOxygen tank on.\n" + OxygenMessaging.PercentRemainingMessage(user) + " remaining " + OxygenMessaging.OxygenBatteryBar(user), NotificationStyle.InfoBox);
                }
                else
                {
                    SendMessageAsUpdate(user, messageManager, "Leaving Earth.\nOxygen tank empty!", NotificationStyle.InfoBox);
                }
            }
            else
            {
                SendMessageAsUpdate(user, messageManager, "Leaving Earth.\nNo oxygen tank!", NotificationStyle.InfoBox);
            }
        }
        protected void MovedOutOfZoneMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                if (OxygenManager.OxygenRemaining(user) > 0)
                {
                    SendMessageAsUpdate(user, messageManager, "Re-entering Earth.\nOxygen tank off.\n" + OxygenMessaging.PercentRemainingMessage(user) + " remaining " + OxygenMessaging.OxygenBatteryBar(user), NotificationStyle.InfoBox);
                }
                else
                {
                    SendMessageAsUpdate(user, messageManager, "Re-entering Earth.\nOxygen tank empty!", NotificationStyle.InfoBox);
                }
            }
            else
            {
                SendMessageAsUpdate(user, messageManager, "Re-entering Earth.\nYou can breathe again", NotificationStyle.InfoBox);
            }
        }
        protected void MovedOutsideMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                SendMessageAsUpdate(user, messageManager, "Oxygen tank on.\n" + OxygenMessaging.PercentRemainingMessage(user) + " remaining " + OxygenMessaging.OxygenBatteryBar(user), NotificationStyle.InfoBox);
            }
        }
    }
    public class OxygenRoomReportMessagesHandler : CategoryMessageHandler
    {
        public static readonly string DEPRESSURISED = "NoPressure";
        public static readonly string LACKING_FILTERS = "NotEnoughFilters";
        public static readonly string LACKING_POWER = "NotEnoughPower";
        public static readonly string LOW_TIER = "TierTooLow";
        public static readonly string OUTSIDE = "Outside";
        public static readonly string ROOM_REPORT = "RoomReport";
        public static readonly string TIER_REPORT = "TierReport";
        public static readonly string VALID = "Valid";
        protected override Dictionary<string, MessageHandlerMethod> Handlers { get; }
        public OxygenRoomReportMessagesHandler()
        {
            Handlers = new Dictionary<string, MessageHandlerMethod>()
            {
                { ROOM_REPORT, RoomReportMessage },
                { TIER_REPORT, TierReportMessage },
                { VALID, ValidMessage },
                { LOW_TIER, LowTierMessage },
                { OUTSIDE, OutsideMessage },
                { LACKING_POWER, LackingPowerMessage },
                { DEPRESSURISED, DepressurisedMessage },
                { LACKING_FILTERS, LackingFiltersMessage }
            };
        }
        protected void DepressurisedMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            OxygenRoomManager.OxygenRoom oxygenRoom = (OxygenRoomManager.OxygenRoom)parameters[0];// OxygenRoomManager.GetOxygenRoomForUser(user);

            if (oxygenRoom == null)
            {
                SendMessage(user, messageManager, "No Oxygen.\nDepressurised room.", NotificationStyle.Error);
                return;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("No Oxygen.");
            sb.AppendLine("Depressurised.");
            sb.AppendLine();
            sb.AppendLine("Fill in windows at:");
            int numToShow = 2;
            Queue<Vector3i> windows = new Queue<Vector3i>(oxygenRoom.RoomSystem.Windows);
            for (int i = 0; i < Math.Min(numToShow, windows.Count); i++)
            {
                sb.AppendLine(windows.Dequeue().ToString());
            }
            if (windows.Count > 0)
            {
                sb.AppendLine("+ " + windows.Count + " more");
            }
            sb.Append(string.Format("Use command " + string.Format("/{0}", OxygenRoomCheckCommands.FULL_REPORT_COMMAND).Bold() + " for full details."));

            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Error);
            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Info);
        }
        protected void LackingFiltersMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("No Oxygen.");
            sb.AppendLine("Install more filters.");
            sb.Append(string.Format("Use command " + string.Format("/{0}", OxygenRoomCheckCommands.FULL_REPORT_COMMAND).Bold() + " for full details."));

            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Error);
        }
        protected void LackingPowerMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("No Oxygen.");
            sb.AppendLine("Some filters lack power.");
            sb.AppendLine("Use command " + string.Format("/{0}", OxygenRoomCheckCommands.FULL_REPORT_COMMAND).Bold() + " for full details.");

            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Error);
            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Chat);
        }
        protected void LowTierMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            //if it is sufficient to breath purely by increasing room tier
            float currentAirtightnessTier = (float)parameters[0];
            float requiredAirtightnessTier = (float)parameters[1];
            int numRooms = (int)parameters[2];
            //oxygenRoom.RequiredAirtightness(false, out float requiredTier)
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("No oxygen.");
            sb.AppendLine(string.Format("Room has {0} rooms.", numRooms));
            sb.AppendLine("Average airtightness tier: " + string.Format("{0:0.##}", currentAirtightnessTier.RoundDown(2)).Underline().Bold().Color(PositiveColour(currentAirtightnessTier, 0f, 4f).HexRGBA));
            sb.AppendLine("Required airtightness tier: " + string.Format("{0:0.##}", requiredAirtightnessTier.RoundUp(2)).Underline().Bold());
            sb.AppendLine(string.Format("Increase room airtightness tier or install new filters."));
            sb.AppendLine("Use command " + string.Format("/{0}", OxygenRoomCheckCommands.TIER_REPORT_COMMAND).Bold() + " for airtightness tier info.");
            sb.AppendLine("Use command " + string.Format("/{0}", OxygenRoomCheckCommands.FULL_REPORT_COMMAND).Bold() + " for full details.");
            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Error);
            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Chat);
        }
        protected Color NegativeColour(float num, float min = 0f, float max = 1f)
        {
            return PositiveColour(max - num, min, max);
        }
        protected void OutsideMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("No Oxygen.");
            sb.AppendLine("Room may be too large or have too many windows.");
            sb.AppendLine("Only valid rooms can have oxygen.");
            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Error);
        }
        protected Color PositiveColour(float num, float min = 0f, float max = 1f)
        {
            Func<float, float, float, float> normalise = (float f, float f1, float f2) => (f - f1) / (f2 - f1);
            float normalised = normalise(num, min, max);
            SortedDictionary<float, Color> colourThresholds = new SortedDictionary<float, Color>()
            {
                { 0.9f, Color.LightGreen },
                { 0.6f, Color.YellowGreen },
                { 0.3f, Color.Orange },
                { 0.1f, Color.LightRed },
                { 0f, Color.Red }
            };

            return colourThresholds[colourThresholds.Keys.Last(threshold => threshold <= num)];
        }
        protected void RoomReportMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            OxygenRoomManager.RoomStatus roomCheckResult = (OxygenRoomManager.RoomStatus)parameters[0];
            if (roomCheckResult.IsOutside)
            {
                SendMessage(user, messageManager, OxygenMessaging.RoomStatusHeader(roomCheckResult) + "\nOutside or room too large.", NotificationStyle.Chat);
            }
            else
            {
                OxygenRoomManager.OxygenRoom oxygenRoom = (OxygenRoomManager.OxygenRoom)parameters[1];
                if (oxygenRoom == null)
                {
                    SendMessage(user, messageManager, OxygenMessaging.RoomStatusHeader(roomCheckResult) + "\nNo Filters.", NotificationStyle.Chat);
                    return;
                }
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine(OxygenMessaging.RoomStatusHeader(roomCheckResult));
                sb.AppendLine(string.Format("Connected rooms: {0}", oxygenRoom.RoomSystem.Rooms.Count));
                sb.AppendLine(string.Format("Total volume: {0}" + SC.M3, oxygenRoom.Volume));
                sb.AppendLine("Average airtightness tier: " + string.Format("{0:0.##}", oxygenRoom.AirtightnessTier.RoundDown(2)).Color(PositiveColour(oxygenRoom.AirtightnessTier, 0f, 4f)));
                sb.AppendLine("Filter penalty from tier: " + string.Format("{0:0.##}%", (oxygenRoom.AirtightnessPenalty * 100).RoundUp(0)).Color(NegativeColour(oxygenRoom.AirtightnessPenalty)));
                sb.AppendLine(string.Format("Total air supply: {0}" + SC.M3, oxygenRoom.FilteredVolume.RoundDown(0)));
                sb.AppendLine(string.Format("Air lost due to tier: {0}" + SC.M3, Math.Min(oxygenRoom.FilteredVolume.RoundDown(0), (oxygenRoom.FilteredVolume - oxygenRoom.EffectiveFilteredVolume).RoundUp(0))));
                //sb.AppendLine("Effective supply: {0} m" + "3".Sup(),<color={2}><u><b>{0} m<sup>3</sup>/{1} m<sup>3</sup></b></u></color> necessary", oxygenRoom.EffectiveFilteredVolume.RoundDown(0), oxygenRoom.Volume, roomCheckResult.IsValid ? "#AAFFAA" : "#FF6060"));
                sb.AppendLine("Effective supply: " + (oxygenRoom.EffectiveFilteredVolume.RoundDown(0).ToString("F0") + " / " + oxygenRoom.Volume + SC.M3).Underline().Bold().Color(roomCheckResult.IsValid ? "#AAFFAA" : "#FF6060"));
                sb.AppendLine();
                sb.AppendLine(RoomSystemSubRooms(oxygenRoom.RoomSystem.Rooms));
                sb.AppendLine(Windows(oxygenRoom.RoomSystem.Windows));
                sb.Append(FilterNetworks(oxygenRoom));
                SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Chat);
                SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Info);
            }
        }
        protected void TierReportMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            //float averageMaterialTier = (float)parameters[0];
            float averageAirtightnessTier = (float)parameters[1];
            int numRooms = (int)parameters[2];
            IEnumerable<OxygenRoomManager.WallTypeComposition> wallCompositions = (IEnumerable<OxygenRoomManager.WallTypeComposition>)parameters[3];
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Room System Tier Report".Underline().Bold());
            sb.AppendLine(string.Format("Connected rooms: {0}", numRooms));
            //sb.AppendLine(string.Format("Material Tier: {0:0.##}", averageMaterialTier.RoundDown(2)));
            sb.AppendLine("Airtightness Tier: " + string.Format("{0:0.##}", averageAirtightnessTier.RoundDown(2)).Color(PositiveColour(averageAirtightnessTier, 0, 4).HexRGBA));
            sb.Append(WallTiers(wallCompositions));
            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Info);
            SendMessage(user, messageManager, sb.ToString(), NotificationStyle.Chat);
        }
        protected void ValidMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessage(user, messageManager, "Room is valid.\nOxygen available.", NotificationStyle.InfoBox);
        }
        private static string FilterNetworks(OxygenRoomManager.OxygenRoom oxygenRoom)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (oxygenRoom.FilterNetworks.Count > 0)
            {
                sb.AppendLine(string.Format("Connected by {0} filter networks".Underline(), oxygenRoom.FilterNetworks.Count));
                foreach (OxygenRoomManager.PipeNetwork pipeNetwork in oxygenRoom.FilterNetworks)
                {
                    sb.AppendLine(string.Format("\tFilter network with {0} waste filters. Supplies {1:0}" + SC.M3, pipeNetwork.ConnectedWasteFilters.Count, oxygenRoom.Volume * pipeNetwork.Efficiency));
                    foreach (WasteFilterObject wasteFilter in pipeNetwork.ConnectedWasteFilters)
                    {
                        sb.AppendLine(string.Format("\t\t" + wasteFilter.UILink().NotTranslated + " at: {0}. {1}", wasteFilter.Position3i.UILink().NotTranslated, oxygenRoom.PoweredWasteFilters.Contains(wasteFilter) ? "Powered" : "Not powered"));
                    }
                }
            }
            else
            {
                sb.AppendLine("Not connected to any waste filter networks!");
            }
            return sb.ToString();
        }
        private string RoomSystemSubRooms(IEnumerable<RoomStats> rooms)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (rooms.Count() > 0)
            {
                sb.AppendLine("Room System:".Underline());
                foreach (RoomStats roomStats in rooms)
                {
                    Room room = RoomData.Obj.GetRoom(roomStats.EmptySpace.First());
                    if (room.RoomValue == null)
                    {
                        sb.AppendLine("\t" + room.UILink(new LocString("Generic".Color(Color.LightRed))).NotTranslated + " at: " + roomStats.AverageEmptyPos.UILink().NotTranslated + ". Volume: " + roomStats.Volume + SC.M3 + string.Format(". Tier: {0:0.##}", roomStats.AverageTier.RoundDown(2)));
                    }
                    //sb.AppendLine(string.Format("\t-Sub-room at: {0}. Volume: {1} m<sup>3</sup>. Tier: {2:0.##}", room.AverageEmptyPos, room.Volume, room.AverageTier.RoundDown(2)));
                    else
                    {
                        sb.AppendLine("\t" + room.UILink(new LocString(room.RoomValue.Summary.NotTranslated.RemoveSize(20).Trim() + " (" + room.RoomValue.Value + ")")).NotTranslated + " at: " + roomStats.AverageEmptyPos.UILink().NotTranslated + ". Volume: " + roomStats.Volume + SC.M3 + string.Format(". Tier: {0:0.##}", roomStats.AverageTier.RoundDown(2)));
                    }
                }
            }
            else
            {
                sb.AppendLine("No rooms! Something went wrong");
            }

            return sb.ToString();
        }
        private string WallTiers(IEnumerable<OxygenRoomManager.WallTypeComposition> wallCompositions)
        {
            wallCompositions = wallCompositions.OrderBy(wall => wall.BlockItemType.Name);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (OxygenRoomManager.WallTypeComposition wall in wallCompositions)
            {
                sb.AppendLine(string.Format("{0} x {1}. (Tier: {2:0})", wall.Count, wall.BlockItemType.UILink().NotTranslated, wall.AirtightnessTier));
            }
            return sb.ToString();
        }
        private string Windows(IEnumerable<Vector3i> windows)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (windows.Count() > 0)
            {
                sb.AppendLine("Windows:".Underline());
                foreach (Vector3i window in windows)
                {
                    sb.AppendLine(string.Format("\t-Window at: {0}", window.UILink().NotTranslated));
                }
            }
            else
            {
                sb.AppendLine("No windows".Underline());
            }
            return sb.ToString();
        }
    }
    public class OxygenTankRefillMessageHandler : CategoryMessageHandler
    {
        public static readonly string NEEDS_BACKPACK = "NeedsBackpack";
        public static readonly string NEEDS_BARREL = "NeedsBarrel";
        public static readonly string NEEDS_BARREL_AND_POWER = "NeedsBarrelAndPower";
        public static readonly string NEEDS_MORE_POWER = "NeedsMorePower";
        public static readonly string NEEDS_POWER_CONNECTION = "NeedsPowerConnection";
        public static readonly string OXYGEN_FULL = "OxygenFull";
        public static readonly string REFILL_SUCCESS = "RefillSuccess";
        protected override Dictionary<string, MessageHandlerMethod> Handlers { get; }
        public OxygenTankRefillMessageHandler()
        {
            Handlers = new Dictionary<string, MessageHandlerMethod>()
            {
                { REFILL_SUCCESS, RefillSuccessMessage },
                { NEEDS_BARREL, NeedsBarrelMessage },
                { NEEDS_POWER_CONNECTION, NeedsPowerConnectionMessage },
                { NEEDS_MORE_POWER, NeedsMorePowerMessage },
                { NEEDS_BARREL_AND_POWER, NeedsBarrelAndPowerMessage },
                { NEEDS_BACKPACK, NeedsBackpackMessage },
                { OXYGEN_FULL, OxygenFullMessage }
            };
        }

        protected void NeedsBackpackMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessage(user, messageManager, "Wear a backpack to refill oxygen", NotificationStyle.InfoBox);
        }
        protected void NeedsBarrelAndPowerMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessage(user, messageManager, "Connect to a power source and carry empty barrels to refill", NotificationStyle.InfoBox);
        }
        protected void NeedsBarrelMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessage(user, messageManager, "Carry empty barrels to refill your backpack tank", NotificationStyle.InfoBox);
        }
        protected void NeedsMorePowerMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            float supplyRequired = (float)parameters[0];
            SendMessage(user, messageManager, "Grid overloaded. Needs " + string.Format("F0", supplyRequired) + "w free", NotificationStyle.InfoBox);
        }
        protected void NeedsPowerConnectionMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessage(user, messageManager, "Connect this waste filter to a power source", NotificationStyle.InfoBox);
        }
        protected void OxygenFullMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            SendMessageAsUpdate(user, messageManager, OxygenMessaging.AddPercentUpdateFlag("Oxygen full", 100), NotificationStyle.InfoBox);
        }
        protected void RefillSuccessMessage(User user, string messageType, IMessageManager messageManager, params object[] parameters)
        {
            if (OxygenManager.OxygenRemaining(user) == OxygenBackpack.OxygenTankCapacity(user))
            {
                Message message = "Oxygen refilled to max: " + OxygenBackpack.FormattedOxygenTankCapacity(user) + "\n" + OxygenMessaging.PercentRemainingMessage(user) + " " + OxygenMessaging.OxygenBatteryBar(user);
                SendMessageAsUpdate(user, messageManager, message, NotificationStyle.InfoBox);
                SendMessageAsUpdate(user, messageManager, message, NotificationStyle.Info);
            }
            else
            {
                Message message = "Oxygen refilled to " + OxygenMessaging.LitresFractionRemainingMessage(user) + "\n" + OxygenMessaging.PercentRemainingMessage(user) + " " + OxygenMessaging.OxygenBatteryBar(user);
                SendMessageAsUpdate(user, messageManager, message, NotificationStyle.InfoBox);
                SendMessageAsUpdate(user, messageManager, message, NotificationStyle.Info);
            }
        }
        //refilling
        //-needs barrels
        //-needs backpack
        //-needs power
        //-success message and new amount

        //oxygen usage
        //-regular percentage updates
        //-depleted
        //oxygen backpack
        //-backpack added
        //-backpack changed
        //-backpack removed
        //oxygen rooms
        //-enter valid room from other valid room
        //-enter valid room from non-valid room with backpack
        //-enter valid room from non-valid room without backpack
        //-enter non-valid room from valid room with backpack
        //-enter non-valid room from valid room without backpack
        //-enter non-valid room from other non-valid room
        //-enter oxygen zone
        //-leave oxygen zone
        //room check
        //-valid room
        //-low tier
        //-no power
        //-no waste filters
        //-outside
        //-depressurised
        //-room not in zone
    }
    /*public class OxygenTankCheckMessageHandler : Singleton<OxygenTankCheckMessageHandler>,  IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            if (messageType != "Check")
            {
                return;
            }
            if (OxygenManager.OxygenRemaining(user) <= 0)
            {
                messageManager.SendMessage(user, OxygenManager.UpdateType.Depleted, forceMessage);
            }
            else
            {
                messageManager.SendMessage(user, OxygenManager.UpdateType.Used, forceMessage);
            }
        }
    }
    public class OxygenTankValidMessageHandler : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                messageManager.SendMsgLocStr(user, "Pressurised Room.\nOxygen tank off.\n" + messageManager.GetFormattedPercentOxygenRemainingForMessage(user) + " remaining", NotificationStyle.InfoBox);
            }
            else
            {
                user.MsgLocStr("Pressurised Room.\nYou can breathe again", NotificationStyle.InfoBox);
            }
            messageManager.LastMessageTime[user] = DateTime.Now;
            messageManager.LastMessagePercent[user] = messageManager.PercentOxygenRemaining(user);
            messageManager.NextMessageTime[user] = DateTime.Now.AddSeconds(messageManager.SecondsUntilNextMessage(user));
        }
    }
    public class OxygenTankDepressurisedMessageHandler : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            OxygenRoomManager.OxygenRoom oxygenRoom = OxygenRoomManager.GetOxygenRoomForUser(user);
            if (OxygenBackpack.WearingBackpack(user))
            {
                user.MsgLocStr("Depressurised Room (" + oxygenRoom.RoomSystem.Windows.Count + " windows).\nOxygen tank on.\n" + messageManager.GetFormattedPercentOxygenRemainingForMessage(user) + " remaining", NotificationStyle.InfoBox);
            }
            else
            {
                user.MsgLocStr("Depressurised Room (" + oxygenRoom.RoomSystem.Windows.Count + " windows)", NotificationStyle.InfoBox);
            }
            messageManager.LastMessageTime[user] = DateTime.Now;
            messageManager.LastMessagePercent[user] = messageManager.PercentOxygenRemaining(user);
            messageManager.NextMessageTime[user] = DateTime.Now.AddSeconds(messageManager.SecondsUntilNextMessage(user));
        }
    }
    public class OxygenTankNotHighTierMessageHandler : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            oxygenRoom = OxygenRoomManager.GetOxygenRoomForUser(user);
            //if it is sufficient to breath purely by increasing room tier
            if (oxygenRoom.RequiredAirtightness(false, out float requiredTier))
            {
                if (OxygenBackpack.WearingBackpack(user))
                {
                    user.MsgLocStr("Room tier too low.\nUse command /atm2 for details.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                }
                else
                {
                    user.MsgLocStr("Room tier too low.\nNeeds Tier " + requiredTier + " with currently powered filters, or connect more powered waste filters.", NotificationStyle.InfoBox);
                }
            }
            //or if more filters need power, even when the room tier is maximised
            else
            {
                if (OxygenBackpack.WearingBackpack(user))
                {
                    user.MsgLocStr("Power more filters.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                }
                else
                {
                    user.MsgLocStr("Power more filters", NotificationStyle.InfoBox);
                }
            }
            LastMessageTime[user] = DateTime.Now;
            LastMessagePercent[user] = PercentOxygenRemaining(user);
            NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
            break;
        }
    }
    public class OxygenTankOutsideMessageHandler : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                user.MsgLocStr("Oxygen tank on. " + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);

                LastMessageTime[user] = DateTime.Now;
                LastMessagePercent[user] = PercentOxygenRemaining(user);
                NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
            }
        }
    }
    public class OxygenTankNoWasteFilterMessageHandler : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                user.MsgLocStr("No waste filter in the room.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
            }
            else
            {
                user.MsgLocStr("No waste filter in the room", NotificationStyle.InfoBox);
            }
            LastMessageTime[user] = DateTime.Now;
            LastMessagePercent[user] = PercentOxygenRemaining(user);
            NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
        }
    }
    public class OxygenTankNoPowerMessageHandler : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                user.MsgLocStr("Waste filter has no power.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
            }
            else
            {
                user.MsgLocStr("Waste filter has no power", NotificationStyle.InfoBox);
            }
            LastMessageTime[user] = DateTime.Now;
            LastMessagePercent[user] = PercentOxygenRemaining(user);
            NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
        }
    }
    public class OxygenTankMovedIntoBoundsMessageHandler : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            if (OxygenBackpack.WearingBackpack(user))
            {
                if (OxygenRemaining(user) > 0)
                {
                    user.MsgLocStr("Leaving Earth.\nOxygen tank on.\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                }
                else
                {
                    user.MsgLocStr("Leaving Earth.\nOxygen tank empty!\n" + FormattedPercentOxygenRemaining(user) + " remaining", NotificationStyle.InfoBox);
                }
                LastMessagePercent[user] = PercentOxygenRemaining(user);
            }
            else
            {
                user.MsgLocStr("Leaving Earth.\nNo oxygen tank!", NotificationStyle.InfoBox);
            }
            LastMessageTime[user] = DateTime.Now;
            NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
        }
    }
    public class OxygenTankTankAddedMessageHandler : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            user.MsgLocStr("Oxygen tank capacity: " + OxygenBackpack.OxygenTankCapacity(user), NotificationStyle.InfoBox);
            LastMessageTime[user] = DateTime.Now;
            NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
        }
    }
    public class OxygenTankTankChangedMessageHandler : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            user.MsgLocStr("Old tank emptied. New tank capacity: " + OxygenBackpack.OxygenTankCapacity(user), NotificationStyle.InfoBox);
            LastMessageTime[user] = DateTime.Now;
            NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
        }
    }
    public class OxygenTankTankRemoved : IMessageHandler
    {
        public void Send(User user, string messageType, MessageManager messageManager, bool forceMessage)
        {
            user.MsgLocStr("Oxygen tank removed.\nYou will need to refill whilst wearing your backpack", NotificationStyle.InfoBox);
            user.MsgLocStr("Oxygen tank removed. You will need to refill whilst wearing your backpack", NotificationStyle.Info);
            LastMessageTime[user] = DateTime.Now;
            LastMessagePercent[user] = PercentOxygenRemaining(user);
            NextMessageTime[user] = DateTime.Now.AddSeconds(SecondsUntilNextMessage(user));
        }
    }*/
    public class UserMessaging : IMessageManager
    {
        public delegate void SentMessageAction(User user, string message, NotificationStyle style);
        public static readonly UserMessaging Obj = ASingleton<UserMessaging>.Obj;
        public UserMessaging()
        { }
        public void SendMsg(User user, LocString message, NotificationStyle style)
        {
            if (user == null)
            {
                return;
            }
            user.Msg(message, style);

            //OnMessageSent(user, message, style);
        }
        public void SendMsgLocStr(User user, string message, NotificationStyle style)
        {
            if (user == null)
            {
                return;
            }
            SendMsg(user, new LocString(message), style);

            //OnMessageSent(user, message, style);
        }
    }
}