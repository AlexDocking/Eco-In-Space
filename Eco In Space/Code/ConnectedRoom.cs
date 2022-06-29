namespace EcoInSpace
{
    using Eco.Core.Utils.Async;
    using Eco.Gameplay.GameActions;
    using Eco.Gameplay.Property;
    using Eco.Gameplay.Rooms;
    using Eco.Shared.Math;
    using Eco.Shared.Utils;
    using Eco.World;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static partial class OxygenRoomManager
    {
        public class ConnectedRoom
        {
            public float AirtightnessTier
            {
                get
                {
                    return WallCompositions.Select(wall => wall.AirtightnessTier * wall.Count).Sum() / WallCompositions.Select(wall => wall.Count).Sum();
                }
            }
            public Vector3i AverageEmptyPos
            {
                get
                {
                    Vector3 average = Vector3.Zero;
                    foreach (RoomStats room in Rooms)
                    {
                        average += (Vector3)room.AverageEmptyPos * (room.Volume / (float)this.Volume);
                    }
                    return average.Round;
                }
            }
            public float AverageTier
            {
                get
                {
                    return Rooms.Select(room => room.AverageTier * room.WallCount).Sum() / (float)Rooms.Select(room => room.WallCount).Sum();
                }
            }
            public bool IsPressurised { get; private set; }
            public HashSet<RoomStats> Rooms { get; private set; }
            public int Volume
            { get { return Rooms.Select(room => room.Volume).Sum(); } }
            public IEnumerable<WallTypeComposition> WallCompositions
            {
                get
                {
                    Dictionary<Type, WallTypeComposition> wallCompositionsByType = new Dictionary<Type, WallTypeComposition>();
                    foreach (RoomStats room in Rooms)
                    {
                        foreach (KeyValuePair<RoomStats.WallComposition, int> wall in room.WallCompositions)
                        {
                            if (wallCompositionsByType.ContainsKey(wall.Key.BlockItemType))
                            {
                                wallCompositionsByType[wall.Key.BlockItemType] = new WallTypeComposition(wall.Key, wallCompositionsByType[wall.Key.BlockItemType].Count + wall.Value);
                            }
                            else
                            {
                                wallCompositionsByType.Add(wall.Key.BlockItemType, new WallTypeComposition(wall.Key, wall.Value));
                            }
                        }
                    }
                    return wallCompositionsByType.Values;
                }
            }
            public HashSet<Vector3i> Windows { get; private set; }
            public ConnectedRoom()
            {
                this.Rooms = new HashSet<RoomStats>();
                this.Windows = new HashSet<Vector3i>();
                this.IsPressurised = true;
            }
            public ConnectedRoom(RoomStats startingRoom) : this()
            {
                AddRoom(startingRoom);
            }
            private ConnectedRoom(HashSet<RoomStats> rooms)
            {
                Rooms = rooms;
            }
            public static ConnectedRoom Union(IEnumerable<ConnectedRoom> connectedRooms)
            {
                HashSet<RoomStats> unionOfRooms = new HashSet<RoomStats>();
                foreach (ConnectedRoom connectedRoom in connectedRooms)
                {
                    foreach (RoomStats room in connectedRoom.Rooms)
                    {
                        if (unionOfRooms.None(knownRoom => SameRooms(room, knownRoom)))
                        {
                            unionOfRooms.Add(room);
                        }
                    }
                }
                return new ConnectedRoom(unionOfRooms);
            }
            //Add this room and any neighbouring rooms
            public void AddRoom(RoomStats newRoom)
            {
                if (ContainsRoom(newRoom))
                {
                    return;
                }
                if (IsOutside(newRoom))
                {
                    IsPressurised = false;
                    return;
                }
                Rooms.Add(newRoom);
                //key is window position, value is the position outside the window
                Dictionary<Vector3i, Vector3i> outsideWindowPositions = new Dictionary<Vector3i, Vector3i>();
                foreach (KeyValuePair<WrappedWorldPosition3i, Axis> window in newRoom.WindowAxis)
                {
                    Vector3i windowPosition = new Vector3i(window.Key.X, window.Key.Y, window.Key.Z);
                    Axis axis = window.Value;
                    Vector3i pos1 = windowPosition + axis.GetAxisDirections().Item1.ToVec();
                    Vector3i pos2 = windowPosition + axis.GetAxisDirections().Item2.ToVec();
                    pos1 = World.GetWrappedWorldPosition(pos1);
                    pos2 = World.GetWrappedWorldPosition(pos2);
                    Vector3i outsideWindowPosition = !newRoom.EmptySpace.Contains(pos1) ? pos1 : pos2;
                    outsideWindowPositions.Add(windowPosition, outsideWindowPosition);
                }
                //key is the position just outside the window, value is the room that coordinate is in
                Dictionary<Vector3i, Room> roomsAtPosition = FindRooms(outsideWindowPositions.Values);
                //the position just outside the window, of those windows which are on the outside of the structure and not between adjoining rooms
                List<Vector3i> outdoorsOutsideWindowPositions = roomsAtPosition.Keys.Where(pos => IsOutside(roomsAtPosition[pos])).ToList();
                List<Vector3i> outdoorsWallWindowPositions = outsideWindowPositions.Where(p => outdoorsOutsideWindowPositions.Contains(p.Value)).Select(p => p.Key).ToList();
                Windows.AddRange(outdoorsWallWindowPositions);

                List<RoomStats> adjoiningRooms = roomsAtPosition.Values.Select(room => room.RoomStats).ToList();
                foreach (RoomStats adjoiningRoom in adjoiningRooms)
                {
                    AddRoom(adjoiningRoom);
                }
            }
            public bool ContainsRoom(RoomStats room)
            {
                return Rooms.Any(currentlyConnectedRoom => currentlyConnectedRoom.AverageEmptyPos == room.AverageEmptyPos);
            }
            public override bool Equals(object obj)
            {
                ConnectedRoom other = obj as ConnectedRoom;
                if (other == null)
                {
                    return false;
                }
                if (Rooms.Count != other.Rooms.Count)
                {
                    return false;
                }
                if (!Rooms.All(room1 => other.Rooms.Any(room2 => SameRooms(room1, room2))))
                {
                    return false;
                }
                if (!other.Rooms.All(room1 => Rooms.Any(room2 => SameRooms(room1, room2))))
                {
                    return false;
                }
                return true;
            }
            public override int GetHashCode()
            {
                return Rooms.GetHashCode();
            }
        }
    }
}