namespace EcoInSpace
{
    using Eco.Gameplay.Plants;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Systems.Messaging.Chat.Commands;
    using Eco.Mods.TechTree;
    using Eco.Shared.Math;
    using Eco.Shared.Services;
    using Eco.Shared.Utils;
    using Eco.Simulation.Agents;
    using Eco.Simulation.WorldLayers;
    using Eco.Simulation.WorldLayers.Layers;
    using Eco.World;
    using Eco.World.Blocks;
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    public interface IWorldAccessor
    {
        void BatchApply(IEnumerable<BlockChange> blockChanges);
        Type GetBlockType(Vector3i worldPos);
        Dictionary<Vector3i, Type> GetLayer(Vector2i corner1, Vector2i corner2, int layer);
        Dictionary<Vector3i, Type> GetTopGroundBlocks(Vector2i corner1, Vector2i corner2);
        Dictionary<Vector3i, Type> GetTopGroundOrWaterBlocks(Vector2i corner1, Vector2i corner2);
        bool IsCaveColumn(Vector3i pos);
        bool IsCaveType(Type blockType);
        bool IsSolidRockType(Type blockType);
        bool IsWater(Vector3i pos);
        bool IsWater(Type blockType);
        bool IsWaterOrEmpty(Vector3i position);
        bool IsWaterOrEmpty(Type blockType);
        Dictionary<Vector3i, Type> LayerAbove(IEnumerable<Vector3i> layer);
        Dictionary<Vector3i, Type> LayerBelow(IEnumerable<Vector3i> layer);
        int MaxLandOrWaterUnwrapped(Vector2i xz);
        Vector3i MaxYPos(Vector2i xZ);
        int NonRockThickness(Vector3i vector3i);
        int RockThickness(Vector3i pos);
    }
    public class BlockChangeBuffer : ICollection<BlockChange>
    {
        public int Count => throw new NotImplementedException();
        public bool IsReadOnly => throw new NotImplementedException();
        private readonly Dictionary<Vector3i, Type> buffer = new Dictionary<Vector3i, Type>();
        public void Add(BlockChange blockChange)
        {
            if (buffer.ContainsKey(blockChange.Position))
            {
                buffer[blockChange.Position] = blockChange.BlockType;
            }
            else
            {
                buffer.Add(blockChange.Position, blockChange.BlockType);
            }
        }
        public void Add(IEnumerable<BlockChange> blockChanges)
        {
            foreach (BlockChange blockChange in blockChanges)
            {
                Add(blockChange);
            }
        }
        public void Clear()
        {
            buffer.Clear();
        }
        public bool Contains(Vector3i pos)
        {
            return buffer.ContainsKey(pos);
        }
        public bool Contains(BlockChange item)
        {
            return buffer.Contains(new KeyValuePair<Vector3i, Type>(item.Position, item.BlockType));
        }
        public void CopyTo(BlockChange[] array, int arrayIndex)
        {
            foreach (BlockChange blockChange in this)
            {
                array[arrayIndex++] = blockChange;
            }
        }
        public IEnumerator<BlockChange> GetEnumerator()
        {
            return buffer.Select(kv => ConvertEntry(kv)).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        public Type GetTypeAtPosition(Vector3i pos)
        {
            buffer.TryGetValue(pos, out Type type);
            return type;
        }
        public bool Remove(BlockChange item)
        {
            if (buffer.ContainsKey(item.Position) && buffer[item.Position] == item.BlockType)
            {
                return buffer.Remove(item.Position);
            }
            return false;
        }
        public bool TryGetValue(Vector3i pos, out Type type)
        {
            return buffer.TryGetValue(pos, out type);
        }
        private BlockChange ConvertEntry(KeyValuePair<Vector3i, Type> entry)
        {
            return new BlockChange()
            {
                Position = entry.Key,
                BlockType = entry.Value
            };
        }
    }
    public abstract class BlockSwitcher
    {
        public abstract Type GetNewBlockType(Type currentType, Vector3i position);
    }
    public class BoundaryWallBuilder
    {
        public Type WallType { get; set; } = typeof(AshlarShaleCubeBlock);

        /// <summary>
        /// Build up to this level
        /// </summary>
        private int SpaceHeight { get => World.VoxelSize.y; }

        public BoundaryWallBuilder()
        {
        }

