namespace EcoInSpace
{
    using Eco.Core.Utils.Async;
    using Eco.Gameplay.GameActions;
    using Eco.Gameplay.Property;
    using Eco.Mods.TechTree;
    using Eco.Shared.Math;
    using Eco.Shared.Utils;
    using System.Collections.Generic;
    using System.Linq;

    public static partial class OxygenRoomManager
    {
        public class PipeNetwork
        {
            public List<WasteFilterObject> ConnectedWasteFilters
            { get { return connectedWasteFilters; } set { connectedWasteFilters = value; } }
            public float Efficiency
            {
                get
                {
                    float pressurisedRoomVolume = PressurisedRoomVolume;
                    if (pressurisedRoomVolume == 0)
                    {
                        return 0;
                    }
                    return PoweredWasteFilters.Count * OxygenRoom.FilteredVolumePerFilter / pressurisedRoomVolume;
                }
            }
            public bool FullyPowered
            {
                get
                {
                    return ConnectedWasteFilters.All(filter => WasteFilterHasPower(filter));
                }
            }
            public float MaximumEfficiency
            {
                get
                {
                    float pressurisedRoomVolume = PressurisedRoomVolume;
                    if (pressurisedRoomVolume == 0)
                    {
                        return 0;
                    }
                    return ConnectedWasteFilters.Count * OxygenRoom.FilteredVolumePerFilter / pressurisedRoomVolume;
                }
            }
            /// <summary>
            /// The air blocks directly in front of the pipes. This is where gas is exchanged
            /// </summary>
            public List<Vector3i> PipeEndAirPositions
            {
                get
                {
                    return pipeEnds.Select(pipe => pipe.FirstPos).ToList();
                }
            }
            /// <summary>
            /// The locations of the end of the pipes. All the pipes have free space in front of them and are not joining up to a wall
            /// </summary>
            public List<Vector3i> PipeEndPositions
            {
                get
                {
                    return PipeEnds.Select(ray => ray.Pos).ToList();
                }
            }
            public List<Ray> PipeEnds
            {
                get
                {
                    return pipeEnds;
                }
                private set
                {
                    pipeEnds = value;
                }
            }
            public List<WasteFilterObject> PoweredWasteFilters
            {
                get
                {
                    return connectedWasteFilters.Where(filter => WasteFilterHasPower(filter)).ToList();
                }
            }
            /// <summary>
            /// The volume of all the (connected) rooms that this network supplies, which don't have holes in
            /// </summary>
            public int PressurisedRoomVolume
            { get { return VirtualRoomOnlyPressurised.Volume; } }
            public HashSet<Vector3i> RoomPositions
            { get { return rooms.Select(room => room.AverageEmptyPos).ToHashSet(); } }
            public int TotalRoomVolume
            { get { return VirtualRoom.Volume; } }
            /// <summary>
            /// A 'room' made up of all the rooms this network connects to. They don't have to be adjacent as the pipes may go to rooms far away from each other
            /// </summary>
            public ConnectedRoom VirtualRoom { get; private set; }
            public ConnectedRoom VirtualRoomOnlyPressurised
            { get { return ConnectedRoom.Union(VirtualRoom.Rooms.Select(room => new ConnectedRoom(room)).Distinct().Where(connectedRoom => connectedRoom.IsPressurised)); } }
            protected List<WasteFilterObject> connectedWasteFilters = new List<WasteFilterObject>();
            protected List<Ray> pipeEnds = new List<Ray>();
            protected HashSet<RoomStats> rooms = new HashSet<RoomStats>();
            public PipeNetwork(List<WasteFilterObject> connectedWasteFilters, List<Ray> pipeEnds)
            {
                this.connectedWasteFilters = connectedWasteFilters;
                this.pipeEnds = pipeEnds;
            }
            public void RecalculateRooms()
            {
                //All the individual rooms which have a pipe connection in this network
                Dictionary<Vector3i, RoomStats> pipeRoomsByPos = FindRoomStats(PipeEndAirPositions);

                //The adjoining rooms to those with a pipe need to be found, as they can shares gases with those with a pipe
                //Ignore the outside 'room'
                ConnectedRoom connectedRoom = new ConnectedRoom();
                foreach (KeyValuePair<Vector3i, RoomStats> pipeRoomByPos in pipeRoomsByPos)
                {
                    Vector3i pipePos = pipeRoomByPos.Key;
                    RoomStats room = pipeRoomByPos.Value;
                    if (!OxygenRoomManager.IsOutside(room))
                    {
                        connectedRoom.AddRoom(room);
                    }
                    else
                    {
                        //forget all the pipes which end outside
                        pipeEnds.RemoveAll(ray => ray.FirstPos == pipePos);
                    }
                }
                this.VirtualRoom = connectedRoom;
                //not just the piped rooms, but all those connected to them as well
                this.rooms = connectedRoom.Rooms.ToHashSet();
            }
        }
    }
}