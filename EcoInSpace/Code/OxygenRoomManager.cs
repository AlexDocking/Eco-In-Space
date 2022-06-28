namespace EcoInSpace
{
    using Eco.Core.Utils;
    using Eco.Core.Utils.Async;
    using Eco.Core.Utils.Threading;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.GameActions;
    using Eco.Gameplay.Objects;
    using Eco.Gameplay.Pipes.LiquidComponents;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Property;
    using Eco.Gameplay.Rooms;
    using Eco.Mods.TechTree;
    using Eco.Shared.Math;
    using Eco.Shared.Utils;
    using Eco.World;
    using Eco.World.Blocks;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Used for checking if a user is in a room with oxygen available.
    /// </summary>
    public static partial class OxygenRoomManager
    {
        /// <summary>
        /// Fired when the user moves to a room with a different status, or the status of the room changes.
        /// args: User, current location status, new location status
        /// </summary>
        public static ThreadSafeAction<User, RoomStatus, RoomStatus> OnOxygenLocationStatusChangedEvent
        {
            get
            {
                return UsersOxygenLocationStatus.OnValueChanged;
            }
        }
        public static IBounds OxygenRoomsZone
        {
            get
            {
                return Settings.GetSpace();
            }
        }
        // = new ThreadSafeAction<User, RoomStatus, RoomStatus>();
        /// <summary>
        /// AverageEmptyPos of actual room. OxygenRoom is belongs to, if any
        /// </summary>
        private static Dictionary<Vector3i, OxygenRoom> OxygenRoomsByLocation { get; set; } = new Dictionary<Vector3i, OxygenRoom>();
        private static readonly object queueRecalculationLock = new object();
        private static readonly object recalculationLock = new object();
        private static readonly UserDictionary<RoomStatus> UsersOxygenLocationStatus = new UserDictionary<RoomStatus>((User user) => CheckRoom(user));
        private static Task queuedRecalculationTask = null;
        public static RoomStatus CheckRoom(User user)
        {
            RoomStats currentRoom = (user.LoggedIn && user.CurrentRoom != null) ? user.CurrentRoom.RoomStats : FindRoomStat(user.Position.Round);
            if (IsOutside(currentRoom))//.Contained || currentRoom.Volume == 0)
            {
                return RoomStatus.Outside;
            }
            //LogRoomInfo(currentRoom);
            OxygenRoom oxygenRoom = GetOxygenRoomFromActualRoom(currentRoom);
            RoomStatus status = RoomStatus.Valid;
            if (oxygenRoom == null)
            {
                if (currentRoom.Windows.Count > 0)
                {
                    status += RoomStatus.Depressurised;
                }
                status += RoomStatus.LackingFilters;
                return status;
            }
            if (oxygenRoom.Breathable)
            {
                return RoomStatus.Valid;
            }
            if (!oxygenRoom.IsPressurised)
            {
                return RoomStatus.Depressurised;
            }

            int requiredFilteredVolume = oxygenRoom.Volume;
            int maximumFilteredVolumeAndT4 = oxygenRoom.ConnectedWasteFilters.Count * OxygenRoom.FilteredVolumePerFilter;
            int maximumFilteredVolumeFromPoweredAndT4 = oxygenRoom.PoweredWasteFilters.Count * OxygenRoom.FilteredVolumePerFilter;

            //if there are enough powered filters => low tier
            //if there not enough powered filters, enough total filters => no power
            //else need more filters
            if (requiredFilteredVolume <= maximumFilteredVolumeFromPoweredAndT4)
            {
                status += RoomStatus.LowTier;
            }
            else if (requiredFilteredVolume <= maximumFilteredVolumeAndT4)
            {
                status += RoomStatus.LackingPower;
            }
            else
            {
                status += RoomStatus.LackingFilters;
            }
            if (status.IsValid)
            {
                Log.WriteErrorLineLocStr("Something went wrong with room status check but didn't find what it was. M3 Required: " + requiredFilteredVolume + ", Maximum with all powered and T4: " + maximumFilteredVolumeAndT4 + ", Maximum from powered and T4:" + maximumFilteredVolumeFromPoweredAndT4);
            }
            return status;
        }
        public static IEnumerable<WasteFilterObject> FindRelevantWasteFilters()
        {
            WorldObjectManager worldObjectManager = new WorldObjectManager();
            IEnumerable<WasteFilterObject> allWasteFilters = worldObjectManager.All.OfType<WasteFilterObject>();
            IEnumerable<WasteFilterObject> wasteFiltersOnMars = allWasteFilters.Where(filter => OxygenRoomsZone.InBounds(filter.Position3i));
            return wasteFiltersOnMars;
        }
        public static OxygenRoom GetOxygenRoomForUser(User user)
        {
            if (user == null)
            {
                return null;
            }

            RoomStats currentRoom = user.LoggedIn ? user.CurrentRoom.RoomStats : FindRoomStat(user.Position.Round);
            return GetOxygenRoomFromActualRoom(currentRoom);
        }
        public static OxygenRoom GetOxygenRoomFromActualRoom(RoomStats room)
        {
            if (room == null)
            {
                return null;
            }
            if (!OxygenRoomsByLocation.ContainsKey(room.AverageEmptyPos))
            {
                return null;
            }
            return OxygenRoomsByLocation[room.AverageEmptyPos];
        }
        public static void Initialize()
        {
            RecalculateOxygenRoomsAsync();
            //recalculate the rooms every 10 seconds. It's easier to do this for now than to listen for every type of event which could change the rooms or filter networks
            IntervalActionWorker updateTicker = PeriodicWorkerFactory.CreateWithInterval(TimeSpan.FromSeconds(10f), () =>
            {
                RecalculateOxygenRoomsAsync();
            });
            updateTicker.Start();
        }

        /// <summary>
        /// When the player moves into a room with a different oxygen status, such as from depressurised to low power, or valid to outside, send a message to the player
        /// </summary>
        /// <param name="user"></param>
        /// <param name="previousLocation"></param>
        /// <param name="newLocation"></param>
        public static void OnOxygenLocationChanged(User user, RoomStatus previousLocation, RoomStatus newLocation)
        {
            if (user == null)
            {
                return;
            }
            if (!OxygenRoomsZone.InBounds(user.Position))
            {
                return;
            }
            if (newLocation.IsValid)
            {
                MessageManager.SendMessage(user, OxygenLocationMessagesHandler.MOVED_INTO_VALID_ROOM);
            }
            else if (newLocation.IsOutside)
            {
                MessageManager.SendMessage(user, OxygenLocationMessagesHandler.MOVED_OUTSIDE);
            }
            else if (newLocation.IsInvalid)
            {
                MessageManager.SendMessage(user, OxygenLocationMessagesHandler.MOVED_INTO_INVALID_ROOM);
            }
        }

        public static bool OxygenAvailable(User user)
        {
            return CheckRoom(user).IsValid;
        }

        /// <summary>
        /// Starting with the waste filters on Mars, find all the rooms they're connected to and the oxygen status of those.
        /// WARNING: Don't run synchronously on plugin initialization - can cause server to crash on startup! Call async version or use Task.Run instead
        /// </summary>
        public static void RecalculateOxygenRooms()
        {
            lock (recalculationLock)
            {
                queuedRecalculationTask = null;

                Dictionary<Vector3i, OxygenRoom> newOxygenRoomsByLocation = new Dictionary<Vector3i, OxygenRoom>();

                IEnumerable<WasteFilterObject> wasteFilters = FindRelevantWasteFilters();
                Dictionary<WasteFilterObject, List<Ray>> wasteFilterPipeEnds = FindEndPipes(wasteFilters);
                List<PipeNetwork> networks = new List<PipeNetwork>();
                foreach (KeyValuePair<WasteFilterObject, List<Ray>> entry in wasteFilterPipeEnds)
                {
                    WasteFilterObject wasteFilter = entry.Key;
                    List<Ray> pipeEnds = entry.Value;

                    //Find existing networks with the same output pipes. Search on end pipe positions as it is a struct; rays are classes and might fail the equality checks
                    List<PipeNetwork> pipeNetworks = networks.Where(network => network.PipeEndPositions.ContainsAny(pipeEnds.Select(p => p.Pos))).ToList();
                    if (pipeNetworks.Count == 0)
                    {
                        PipeNetwork newNetwork = new PipeNetwork(new List<WasteFilterObject>() { wasteFilter }, pipeEnds);
                        networks.Add(newNetwork);
                    }
                    else
                    {
                        PipeNetwork merged = pipeNetworks[0];
                        List<PipeNetwork> subsequent = pipeNetworks.Skip(1).ToList();
                        foreach (PipeNetwork toMerge in subsequent)
                        {
                            merged.ConnectedWasteFilters.AddUniqueRange(toMerge.ConnectedWasteFilters);
                            merged.PipeEnds.AddUniqueRange(toMerge.PipeEnds);
                            networks.Remove(toMerge);
                        }
                        merged.ConnectedWasteFilters.Add(wasteFilter);
                        merged.PipeEnds.AddUniqueRange(pipeEnds);
                    }
                }
                foreach (PipeNetwork network in networks)
                {
                    network.RecalculateRooms();
                }
                //go through all the of the piped rooms and make oxygen rooms of them with their neighbours
                foreach (PipeNetwork network in networks)
                {
                    foreach (RoomStats room in network.VirtualRoom.Rooms)
                    {
                        OxygenRoom existingOxygenRoom = newOxygenRoomsByLocation.Values.FirstOrDefault(oxygenRoom => oxygenRoom.RoomSystem.ContainsRoom(room));
                        if (existingOxygenRoom != null)
                        {
                            newOxygenRoomsByLocation.TryAdd(room.AverageEmptyPos, existingOxygenRoom);
                            existingOxygenRoom.FilterNetworks.AddUnique(network);
                        }
                        else
                        {
                            OxygenRoom newOxygenRoom = new OxygenRoom(new ConnectedRoom(room));
                            newOxygenRoom.FilterNetworks.AddUnique(network);
                            newOxygenRoomsByLocation.TryAdd(room.AverageEmptyPos, newOxygenRoom);
                        }
                    }
                }
                //go through all of the rooms which have either a direct pipe connection or via an adjoining room
                IEnumerable<OxygenRoom> oxygenRooms = newOxygenRoomsByLocation.Values.Distinct().ToList();
                foreach (OxygenRoom oxygenRoom in oxygenRooms)
                {
                    foreach (RoomStats room in oxygenRoom.RoomSystem.Rooms)
                    {
                        newOxygenRoomsByLocation.TryAdd(room.AverageEmptyPos, oxygenRoom);
                    }
                }
                OxygenRoomsByLocation = newOxygenRoomsByLocation;
                RecheckAllUserLocation();
            }
        }
        public static Task RecalculateOxygenRoomsAsync()
        {
            lock (queueRecalculationLock)
            {
                Task currentRecalculationTask = queuedRecalculationTask;
                if (currentRecalculationTask == null)
                {
                    Task newRecalculationTask = Task.Run(RecalculateOxygenRooms);
                    queuedRecalculationTask = newRecalculationTask;
                    return newRecalculationTask;
                }
                return currentRecalculationTask;
            }
        }
        public static void RecheckAllUserLocation()
        {
            foreach (User user in UserManager.OnlineUsers)
            {
                RecheckUserLocation(user);
            }
        }
        /// <summary>
        /// When a user moves, fire the event for when the player moves to a different type room, e.g. from valid into depressurised, or low tier to outside
        /// </summary>
        /// <param name="user"></param>
        public static void RecheckUserLocation(User user)
        {
            if (user == null)
            {
                return;
            }
            UsersOxygenLocationStatus[user] = CheckRoom(user);
        }
        public static bool WasteFilterHasPower(WasteFilterObject wasteFilter)
        {
            return Power.WorldObjectHasPower(wasteFilter);
        }
        public static bool WasteFilterIsEnabled(WasteFilterObject wasteFilter)
        {
            return wasteFilter.Enabled;
        }
        private static Dictionary<WasteFilterObject, List<Ray>> FindEndPipes(IEnumerable<WasteFilterObject> wasteFilters)
        {
            Dictionary<WasteFilterObject, List<Ray>> pipeOutputs = new Dictionary<WasteFilterObject, List<Ray>>();
            foreach (WasteFilterObject wasteFilter in wasteFilters)
            {
                if (wasteFilter == null)
                {
                    continue;
                }

                Ray startRay = WasteFilterOutputPipeRay(wasteFilter).FirstRay;

                List<Ray> pipeOutputRays = PipeSearch.FindPipeEnds(startRay).ToList();
                pipeOutputs.Add(wasteFilter, pipeOutputRays);
            }
            return pipeOutputs;
        }
        private static Dictionary<WasteFilterObject, List<Vector3i>> FindPipeOutputs(IEnumerable<WasteFilterObject> wasteFilters)
        {
            Dictionary<WasteFilterObject, List<Vector3i>> pipeOutputs = new Dictionary<WasteFilterObject, List<Vector3i>>();
            foreach (WasteFilterObject wasteFilter in wasteFilters)
            {
                List<Vector3i> outputs = new List<Vector3i>();
                LiquidProducerComponent outputComponent = wasteFilter.Components.FirstOrDefault(c => c is LiquidProducerComponent) as LiquidProducerComponent;
                foreach (Ray outputRay in outputComponent.OutputPipe.CachedOpenEnds.ToList())
                {
                    outputs.Add(outputRay.FirstPos);
                }
            }
            return pipeOutputs;
        }
        private static Room FindRoom(Vector3i position)
        {
            return RoomData.Obj.GetRoom(World.GetWrappedWorldPosition(position));
        }
        private static Dictionary<WasteFilterObject, List<Vector3i>> FindRoomPositionsForWasteFilters(IEnumerable<WasteFilterObject> wasteFilters)
        {
            Dictionary<WasteFilterObject, List<Vector3i>> allWasteFilterPipeOutputPositions = FindPipeOutputs(wasteFilters);
            Dictionary<WasteFilterObject, List<Vector3i>> wasteFilterConnectedRoomPositions = new Dictionary<WasteFilterObject, List<Vector3i>>();
            foreach (KeyValuePair<WasteFilterObject, List<Vector3i>> wasteFilterPipeOutputPositions in allWasteFilterPipeOutputPositions)
            {
                WasteFilterObject wasteFilter = wasteFilterPipeOutputPositions.Key;
                List<Vector3i> outputPositions = wasteFilterPipeOutputPositions.Value;
                wasteFilterConnectedRoomPositions.Add(wasteFilter, FindRoomStats(outputPositions).Values.Select(room => room.AverageEmptyPos).ToList());
            }
            return wasteFilterConnectedRoomPositions;
        }
        private static Dictionary<Vector3i, Room> FindRooms(IEnumerable<Vector3i> positions)
        {
            Dictionary<Vector3i, Room> rooms = new Dictionary<Vector3i, Room>();
            foreach (Vector3i position in positions)
            {
                Room room = FindRoom(position);
                rooms.TryAdd(position, room);
            }
            return rooms;
        }
        private static RoomStats FindRoomStat(Vector3i position)
        {
            return RoomChecker.GetRoomStats(Room.Query(WrappedWorldPosition3i.Create(position.x, position.y, position.z)));
        }
        private static Dictionary<Vector3i, RoomStats> FindRoomStats(IEnumerable<Vector3i> positions)
        {
            Dictionary<Vector3i, RoomStats> rooms = new Dictionary<Vector3i, RoomStats>();
            foreach (Vector3i position in positions)
            {
                RoomStats roomStats = FindRoomStat(position);
                rooms.TryAdd(position, roomStats);
            }
            return rooms;
        }
        private static bool IsOutside(RoomStats room)
        {
            if (room == null)
            {
                return true;
            }
            return SameRooms(room, Room.Global.RoomStats);//!room.Contained || room.Volume == 0 || room.AverageEmptyPos == Vector3i.Zero ||
        }
        private static bool IsOutside(Room room)
        {
            return room == RoomData.Obj.GlobalRoom;
        }
        private static bool SameRooms(RoomStats room1, RoomStats room2)
        {
            if (room1.AverageEmptyPos != room2.AverageEmptyPos)
            {
                return false;
            }
            if (room1.Volume != room2.Volume)
            {
                return false;
            }
            if (!room1.EmptySpace.SetEquals(room2.EmptySpace))
            {
                return false;
            }
            return true;
        }

        private static Ray WasteFilterOutputPipeRay(WasteFilterObject wasteFilter)
        {
            Vector3i pos;
            Direction dir;

            if (wasteFilter.Rotation.Forward == Vector3i.Back)
            {
                pos = wasteFilter.Position3i + new Vector3i(2, 0, 2);
                dir = Direction.Right;
            }
            else if (wasteFilter.Rotation.Forward == Vector3i.Left)
            {
                pos = wasteFilter.Position3i + new Vector3i(2, 0, -2);
                dir = Direction.Back;
            }
            else if (wasteFilter.Rotation.Forward == Vector3i.Forward)
            {
                pos = wasteFilter.Position3i + new Vector3i(-2, 0, -2);
                dir = Direction.Left;
            }
            else
            {
                pos = wasteFilter.Position3i + new Vector3i(-2, 0, 2);
                dir = Direction.Forward;
            }
            return new Ray(pos, dir);
        }

        public struct RoomStatus
        {
            public bool IsDepressurised { get; }
            public bool IsInvalid { get { return !IsValid; } }
            public bool IsLackingFilters { get; }
            public bool IsLackingPower { get; }
            public bool IsLowTier { get; }
            public bool IsOutside { get; }
            public bool IsValid
            {
                get
                {
                    return !IsOutside && !IsDepressurised && !IsLowTier && !IsLackingFilters && !IsLackingPower;
                }
            }
            public static readonly RoomStatus Depressurised = new RoomStatus(depressurised: true);
            public static readonly RoomStatus LackingFilters = new RoomStatus(lackingFilters: true);
            public static readonly RoomStatus LackingPower = new RoomStatus(lackingPower: true);
            public static readonly RoomStatus LowTier = new RoomStatus(lowTier: true);
            public static readonly RoomStatus Outside = new RoomStatus(outside: true);
            public static readonly RoomStatus Valid = new RoomStatus();
            public RoomStatus(bool outside = false, bool depressurised = false, bool lowTier = false, bool lackingFilters = false, bool lackingPower = false)
            {
                this.IsOutside = outside;
                this.IsDepressurised = depressurised;
                this.IsLowTier = lowTier;
                this.IsLackingFilters = lackingFilters;
                this.IsLackingPower = lackingPower;
            }
            public static bool operator !=(RoomStatus s1, RoomStatus s2)
            {
                return !(s1 == s2);
            }
            public static RoomStatus operator +(RoomStatus s1, RoomStatus s2)
            {
                return new RoomStatus(s1.IsOutside || s2.IsOutside, s1.IsDepressurised || s2.IsDepressurised, s1.IsLowTier || s2.IsLowTier, s1.IsLackingFilters || s2.IsLackingFilters, s1.IsLackingPower || s2.IsLackingPower);
            }
            public static bool operator ==(RoomStatus s1, RoomStatus s2)
            {
                return s1.Equals(s2);
            }
            public override bool Equals(object obj)
            {
                if (!(obj is RoomStatus))
                {
                    return false;
                }
                RoomStatus other = (RoomStatus)obj;
                if (this.IsOutside != other.IsOutside)
                {
                    return false;
                }
                if (this.IsDepressurised != other.IsDepressurised)
                {
                    return false;
                }
                if (this.IsLowTier != other.IsLowTier)
                {
                    return false;
                }
                if (this.IsLackingFilters != other.IsLackingFilters)
                {
                    return false;
                }
                if (this.IsLackingPower != other.IsLackingPower)
                {
                    return false;
                }
                return true;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(IsOutside, IsDepressurised, IsLowTier, IsLackingFilters, IsLackingPower);
            }

            public override string ToString()
            {
                List<string> statuses = new List<string>();
                if (IsValid)
                {
                    statuses.Add("Valid");
                }
                if (IsOutside)
                {
                    statuses.Add("Outside");
                }
                if (IsDepressurised)
                {
                    statuses.Add("Depressurised");
                }
                if (IsLowTier)
                {
                    statuses.Add("Low Tier");
                }
                if (IsLackingFilters)
                {
                    statuses.Add("Lacking Filters");
                }
                if (IsLackingPower)
                {
                    statuses.Add("Lacking Power");
                }
                return string.Join(", ", statuses);
            }
        }

        public struct WallTypeComposition
        {
            public float Airtightness { get { return Wall.Airtightness; } }
            public float AirtightnessTier { get { return Wall.AirtightnessTier; } }
            public Type BlockItemType { get { return Wall.BlockItemType; } }
            public int Count { get; }
            public string FriendlyName { get { return Wall.FriendlyName; } }
            public float Tier { get { return Wall.Tier; } }
            public Wall Wall { get; }
            public WallTypeComposition(Wall wall, int count)
            {
                this.Wall = wall;
                this.Count = count;
            }
            public WallTypeComposition(RoomStats.WallComposition wallComposition, int count) : this(new Wall(wallComposition), count)
            {
            }
            public WallTypeComposition(Type blockItemType, float tier, int count) : this(new Wall(blockItemType, tier), count)
            {
            }
        }
        /// <summary>
        /// Unfinished
        /// </summary>
        public class PossibleChangeListener : IGameActionAware
        {
            public void ActionPerformed(GameAction action)
            {
                if (!(action is PlaceOrPickUpObject))
                {
                    return;
                }
                PlaceOrPickUpObject placeOrPickUpAction = (PlaceOrPickUpObject)action;
                if (!(placeOrPickUpAction.WorldObjectItem is WasteFilterItem))
                {
                    return;
                }
                if (!OxygenRoomsZone.InBounds(placeOrPickUpAction.ActionLocation))
                {
                    return;
                }
                OxygenRoomManager.RecalculateOxygenRoomsAsync();
            }

            public LazyResult ShouldOverrideAuth(GameAction action)
            {
                throw new NotImplementedException();
            }
        }
        public class Wall
        {
            public float Airtightness
            {
                get
                {
                    return GetAirtightness(AirtightnessTier);
                }
            }
            public float AirtightnessTier
            {
                get
                {
                    return GetAirtightnessTier(BlockItemType, Tier);
                }
            }
            public string FriendlyName
            { get { return BlockItemType.Name.TrimEndString("Item").AddSpacesBetweenCapitals(); } }
            public Type BlockItemType;
            public float Tier;
            public Wall(RoomStats.WallComposition wallComposition)
            {
                this.BlockItemType = wallComposition.BlockItemType;
                this.Tier = wallComposition.Tier;
            }
            public Wall(Type blockItemType, float tier)
            {
                this.BlockItemType = blockItemType;
                this.Tier = tier;
            }
            public static float GetAirtightness(float airtightnessTier)
            {
                //T4:1^2   = 1
                //T3:0.8^2 = 0.64
                //T2:0.6^2 = 0.36
                //T1:0.4^2 = 0.16
                //T0:0.2^2 = 0.04 => 25x as much filtering capacity needed as T4
                return (float)Math.Pow(0.2f + 0.8f * airtightnessTier / 4, 2);
            }
            public static float GetAirtightnessTier(Type blockItemType, float tier)
            {
                Dictionary<Type, float> tierOverrides = new Dictionary<Type, float>()
                {
                    { typeof(SandstoneItem), 2f },
                    { typeof(GraniteItem), 2f },
                    { typeof(BasaltItem), 2f },
                    { typeof(GneissItem), 2f },
                    { typeof(ShaleItem), 2f },
                    { typeof(LimestoneItem), 2f },
                    { typeof(IronOreItem), 2f },
                    { typeof(CopperOreItem), 2f },
                    { typeof(GoldOreItem), 2f },
                    { typeof(LumberItem), 1f },
                    { typeof(SoftwoodLumberItem), 1f },
                    { typeof(HardwoodLumberItem), 1f },
                    { typeof(HewnLogItem), 0f },
                    { typeof(SoftwoodHewnLogItem), 0f },
                    { typeof(HardwoodHewnLogItem), 0f },
                    { typeof(IronPipeItem), 0f },
                    { typeof(CopperPipeItem), 0f },
                    { typeof(SteelPipeItem), 0f },
                    { typeof(CompositeBirchLumberItem), 3f },
                    { typeof(CompositeOakLumberItem), 3f },
                    { typeof(CompositeJoshuaLumberItem), 3f },
                    { typeof(CompositeSpruceLumberItem), 3f },
                    { typeof(CompositeSaguaroLumberItem), 3f },
                    { typeof(CompositePalmLumberItem), 3f },
                    { typeof(CompositeCeibaLumberItem), 3f },
                    { typeof(CompositeRedwoodLumberItem), 3f },
                    { typeof(CompositeFirLumberItem), 3f },
                    { typeof(CompositeLumberItem), 3f },
                    { typeof(CompositeCedarLumberItem), 3f },
                    { typeof(BrickItem), 1f },
                    { typeof(AsphaltConcreteItem), 2f },
                    { typeof(CorrugatedSteelItem), 4f },
                    { typeof(ReinforcedConcreteItem), 4f },
                    { typeof(CottonCarpetItem), 3f },
                    { typeof(NylonCarpetItem), 3f },
                    { typeof(WoolCarpetItem), 3f }
                };
                if (tierOverrides.ContainsKey(blockItemType))
                {
                    return tierOverrides[blockItemType];
                }
                return tier;
            }
        }
        private static class PipeSearch
        {
            public static IEnumerable<Ray> FindPipeEnds(Ray start)
            {
                Block pipeBlock = World.GetBlock(start.Pos);
                Type pipeType = pipeBlock.GetType();
                if (pipeType == typeof(EmptyBlock))
                {
                    return new List<Ray>() { start };
                }
                else if (!(pipeBlock is Eco.Gameplay.Pipes.PipeBlock))
                {
                    return new List<Ray>();
                }
                List<Ray> openEnds = new List<Ray>();
                Queue<Ray> frontier = new Queue<Ray>();
                frontier.Enqueue(start);
                HashSet<Vector3i> visitedPositions = new HashSet<Vector3i>();
                while (frontier.Count > 0)
                {
                    Ray pipeRay = frontier.Dequeue();

                    if (visitedPositions.Contains(pipeRay.Pos))
                    {
                        continue;
                    }
                    IEnumerable<Ray> neighbours = GetPipeNeighbours(pipeRay, pipeType);
                    if (neighbours.Count() == 0 && World.GetBlock(pipeRay.FirstPos).GetType() == typeof(EmptyBlock))
                    {
                        openEnds.Add(pipeRay);
                    }
                    foreach (Ray neighbour in neighbours)
                    {
                        frontier.Enqueue(neighbour);
                    }
                    visitedPositions.Add(pipeRay.Pos);
                }
                return openEnds;
            }
            private static IEnumerable<Ray> GetNeighbours(Ray currentEnd)
            {
                List<Ray> neighbours = new List<Ray>(currentEnd.Pos.XZNeighborsAndDir)
                {
                    new Ray(World.GetWrappedWorldPosition(currentEnd.Pos + Vector3i.Up), Direction.Up),
                    new Ray(World.GetWrappedWorldPosition(currentEnd.Pos + Vector3i.Down), Direction.Down)
                };
                neighbours.RemoveFirst(ray => ray.Dir.Inverse() == currentEnd.Dir);
                return neighbours;
            }
            private static IEnumerable<Ray> GetPipeNeighbours(Ray currentEnd, Type pipeType)
            {
                return GetNeighbours(currentEnd).Where(ray => World.GetBlock(ray.Pos).GetType() == pipeType);
            }
        }
    }
}