        public BoundaryWallBuilder(Type wallType)
        {
            WallType = wallType;
        }
        public IEnumerable<BlockChange> BuildAllWalls(Vector2i corner1, Vector2i corner2)
        {
            List<BlockChange> blockChanges = new List<BlockChange>();
            int northLength = corner2.y - corner1.y + 1;
            int eastLength = corner2.x - corner1.x + 1;

            Vector2i bottomLeft = corner1;
            Vector2i bottomRight = bottomLeft + Vector2i.Right * eastLength;
            Vector2i topLeft = bottomLeft + Vector2i.Up * northLength;

            blockChanges.AddRange(BuildWallNorth(bottomLeft, northLength));
            blockChanges.AddRange(BuildWallNorth(bottomRight, northLength));
            blockChanges.AddRange(BuildWallEast(bottomLeft, eastLength));
            blockChanges.AddRange(BuildWallEast(topLeft, eastLength));
            return blockChanges;
        }
        private IEnumerable<BlockChange> BuildWallEast(Vector2i westPoint, int length)
        {
            List<BlockChange> blockChanges = new List<BlockChange>();
            for (int i = 0; i < length; i++)
            {
                Vector2i xy = World.GetWrappedWorldPosition(westPoint + Vector2i.Right * i);
                for (int h = 1; h < SpaceHeight; h++)
                {
                    Vector3i wallPoint = new Vector3i(xy.x, h, xy.y);
                    BlockChange blockChange = new BlockChange();
                    blockChange.Position = wallPoint;
                    blockChange.BlockType = WallType;
                    blockChanges.Add(blockChange);
                }
            }
            return blockChanges;
        }
        private IEnumerable<BlockChange> BuildWallNorth(Vector2i southPoint, int length)
        {
            List<BlockChange> blockChanges = new List<BlockChange>();
            for (int i = 0; i < length; i++)
            {
                Vector2i xy = World.GetWrappedWorldPosition(southPoint + Vector2i.Up * i);
                for (int h = 1; h < SpaceHeight; h++)
                {
                    Vector3i wallPoint = new Vector3i(xy.x, h, xy.y);
                    BlockChange blockChange = new BlockChange();
                    blockChange.Position = wallPoint;
                    blockChange.BlockType = WallType;
                    blockChanges.Add(blockChange);
                }
            }
            return blockChanges;
        }
    }
    public class CachedWorld : IWorldAccessor
    {
        private readonly BlockChangeBuffer buffer = new BlockChangeBuffer();

        /// <summary>
        /// Keep a record of the highest block we change for each xz position
        /// </summary>
        private readonly Dictionary<Vector2i, int> maxChangedHeight = new Dictionary<Vector2i, int>();

        public void BatchApply(IEnumerable<BlockChange> blockChanges)
        {
            buffer.Add(blockChanges);
        }

        /// <summary>
        /// Apply the edits to the world and clear the buffer
        /// </summary>
        public void ClearCache()
        {
            try
            {
                World.BatchApply(buffer);
            }
            catch
            {
                Log.WriteErrorLineLocStr("PerformEdits failed to perform " + buffer.Count + " edits");

                throw;
            }
            finally
            {
                maxChangedHeight.Clear();
                buffer.Clear();
            }
        }
        public Type GetBlockType(Vector3i worldPos)
        {
            if (buffer.Contains(worldPos))
            {
                return buffer.GetTypeAtPosition(worldPos);
            }
            return World.GetBlock(worldPos).GetType();
        }

        public Dictionary<Vector3i, Type> GetLayer(Vector2i corner1, Vector2i corner2, int layer)
        {
            Dictionary<Vector3i, Type> layerBlocks = new Dictionary<Vector3i, Type>();
            for (int x = corner1.x; x <= corner2.x; x++)
            {
                for (int y = corner1.y; y <= corner2.y; y++)
                {
                    Vector3i wrappedPos = World.GetWrappedWorldPosition(new Vector3i(x, layer, y));
                    layerBlocks.Add(wrappedPos, GetBlockType(wrappedPos));
                }
            }
            return layerBlocks;
        }
        public Dictionary<Vector3i, Type> GetTopGroundBlocks(Vector2i corner1, Vector2i corner2)
        {
            Dictionary<Vector3i, Type> topBlocks = new Dictionary<Vector3i, Type>();
            for (int x = corner1.x; x <= corner2.x; x++)
            {
                for (int y = corner1.y; y <= corner2.y; y++)
                {
                    Vector2i wrappedPos = World.GetWrappedWorldPosition(new Vector2i(x, y));
                    Vector3i topPosition = MaxYPos(wrappedPos);
                    topBlocks.Add(topPosition, GetBlockType(topPosition));
                }
            }
            return topBlocks;
        }
        public Dictionary<Vector3i, Type> GetTopGroundOrWaterBlocks(Vector2i corner1, Vector2i corner2)
        {
            Dictionary<Vector3i, Type> topBlocks = new Dictionary<Vector3i, Type>();
            for (int x = corner1.x; x <= corner2.x; x++)
            {
                for (int y = corner1.y; y <= corner2.y; y++)
                {
                    Vector2i wrappedPos = World.GetWrappedWorldPosition(new Vector2i(x, y));
                    Log.WriteErrorLineLocStr("World landwater : " + World.MaxLandOrWaterUnwrapped(wrappedPos) + ", mine: " + MaxLandOrWaterUnwrapped(wrappedPos));
                    Log.WriteErrorLineLocStr("World max y : " + World.MaxYPos(wrappedPos).y + ", mine: " + MaxYPos(wrappedPos).y);

                    Vector3i topPosition = new Vector3i(wrappedPos.x, MaxLandOrWaterUnwrapped(wrappedPos), wrappedPos.y);
                    topBlocks.Add(topPosition, GetBlockType(topPosition));
                }
            }
            return topBlocks;
        }
        /// <summary>
        /// Returns if this position is somewhere an unsupported column can collapse into i.e. is air or water
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public bool IsCaveColumn(Vector3i pos)
        {
            try
            {
                Type blockType = GetBlockType(pos);
                return IsCaveType(blockType);
            }
            catch
            {
                Log.WriteErrorLineLocStr("IsCave failed " + pos);
                throw;
            }
        }
        public bool IsCaveType(Type blockType)
        {
            return blockType == typeof(EmptyBlock) || blockType == typeof(WaterBlock) || blockType == typeof(EncasedWaterBlock);
        }
        public bool IsSolidRock(Vector3i pos)
        {
            Type blockType = GetBlockType(pos);
            return IsSolidRockType(blockType);
        }
        public bool IsSolidRockType(Type blockType)
        {
            Type[] rockTypes = new Type[]
            {
                typeof(BasaltBlock),
                typeof(ShaleBlock),
                typeof(SandstoneBlock),
                typeof(GraniteBlock),
                typeof(LimestoneBlock),
                typeof(GneissBlock),
                typeof(IronOreBlock),
                typeof(CopperOreBlock),
                typeof(GoldOreBlock),
                typeof(CoalBlock)
            };
            return rockTypes.Contains(blockType);
        }
        public bool IsWater(Vector3i pos)
        {
            Type blockType = GetBlockType(pos);
            return IsWater(blockType);
        }
        public bool IsWater(Type blockType)
        {
            return (blockType == typeof(WaterBlock)) || (blockType == typeof(EncasedWaterBlock));
        }
        public bool IsWaterOrEmpty(Vector3i position)
        {
            Type blockType = GetBlockType(position);
            return IsWaterOrEmpty(blockType);
        }
        public bool IsWaterOrEmpty(Type blockType)
        {
            return IsWater(blockType) || IsEmpty(blockType);
        }
        public Dictionary<Vector3i, Type> LayerAbove(IEnumerable<Vector3i> layer)
        {
            Dictionary<Vector3i, Type> nextLayer = new Dictionary<Vector3i, Type>();
            foreach (Vector3i pos in layer)
            {
                //MaxLandOrWaterUnwrapped doesn't work properly with water, so just keep going until roughly space level
                if (pos.y + 1 <= 200)//MaxLandOrWaterUnwrapped(pos.XZ))
                {
                    Vector3i above = new Vector3i(pos.x, pos.y + 1, pos.z);
                    nextLayer.Add(above, GetBlockType(above));
                }
            }
            return nextLayer;
        }
        public Dictionary<Vector3i, Type> LayerBelow(IEnumerable<Vector3i> layer)
        {
            Dictionary<Vector3i, Type> nextLayer = new Dictionary<Vector3i, Type>();
            foreach (Vector3i pos in layer)
            {
                if (pos.y - 1 >= 0)
                {
                    Vector3i beneath = new Vector3i(pos.x, pos.y - 1, pos.z);
                    nextLayer.Add(beneath, GetBlockType(beneath));
                }
            }
            return nextLayer;
        }
        public int MaxLandOrWaterUnwrapped(Vector2i xz)
        {
            xz = World.GetWrappedWorldPosition(xz);
            int existingMaxY = World.MaxLandOrWaterUnwrapped(xz);
            if (maxChangedHeight.ContainsKey(xz))
            {
                //if we have placed any blocks at or above the existing max, the new max y may be different
                if (existingMaxY <= maxChangedHeight[xz])
                {
                    //we must check the blocks above in case any of them are non-empty, in which case this is the new max y
                    for (int h = maxChangedHeight[xz]; h >= 1; h--)
                    {
                        Vector3i pos = new Vector3i(xz.x, h, xz.y);
                        if (!IsEmpty(GetBlockType(pos)))
                        {
                            return h;
                        }
                    }
                    return 0;
                }
            }
            return existingMaxY;
        }
        public Vector3i MaxYPos(Vector2i xz)
        {
            Vector3i existingMaxPos = World.MaxYPos(xz);
            if (maxChangedHeight.ContainsKey(xz))
            {
                //if we have placed any blocks at or above the existing max, the new max y may be different
                if (existingMaxPos.y <= maxChangedHeight[xz])
                {
                    //we must check the blocks above in case any of them are non-empty, in which case this is the new max y
                    for (int h = maxChangedHeight[xz]; h >= 1; h--)
                    {
                        Vector3i pos = new Vector3i(xz.x, h, xz.y);
                        if (!IsWaterOrEmpty(GetBlockType(pos)))
                        {
                            return pos;
                        }
                    }
                    return new Vector3i(xz.x, 0, xz.y);
                }
            }
            return existingMaxPos;
        }
        public int NonRockThickness(Vector3i basePos)
        {
            try
            {
                for (int y = 0; y <= MaxYPos(basePos.XZ).y; y++)
                {
                    if (IsSolidRock(basePos + Vector3i.Up * y))
                    {
                        return y;
                    }
                }
            }
            catch
            {
                Log.WriteErrorLineLocStr("NonRockThickness failed. basePos= " + basePos);
                throw;
            }
            return MaxYPos(basePos.XZ).y - basePos.y + 1;
        }
        public void QueueChanges(IEnumerable<BlockChange> blockChanges)
        {
            buffer.Add(blockChanges);
            foreach (BlockChange blockChange in blockChanges)
            {
                UpdateMaxChangedPositionCache(blockChange.Position);
            }
        }
        public int RockThickness(Vector3i basePos)
        {
            try
            {
                for (int y = 0; y <= MaxYPos(basePos.XZ).y; y++)
                {
                    Vector3i pos = basePos + Vector3i.Up * y;
                    if (!IsSolidRock(pos))
                    {
                        return y;
                    }
                }
            }
            catch
            {
                Log.WriteErrorLineLocStr("RockThickness failed. basePos=" + basePos);
                throw;
            }
            return MaxYPos(basePos.XZ).y - basePos.y + 1;
        }
        private bool IsEmpty(Type blockType)
        {
            return blockType == typeof(EmptyBlock);
        }
        private void UpdateMaxChangedPositionCache(Vector3i position)
        {
            if (maxChangedHeight.ContainsKey(position.XZ))
            {
                if (maxChangedHeight[position.XZ] < position.y)
                {
                    maxChangedHeight[position.XZ] = position.y;
                }
            }
            else
            {
                maxChangedHeight.Add(position.XZ, position.y);
            }
        }
    }
    public class CollapsedBlockSwitcher : BlockSwitcher
    {
        public override Type GetNewBlockType(Type currentType, Vector3i position)
        {
            Dictionary<Type, Type> blockSwitches = new Dictionary<Type, Type>()
            {
                { typeof(SandstoneBlock), typeof(CrushedSandstoneBlock) },
                { typeof(BasaltBlock), typeof(CrushedBasaltBlock) },
                { typeof(LimestoneBlock), typeof(CrushedLimestoneBlock) },
                { typeof(GneissBlock), typeof(CrushedGneissBlock) },
                { typeof(GraniteBlock), typeof(CrushedGraniteBlock) },
                { typeof(ShaleBlock), typeof(CrushedShaleBlock) },
                { typeof(IronOreBlock), typeof(CrushedIronOreBlock) },
                { typeof(GoldOreBlock), typeof(CrushedGoldOreBlock) },
                { typeof(CopperOreBlock), typeof(CrushedCopperOreBlock) },
            };
            if (blockSwitches.ContainsKey(currentType))
            {
                return blockSwitches[currentType];
            }
            return currentType;
        }
    }
    public class MarsBlockSwitcher : BlockSwitcher
    {
        public override Type GetNewBlockType(Type currentType, Vector3i position)
        {
            Dictionary<Type, Type> blockSwitches = new Dictionary<Type, Type>()
            {
                { typeof(DirtBlock), typeof(CrushedSandstoneBlock) },
                { typeof(GrassBlock), typeof(EmptyBlock) },
                { typeof(ColdForestSoilBlock), typeof(SandstoneBlock) },
                { typeof(ForestSoilBlock), typeof(CrushedIronOreBlock) },
                { typeof(FrozenSoilBlock), typeof(SandstoneBlock) },
                { typeof(RainforestSoilBlock), typeof(CrushedSandstoneBlock) },
                { typeof(RockySoilBlock), typeof(SandstoneBlock) },
                { typeof(TaigaSoilBlock), typeof(CrushedIronOreBlock) },
                { typeof(TundraSoilBlock), typeof(EmptyBlock) },
                { typeof(WarmForestSoilBlock), typeof(CrushedIronOreBlock) },
                { typeof(WetlandsSoilBlock), typeof(EmptyBlock) },
                { typeof(DesertSandBlock), typeof(EmptyBlock) },
                { typeof(OceanSandBlock), typeof(CrushedSandstoneBlock) },
                { typeof(RiverbedBlock), typeof(EmptyBlock) },
                { typeof(RiverSandBlock), typeof(EmptyBlock) },
                { typeof(ClayBlock), typeof(EmptyBlock) },
                { typeof(SandBlock), typeof(CrushedSandstoneBlock) },
                { typeof(WaterBlock), typeof(EmptyBlock) },
                { typeof(BasaltBlock), typeof(SandstoneBlock) },
                { typeof(CrushedBasaltBlock), typeof(CrushedSandstoneBlock) },
                { typeof(LimestoneBlock), typeof(IronOreBlock) },
                { typeof(CrushedLimestoneBlock), typeof(CrushedIronOreBlock) },
                { typeof(GneissBlock), typeof(WaterBlock) },
                { typeof(CrushedGneissBlock), typeof(WaterBlock) },
                { typeof(IronOreBlock), typeof(SandstoneBlock) },
                { typeof(CrushedIronOreBlock), typeof(CrushedCopperOreBlock) },
                { typeof(ShaleBlock), typeof(SandstoneBlock) },
                { typeof(CrushedShaleBlock), typeof(GoldConcentrateBlock) },
                { typeof(CopperOreBlock), typeof(CrushedGraniteBlock) },
                { typeof(GoldOreBlock), typeof(SandstoneBlock) },
                { typeof(CrushedGoldOreBlock), typeof(CrushedSandstoneBlock) },
                { typeof(GraniteBlock), typeof(SandstoneBlock) },
                { typeof(CrushedGraniteBlock), typeof(CrushedIronOreBlock) },
                { typeof(IceBlock), typeof(CrushedSandstoneBlock) },
                { typeof(CoalBlock), typeof(EmptyBlock) },
                { typeof(LogBlock), typeof(EmptyBlock) },
                { typeof(TreeBlock), typeof(EmptyBlock) },
                { typeof(PlantBlock), typeof(EmptyBlock) }
            };
            if (currentType == typeof(CoalBlock))
            {
                if (new System.Random().Chance(0.2f))
                {
                    return typeof(CrushedCopperOreBlock);
                }
            }
            else if (currentType == typeof(CopperOreBlock))
            {
                if (new System.Random().Chance(0.25f))
                {
                    return typeof(CrushedCopperOreBlock);
                }
                if (new System.Random().Chance(0.1f))
                {
                    return typeof(CrushedGoldOreBlock);
                }
                if (new System.Random().Chance(0.1f))
                {
                    return typeof(CopperConcentrateBlock);
                }
                if (new System.Random().Chance(0.1f))
                {
                    return typeof(GoldConcentrateBlock);
                }
            }
            else if (currentType == typeof(GoldOreBlock))
            {
                if (new System.Random().Chance(0.25f))
                {
                    return typeof(IronOreBlock);
                }
                else if (new System.Random().Chance(0.15f))
                {
                    return typeof(CrushedIronOreBlock);
                }
            }
            else if (currentType == typeof(GneissBlock))
            {
                //Change all gneiss closer than 30m from the surface into sandstone. We don't like the holes to the surface
                if (position.y > World.MaxLandOrWaterUnwrapped(position.XZ) - 30)
                {
                    return typeof(SandstoneBlock);
                }
            }
            if (blockSwitches.ContainsKey(currentType))
            {
                return blockSwitches[currentType];
            }
            return currentType;
        }
        public IEnumerable<BlockChange> SwitchBlockTypes(IEnumerable<KeyValuePair<Vector3i, Type>> blocks)
        {
            List<BlockChange> blockChanges = new List<BlockChange>();
            foreach (KeyValuePair<Vector3i, Type> pair in blocks)
            {
                Vector3i position = pair.Key;
                Type blockType = pair.Value;
                Type newBlockType = GetNewBlockType(blockType, position);
                BlockChange blockChange = new BlockChange();
                blockChange.Position = position;
                blockChange.BlockType = newBlockType;
                blockChanges.Add(blockChange);
            }
            return blockChanges;
        }
    }
    public class MarsTerraformingCommands : IChatCommandHandler
    {
        [ChatCommand("Build Ashlar Shale walls on the borders of the given area, from bedrock up to space", ChatAuthorizationLevel.Admin)]
        public static async void BuildSpaceWalls(User user, int x, int y, int width, int height)
        {
            Terraformer terraformer = new Terraformer(x, y, width, height);

            user.MsgLocStr("Start building walls", NotificationStyle.Chat);
            await Task.Run(() =>
            {
                terraformer.BuildWalls();
                terraformer.Finalise();
            });
            user.MsgLocStr("Finished building walls", NotificationStyle.Chat);
        }
        /// <summary>
        /// Clamp corner 2 so that it doesn't wrap all the way round the world and past corner 1
        /// </summary>
        /// <param name="corner1"></param>
        /// <param name="corner2"></param>
        /// <returns></returns>
        /// <summary>
        /// Collapse overhangs where the solid rock thickness is fewer than three blocks thick
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Collapse overhanging rock fewer than 3 blocks thick", ChatAuthorizationLevel.Admin)]
        public static async void CollapseOverhangs(User user, int x, int y, int width, int height)
        {
            Terraformer terraformer = new Terraformer(x, y, width, height);

            user.MsgLocStr("Starting collapsing", NotificationStyle.Chat);
            await Task.Run(() =>
            {
                terraformer.CollapseOverhangs();
                terraformer.Finalise();
            });
            user.MsgLocStr("Finished collapsing", NotificationStyle.Chat);
        }
        [ChatCommand("Where there is exposed bedrock, cover it over with a layer crushed sandstone", ChatAuthorizationLevel.Admin)]
        public static async void CoverOverBedrock(User user, int x, int y, int width, int height)
        {
            Terraformer terraformer = new Terraformer(x, y, width, height);

            user.MsgLocStr("Starting covering", NotificationStyle.Chat);
            await Task.Run(() =>
            {
                terraformer.CoverOverBedrock();
                terraformer.Finalise();
            });
            user.MsgLocStr("Finished covering", NotificationStyle.Chat);
        }
        /// <summary>
        /// Evaporate water above this depth
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Evaporate water above a given altitude", ChatAuthorizationLevel.Admin)]
        public static void EvaporateAbove(User user, int x, int y, int width, int height, int depth)
        {
            Terraformer terraformer = new Terraformer(x, y, width, height);

            user.MsgLocStr("Starting evaporating", NotificationStyle.Chat);
            Task.Run(() =>
            {
                terraformer.EvaporateAbove(depth);
                terraformer.Finalise();
            });
            user.MsgLocStr("Finished evaporating", NotificationStyle.Chat);
        }

        /// <summary>
        /// Collapse overhangs where the solid rock thickness is fewer than three blocks thick
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Kill all plants in a given area", ChatAuthorizationLevel.Admin)]
        public static async void KillPlants(User user, int x, int y, int width, int height)
        {
            Terraformer terraformer = new Terraformer(x, y, width, height);

            user.MsgLocStr("Start killing plants", NotificationStyle.Chat);
            await Task.Run(() =>
            {
                terraformer.KillAllPlants();
                terraformer.Finalise();
            }); user.MsgLocStr("Finished killing plants", NotificationStyle.Chat);
        }
        /// <summary>
        /// Chat command for server administrators for repeating a message back to the sender
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Transform the area into a Martian landscape", ChatAuthorizationLevel.Admin)]
        public static async void MakeMars(User user, int x, int y, int width, int height)
        {
            Terraformer terraformer = new Terraformer(x, y, width, height);

            user.MsgLocStr("Start terraforming", NotificationStyle.Chat);
            await Task.Run(() =>
            {
                terraformer.WipeLayers();
                terraformer.KillAllPlants();
                terraformer.ReplaceRockWithMartianRock();
                terraformer.CollapseOverhangs();
                terraformer.CoverOverBedrock();
                terraformer.EvaporateAbove(15);

                //terraformer.BuildWalls();
                terraformer.Finalise();
            });
            user.MsgLocStr("Finished terraforming", NotificationStyle.Chat);
            Settings.AddSaveSpace(new Bounds2D(terraformer.Corner1, terraformer.Corner2 - terraformer.Corner1 + Vector2i.One));
        }

        [ChatCommand("Specify an area where oxygen is required", ChatAuthorizationLevel.Admin)]
        public static void MakeSpace(User user, int x, int y, int width, int height)
        {
            Vector2i corner1 = new Vector2i(x, y);
            Vector2i corner2 = new Vector2i(x + width - 1, y + height - 1);
            Settings.AddSaveSpace(new Bounds2D(corner1, corner2 - corner1 + Vector2i.One));
        }
    }
    public class Terraformer
    {
        public Vector2i Corner1 { get; set; }
        public Vector2i Corner2 { get => corner2; set => corner2 = ClampCorner2(Corner1, value); }
        private IWorldAccessor WorldAccessor { get => cachedWorld; }
        private CachedWorld cachedWorld = new CachedWorld();
        private Vector2i corner2;
        public Terraformer(Vector2i corner1, Vector2i corner2)
        {
            this.Corner1 = corner1;
            this.Corner2 = corner2;
        }
        public Terraformer(int x, int y, int width, int height)
        {
            this.Corner1 = new Vector2i(x, y);
            this.Corner2 = new Vector2i(x + width - 1, y + height - 1);
        }
        public void BuildWalls()
        {
            AddToBuffer((new BoundaryWallBuilder(typeof(AshlarShaleCubeBlock))).BuildAllWalls(Corner1, Corner2));
        }
        public void CollapseOverhangs()
        {
            for (int x = Corner1.x; x <= Corner2.x; x++)
            {
                List<Vector3i> rowPositions = new List<Vector3i>();
                for (int z = Corner1.y; z <= corner2.y; z++)
                {
                    try
                    {
                        rowPositions.Add(World.GetWrappedWorldPosition(new Vector3i(x, 1, z)));
                    }
                    catch
                    {
                        Log.WriteErrorLineLocStr("CollapseAllOverhangs failed at x=" + x + " z=" + z);
                        throw;
                    }
                }
                IEnumerable<Vector3i> changedStackCoords = rowPositions;
                do
                {
                    IEnumerable<BlockChange> blockChanges;
                    try
                    {
                        blockChanges = CollapseOverhangsChanges(changedStackCoords, out changedStackCoords);
                    }
                    catch
                    {
                        Log.WriteErrorLineLocStr("CollapseOverhangsChanges failed\n" + string.Join("\n", changedStackCoords.Select(c => c.ToString())));
                        throw;
                    }
                    if (blockChanges.Count() > 0)
                    {
                        AddToBuffer(blockChanges);
                    }
                }
                while (changedStackCoords.Count() > 0);
            }
        }
        public void CoverOverBedrock()
        {
            List<BlockChange> blockChanges = new List<BlockChange>();
            Dictionary<Vector3i, Type> layer = WorldAccessor.GetLayer(Corner1, Corner2, 1);
            foreach (KeyValuePair<Vector3i, Type> block in layer)
            {
                Vector3i position = block.Key;
                Type blockType = block.Value;
                if (WorldAccessor.IsWaterOrEmpty(blockType))
                {
                    BlockChange blockChange = new BlockChange();
                    blockChange.Position = position;
                    blockChange.BlockType = typeof(CrushedSandstoneBlock);
                    blockChanges.Add(blockChange);
                }
            }
            AddToBuffer(blockChanges);
        }
        public void EvaporateAbove(int depth)
        {
            //Sometimes it starts evaporating a block below where it should
            List<Vector3i> layer = WorldAccessor.GetLayer(Corner1, Corner2, depth + 1).Keys.ToList();
            List<BlockChange> blockChanges = new List<BlockChange>();
            while (layer.Count() > 0)
            {
                foreach (Vector3i pos in layer)
                {
                    if (WorldAccessor.IsWater(pos))
                    {
                        BlockChange blockChange = new BlockChange();
                        blockChange.Position = pos;
                        blockChange.BlockType = typeof(EmptyBlock);
                        blockChanges.Add(blockChange);
                    }
                }

                layer = WorldAccessor.LayerAbove(layer).Keys.ToList();
            }
            AddToBuffer(blockChanges);
        }
        public void Finalise()
        {
            cachedWorld.ClearCache();
        }
        /// <summary>
        /// kill all the surface plants
        /// </summary>
        /// <remarks>
        /// i clear the buffer before and after because i can't get the plant block with the worldaccessor.
        /// todo: check which type of block check finds and destroys plants, as it may not be necessary to get the source block
        /// </remarks>
        public void KillAllPlants()
        {
            Finalise();
            Dictionary<Vector3i, Type> topBlocks = WorldAccessor.GetTopGroundBlocks(Corner1, Corner2);
            List<BlockChange> blockChanges = new List<BlockChange>();
            foreach (Vector3i groundPos in topBlocks.Keys)
            {
                Vector3i plantPos = groundPos;
                Plant plant = PlantBlock.GetPlant(plantPos);
                PlantBlock plantBlock = World.GetBlock(plantPos) as PlantBlock;
                if (plant != null)
                {
                    plant.Destroy();
                    BlockChange blockChange = new BlockChange();
                    blockChange.Position = plantPos;
                    blockChange.BlockType = typeof(EmptyBlock);
                    blockChanges.Add(blockChange);
                }
                if (plantBlock != null)
                {
                    plantBlock.Destroyed(plantPos, Block.Empty);
                }
                if (plant != null || plantBlock != null)
                {
                    BlockChange blockChange = new BlockChange();
                    blockChange.Position = plantPos;
                    blockChange.BlockType = typeof(EmptyBlock);
                    blockChanges.Add(blockChange);
                }
                plantPos = groundPos + Vector3i.Up;
                plant = PlantBlock.GetPlant(plantPos);
                plantBlock = World.GetBlock(plantPos) as PlantBlock;
                if (plant != null)
                {
                    plant.Destroy();
                }
                if (plantBlock != null)
                {
                    plantBlock.Destroyed(plantPos, Block.Empty);
                }
                if (plant != null || plantBlock != null)
                {
                    BlockChange blockChange = new BlockChange();
                    blockChange.Position = plantPos;
                    blockChange.BlockType = typeof(EmptyBlock);
                    blockChanges.Add(blockChange);
                }
            }
            AddToBuffer(blockChanges);
            Finalise();
        }
        public void ReplaceRockWithMartianRock()
        {
            //As getting the top blocks doesn't work, instead we can do every block above bedrock up to a very high altitude as this should cover all natural terrain just the same
            Dictionary<Vector3i, Type> baseBlocks = WorldAccessor.GetLayer(Corner1, Corner2, 1);// GetTopGroundOrWaterBlocks(Corner1, Corner2);

            Dictionary<Vector3i, Type> inQueue = baseBlocks;
            List<BlockChange> blockChanges = new List<BlockChange>();
            while (inQueue.Count() > 0)
            {
                blockChanges.AddRange(new MarsBlockSwitcher().SwitchBlockTypes(inQueue));
                inQueue = WorldAccessor.LayerAbove(inQueue.Keys);
            }
            AddToBuffer(blockChanges);
        }
        public void WipeLayers(IEnumerable<string> layerNames, float newValue = 0f)
        {
            List<WorldLayer> validLayers = new List<WorldLayer>();
            foreach (string layerName in layerNames)
            {
                WorldLayer layer = WorldLayerManager.Obj.GetLayer(layerName);
                if (layer != null)
                {
                    for (int x = Corner1.x; x <= Corner2.x; x++)
                    {
                        for (int z = Corner1.y; z <= Corner2.y; z++)
                        {
                            Vector2i pos = World.GetWrappedWorldPosition(new Vector2i(x, z));

                            layer.SetAtWorldPos(pos, newValue);
                        }
                    }
                    validLayers.Add(layer);
                }
            }
            foreach (string layerName in layerNames)
            {
                try
                {
                    WorldLayer layer = WorldLayerManager.Obj.GetLayer(layerName);
                    if (layer != null)
                    {
                        layer.DoTick();
                    }
                }
                catch
                {
                    Log.WriteErrorLineLocStr("Something went wrong wiping " + layerName + " layer");
                }
            }
        }
        /// <summary>
        /// Wipe all layers that make Earth different than a cold lifeless rock
        /// </summary>
        public void WipeLayers()
        {
            List<string> layerNames = new List<string>()
            {
                "Oilfield",
                "Temperature",
                "SaltWater",
                "FreshWater",
                "SoilMoisture",
                "Rainfall",
                "Invertebrates",
                "Nitrogen",
                "Phosphorus",
                "Potassium",
                "SaltWaterSpread"
            };
            //These ones don't work
            /*
                "Agouti",
                "Alligator",
                "Bass",
                "BighornSheep",
                "Bison",
                "BlueShark",
                "Cod",
                "Coyote",
                "Crab",
                "Deer",
                "Elk",
                "Fox",
                "Hare",
                "Jaguar",
                "MoonJellyfish",
                "MountainGoat",
                "Otter",
                "PacificSardine",
                "PrairieDog",
                "Salmon",
                "SnappingTurtle",
                "Tarantula",
                "Tortoise",
                "Trout",
                "Tuna",
                "Turkey",
                "Wolf"
            */
            layerNames.AddRange(WorldLayerManager.Obj.Layers.Where(layer => layer.Name.EndsWith("Potential") || layer.Name.EndsWith("Biome") || layer.Name.EndsWith("Capacity") || layer.Name.EndsWith("Growth")).Select(layer => layer.Name));
            WipeLayers(layerNames);
        }
        protected Vector2i ClampCorner2(Vector2i corner1, Vector2i corner2)
        {
            //e.g.
            //corner 1 = (2,2), corner2 = (9,9), world size=(4,4)
            //corner2 = (5,5)
            Vector2i clampedToWorldSize = new Vector2i(corner1.x + Math.Min(World.WrappedVoxelSize.x - 1, corner2.x - corner1.x), corner1.y + Math.Min(World.WrappedVoxelSize.z - 1, corner2.y - corner1.y));
            return clampedToWorldSize;
        }
        /// <summary>
        /// Move the overhang down to the ground below
        /// </summary>
        /// <param name="blockSwitcher"></param>
        /// <param name="lowestPos"></param>
        /// <param name="thickness"></param>
        /// <param name="dropHeight"></param>
        /// <returns></returns>
        protected IEnumerable<BlockChange> CollapseColumn(BlockSwitcher blockSwitcher, Vector3i lowestPos, int thickness, int dropHeight)
        {
            List<BlockChange> blockChanges = new List<BlockChange>();
            try
            {
                //move blocks down
                for (int i = 0; i < thickness; i++)
                {
                    Vector3i oldPos = lowestPos + Vector3i.Up * i;
                    Vector3i newPos = oldPos - Vector3i.Up * dropHeight;
                    Type blockType = WorldAccessor.GetBlockType(oldPos);
                    Type newType = blockSwitcher.GetNewBlockType(blockType, oldPos);
                    BlockChange blockChange = new BlockChange();
                    blockChange.Position = newPos;
                    blockChange.BlockType = newType;
                    blockChanges.Add(blockChange);
                }
                //replace with air
                for (int i = 0; i < dropHeight; i++)
                {
                    Vector3i pos = lowestPos + Vector3i.Up * (-dropHeight + thickness + i);
                    BlockChange blockChange = new BlockChange();
                    blockChange.Position = pos;
                    blockChange.BlockType = typeof(EmptyBlock);
                    blockChanges.Add(blockChange);
                }
            }
            catch
            {
                Log.WriteErrorLineLocStr("CollapseColumn failed at " + lowestPos + " thickness=" + thickness + " drop height=" + dropHeight);
                throw;
            }
            return blockChanges;
        }
        /// <summary>
        /// Given a list of starting positions, search upwards in each column for overhangs to collapse
        /// </summary>
        /// <param name="basePositions">Starting positions</param>
        /// <param name="changedStacks">The columns which collapsed. Specifically the positions from which search should continue</param>
        /// <returns>All the blocks which changed</returns>
        protected IEnumerable<BlockChange> CollapseOverhangsChanges(IEnumerable<Vector3i> basePositions, out IEnumerable<Vector3i> changedStacks)
        {
            List<BlockChange> blockChanges = new List<BlockChange>();
            Dictionary<Vector2i, Vector3i> changedStacksCoords = new Dictionary<Vector2i, Vector3i>();
            BlockSwitcher blockSwitcher = new CollapsedBlockSwitcher();
            foreach (Vector3i basePosition in basePositions)
            {
                int maxY = WorldAccessor.MaxYPos(basePosition.XZ).y;
                int caveFloorY = 1;
                bool inCave = false;
                for (int y = basePosition.y; y <= maxY; y++)
                {
                    try
                    {
                        Vector3i pos = basePosition.XZ.X_Z(y);
                        if (WorldAccessor.IsCaveColumn(pos))
                        {
                            if (!inCave)
                            {
                                caveFloorY = y;
                                inCave = true;
                            }
                        }
                        else
                        {
                            inCave = false;
                            //if cave below
                            if (WorldAccessor.IsCaveColumn(pos - Vector3i.Up))
                            {
                                //with pos as the base, how many blocks of contiguous solid rock are there (inclusive)
                                int rockThickness = WorldAccessor.RockThickness(pos);
                                //collapse only where the solid rock is one or no blocks thick
                                if (rockThickness <= 1)
                                {
                                    //crushed rock, and any other solid blocks which aren't raw rock
                                    int nonRockThickness = WorldAccessor.NonRockThickness(pos + Vector3i.Up * rockThickness);
                                    int collapsedThickness = rockThickness + nonRockThickness;
                                    int depthDropped = y - caveFloorY;
                                    //collapse the column and replace where it came from with air
                                    blockChanges.AddRange(CollapseColumn(blockSwitcher, pos, collapsedThickness, depthDropped));
                                    if (!changedStacksCoords.ContainsKey(pos.XZ))
                                    {
                                        changedStacksCoords.Add(pos.XZ, pos + Vector3i.Down * (depthDropped - collapsedThickness));
                                    }
                                    y += collapsedThickness - 1;
                                }
                            }
                        }
                    }
                    catch
                    {
                        Log.WriteErrorLineLocStr("Something went wrong at " + basePosition + ", max y = " + maxY + ", y=" + y);
                        throw;
                    }
                }
            }
            changedStacks = changedStacksCoords.Values;
            return blockChanges;
        }
        /// <summary>
        /// Queue up these edits to be applied to the actual eco world later
        /// </summary>
        /// <param name="blockChanges"></param>
        private void AddToBuffer(IEnumerable<BlockChange> blockChanges)
        {
            cachedWorld.QueueChanges(blockChanges);
        }
    }
}