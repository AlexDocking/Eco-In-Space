namespace Eco
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Eco.Core.Plugins.Interfaces;
    using Eco.Core.Utils;
    using Eco.Core.Utils.Async;
    using Eco.Core.Utils.Threading;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.GameActions;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Objects;
    using Eco.Gameplay.Plants;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Systems.Chat;
    using Eco.Gameplay.Systems.Messaging.Chat;
    using Eco.Gameplay.Systems.Messaging.Chat.Commands;
    using Eco.Mods.TechTree;
    using Eco.Shared.Localization;
    using Eco.Shared.Math;
    using Eco.Shared.Networking;
    using Eco.Shared.Services;
    using Eco.Shared.Utils;
    using Eco.Simulation.Agents;
    using Eco.Simulation.Types;
    using Eco.Simulation.WorldLayers;
    using Eco.Simulation.WorldLayers.Layers;
    using Eco.World;
    using Eco.World.Blocks;
    using Eco.World.Utils;
    public class Startup : IInitializablePlugin, IModKitPlugin
    {
        public string GetStatus() => "Active";
        public void Initialize(TimedTask timer)
        {
            //foreach(WorldLayer layer in WorldLayerManager.Obj.Layers)
            ///{
            //    Log.WriteErrorLineLocStr(layer.Name + " : " + layer.GetValue(LayerPosition.FromWorldPosition(new Vector2i(200, 200), layer.Settings.VoxelsPerEntry)));
            //}
            Vector2i marsLowerLeft = new Vector2i(0, 0);
            Vector2i marsDimensions = new Vector2i(99, 99);
            
            MarsTerraforming.Terraform(marsLowerLeft, marsDimensions);
            //MarsTerraforming.CollapseAllOverhangs(new Vector2i(470, 420), new Vector2i(470, 420) + new Vector2i(150, 150));
        }
    }
    public class MarsTerraforming : IChatCommandHandler
    {
        /// <summary>
        /// Chat command for server administrators for repeating a message back to the sender
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Transform the area into a Martian landscape", ChatAuthorizationLevel.Admin)]
        public static void MakeMars(User user, string argumentString = "")
        {
            if (argumentString.Length == 0)
            {
                user.MsgLocStr("Specify coords");
                return;
            }
            string[] parts = argumentString.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts.Any(s => !int.TryParse(s, out int n)))
            {
                user.MsgLocStr("Specify integer coords in the format: lower left x,lower left z,width,height", NotificationStyle.Info);
                return;
            }
            int[] coords = parts.Select(s => int.Parse(s)).ToArray();
            Vector3 worldSize = World.World.WrappedVoxelSize;
            Log.WriteErrorLineLocStr(string.Format("World size: {0},{1},{2}", worldSize.x, worldSize.y, worldSize.z));

            Vector2i corner1 = new Vector2i(coords[0], coords[1]);
            Vector2i corner2 = new Vector2i(coords[0] + coords[2] - 1, coords[1] + coords[3] - 1);
            corner2 = ClampCorner2(corner1, corner2);
            Log.WriteErrorLineLocStr("Terraforming " + corner1 + "-" + corner2 + " (unclamped coords)");
            Terraform(corner1, corner2);

            Log.WriteErrorLineLocStr("Finished terraforming");
        }
        /// <summary>
        /// Collapse overhangs where the solid rock thickness is fewer than three blocks thick
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Collapse overhangs where the solid rock thickness is fewer than three blocks thick", ChatAuthorizationLevel.Admin)]
        public static void CollapseOverhangs(User user, string argumentString = "")
        {
            if (argumentString.Length == 0)
            {
                user.MsgLocStr("Specify coords", NotificationStyle.Info);
                return;
            }
            string[] parts = argumentString.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts.Any(s => !int.TryParse(s, out int n)))
            {
                user.MsgLocStr("Specify integer coords in the format: lower left x,lower left z,width,height", NotificationStyle.Info);
                return;
            }
            int[] coords = parts.Select(s => int.Parse(s)).ToArray();
            Vector2i corner1 = new Vector2i(coords[0], coords[1]);
            Vector2i corner2 = new Vector2i(coords[0] + coords[2] - 1, coords[1] + coords[3] - 1);
            corner2 = ClampCorner2(corner1, corner2); 
            CollapseAllOverhangs(corner1, corner2);

            Log.WriteErrorLineLocStr("Finished collapsing");
        }
        /// <summary>
        /// Collapse overhangs where the solid rock thickness is fewer than three blocks thick
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Collapse overhangs where the solid rock thickness is fewer than three blocks thick", ChatAuthorizationLevel.Admin)]
        public static void KillPlants(User user, string argumentString = "")
        {
            if (argumentString.Length == 0)
            {
                user.MsgLocStr("Specify coords", NotificationStyle.Info);
                return;
            }
            string[] parts = argumentString.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts.Any(s => !int.TryParse(s, out int n)))
            {
                user.MsgLocStr("Specify integer coords in the format: lower left x,lower left z,width,height", NotificationStyle.Info);
                return;
            }
            int[] coords = parts.Select(s => int.Parse(s)).ToArray();
            Vector2i corner1 = new Vector2i(coords[0], coords[1]);
            Vector2i corner2 = new Vector2i(coords[0] + coords[2] - 1, coords[1] + coords[3] - 1); KillAllPlants(corner1, corner2);
            corner2 = ClampCorner2(corner1, corner2);
            Log.WriteErrorLineLocStr("Finished killing plants");
        }
        /// <summary>
        /// Evaporate water above this depth
        /// </summary>
        /// <param name="user">User sending the message</param>
        /// <param name="message">Message to be repeated</param>
        [ChatCommand("Evaporate water above this depth", ChatAuthorizationLevel.Admin)]
        public static void EvaporateAbove(User user, string argumentString = "")
        {
            if (argumentString.Length == 0)
            {
                user.MsgLocStr("Specify coords", NotificationStyle.Chat);
                return;
            }
            string[] parts = argumentString.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5 || parts.Any(s => !int.TryParse(s, out int n)))
            {
                user.MsgLocStr("Specify integer coords in the format: lower left x,lower left z,width,height,depth to keep", NotificationStyle.Info);
                return;
            }
            int[] coords = parts.Select(s => int.Parse(s)).ToArray();
            Vector2i corner1 = new Vector2i(coords[0], coords[1]);
            Vector2i corner2 = new Vector2i(coords[0] + coords[2] - 1, coords[1] + coords[3] - 1);
            corner2 = ClampCorner2(corner1, corner2);
            int depth = coords[4];
            RemoveAllWaterAboveHeight(corner1, corner2, depth);

            Log.WriteErrorLineLocStr("Finished evaporating");
        }
        public static Vector2i ClampCorner2(Vector2i corner1, Vector2i corner2)
        {
            //corner 1 = (2,2), corner2 = (9,9), world size=(4,4)
            //corner2 = (5,5)
            Vector2i clampedToWorldSize = new Vector2i(corner1.x + Math.Min(World.World.WrappedVoxelSize.x - 1, corner2.x - corner1.x), corner1.y + Math.Min(World.World.WrappedVoxelSize.z - 1, corner2.y - corner1.y));
            return clampedToWorldSize;
        }
        public static void Terraform(Vector2i corner1, Vector2i corner2)
        {
            Vector3 worldSize = World.World.WrappedVoxelSize;
            corner2 = ClampCorner2(corner1, corner2);
            WipeLayersForMars(corner1, corner2);
            KillAllPlants(corner1, corner2);
            ReplaceRockWithMartianRock(corner1, corner2);
            RemoveAllWaterAboveHeight(corner1, corner2, 30);
            CollapseAllOverhangs(corner1, corner2);
            List<MarsBounds> newMarsBounds = MarsMultiBounds.Mars.Bounds;
            newMarsBounds.Add(new MarsBounds(corner1, corner2 - corner1));
            MarsMultiBounds.Mars = new MarsMultiBounds(newMarsBounds);
        }
        private static void ReplaceRockWithMartianRock(Vector2i corner1, Vector2i corner2)
        {
            corner2 = ClampCorner2(corner1, corner2);
            Dictionary<Vector3i, Block> topBlocks = GetTopGroundOrWaterBlocks(corner1, corner2);
            Dictionary<Vector3i, Block> inQueue = topBlocks;
            while (inQueue.Count() > 0)
            {
                Log.WriteErrorLineLocStr("Got " + inQueue.Count() + " blocks");
                IEnumerable<BlockChange> blockChanges = new MarsBlockSwitcher().SwitchBlockTypes(inQueue);
                PerformEdits(blockChanges);
                inQueue = LayerBelow(inQueue.Keys);
            }
        }
        public static void CollapseAllOverhangs(Vector2i corner1, Vector2i corner2)
        {
            corner2 = ClampCorner2(corner1, corner2);
            for (int x = corner1.x; x <= corner2.x; x++)
            {
                List<Vector3i> rowPositions = new List<Vector3i>();
                for (int z = corner1.y; z <= corner2.y; z++)
                {
                    try
                    {

                        rowPositions.Add(World.World.GetWrappedWorldPosition(new Vector3i(x, 1, z)));

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
                        PerformEdits(blockChanges);
                        Log.WriteErrorLineLocStr("" + changedStackCoords.Count() + " changed columns in x=" + x);
                    }
                }
                while (changedStackCoords.Count() > 0);
            }
        }
        public static void KillAllPlants(Vector2i corner1, Vector2i corner2)
        {
            corner2 = ClampCorner2(corner1, corner2);
            Dictionary<Vector3i, Block> topBlocks = GetTopGroundBlocks(corner1, corner2);
            foreach(Vector3i groundPos in topBlocks.Keys)
            {
                Vector3i plantPos = groundPos;
                Plant plant = PlantBlock.GetPlant(plantPos);
                PlantBlock plantBlock = World.World.GetBlock(plantPos) as PlantBlock;
                if (plant != null)
                {
                    plant.Destroy();
                }
                if (plantBlock != null)
                {
                    plantBlock.Destroyed(plantPos, Block.Empty);
                }
                plantPos = groundPos + Vector3i.Up;
                plant = PlantBlock.GetPlant(plantPos);
                plantBlock = World.World.GetBlock(plantPos) as PlantBlock;
                if (plant != null)
                {
                    plant.Destroy();
                }
                if (plantBlock != null)
                {
                    plantBlock.Destroyed(plantPos, Block.Empty);
                }
            }
        }
        public static void WipeLayersForMars(Vector2i corner1, Vector2i corner2)
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
                "SaltWaterSpread",

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

            };
            layerNames.AddRange(WorldLayerManager.Obj.Layers.Where(layer => layer.Name.EndsWith("Potential") || layer.Name.EndsWith("Biome") || layer.Name.EndsWith("Capacity") || layer.Name.EndsWith("Growth")).Select(layer => layer.Name));
            WipeLayers(corner1, corner2, layerNames);
        }
        public static void WipeLayers(Vector2i corner1, Vector2i corner2, IEnumerable<string> layerNames, float newValue = 0f)
        {
            List<WorldLayer> validLayers = new List<WorldLayer>();
            foreach (string layerName in layerNames)
            {
                WorldLayer layer = WorldLayerManager.Obj.GetLayer(layerName);
                if (layer != null)
                {
                    for (int x = corner1.x; x <= corner2.x; x++)
                    {
                        for (int z = corner1.y; z <= corner2.y; z++)
                        {
                            Vector2i pos = World.World.GetWrappedWorldPosition(new Vector2i(x,  z));

                            layer.SetAtWorldPos(pos, newValue);
                        }
                    }
                    Log.WriteErrorLineLocStr("Set " + layerName + " layer");
                    validLayers.Add(layer);
                }
                else
                {
                    Log.WriteErrorLineLocStr(layerName + " does not exist");
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
                    Log.WriteErrorLineLocStr("Something went wrong with layer: " + layerName);
                }
            }
            Log.WriteErrorLineLocStr("Finished wiping layers");
        }
        private static void RemoveAllWaterAboveHeight(Vector2i corner1, Vector2i corner2, int height)
        {
            corner2 = ClampCorner2(corner1, corner2);
            Dictionary<Vector3i, Block> layer = new Dictionary<Vector3i, Block>();
            for (int x = corner1.x; x <= corner2.x; x++)
            {
                for (int z = corner1.y; z <= corner2.y; z++)
                {
                    Vector3i basePos = World.World.GetWrappedWorldPosition(new Vector3i(x, height, z));
                    layer.Add(basePos, World.World.GetBlock(basePos));
                }
            }
            List<BlockChange> blockChanges = new List<BlockChange>();
            while(layer.Count() > 0)
            {
                foreach(Vector3i pos in layer.Keys)
                {
                    if (layer[pos].GetType() == typeof(WaterBlock) || layer[pos].GetType() == typeof(EncasedWaterBlock))
                    {
                        BlockChange blockChange = new BlockChange();
                        blockChange.Position = pos;
                        blockChange.BlockType = typeof(EmptyBlock);
                        blockChanges.Add(blockChange);
                    }
                }

                Log.WriteErrorLineLocStr("Got " + layer.Count() + " blocks");
                layer = LayerAbove(layer.Keys);
            }
            PerformEdits(blockChanges);
        }
        private static void PerformEdits(IEnumerable<BlockChange> blockChanges)
        {
            try
            {
                World.World.BatchApply(blockChanges);
            }
            catch
            {
                Log.WriteErrorLineLocStr("PerformEdits failed\n" + string.Join("\n", blockChanges.Select(c => c.Position.ToStringBasic() + ":" + c.BlockType.Name)));
                
                throw;
            }
        }
        private static Dictionary<Vector3i, Block> GetTopGroundOrWaterBlocks(Vector2i corner1, Vector2i corner2)
        {
            Dictionary<Vector3i, Block> topBlocks = new Dictionary<Vector3i, Block>();
            for (int x = corner1.x; x <= corner2.x; x++)
            {
                for (int y = corner1.y; y <= corner2.y; y++)
                {
                    Vector2i wrappedPos = World.World.GetWrappedWorldPosition(new Vector2i(x, y));
                    Vector3i topPosition = new Vector3i(wrappedPos.x, World.World.MaxLandOrWaterUnwrapped(wrappedPos), wrappedPos.y);
                    topBlocks.Add(topPosition, World.World.GetBlock(topPosition));
                }
            }
            return topBlocks;
        }
        private static Dictionary<Vector3i, Block> GetTopGroundBlocks(Vector2i corner1, Vector2i corner2)
        {
            Dictionary<Vector3i, Block> topBlocks = new Dictionary<Vector3i, Block>();
            for (int x = corner1.x; x <= corner2.x; x++)
            {
                for (int y = corner1.y; y <= corner2.y; y++)
                {
                    Vector2i wrappedPos = World.World.GetWrappedWorldPosition(new Vector2i(x, y));
                    Vector3i topPosition = World.World.MaxYPos(wrappedPos);
                    topBlocks.Add(topPosition, World.World.GetBlock(topPosition));
                }
            }
            return topBlocks;
        }
        private static Dictionary<Vector3i, Block> LayerAbove(IEnumerable<Vector3i> layer)
        {
            Dictionary<Vector3i, Block> nextLayer = new Dictionary<Vector3i, Block>();
            foreach (Vector3i pos in layer)
            {
                if (pos.y <= World.World.MaxLandOrWaterUnwrapped(pos.XZ))
                {
                    Vector3i above = new Vector3i(pos.x, pos.y + 1, pos.z);
                    nextLayer.Add(above, World.World.GetBlock(above));
                }
            }
            return nextLayer;
        }
        private static Dictionary<Vector3i, Block> LayerBelow(IEnumerable<Vector3i> layer)
        {
            Dictionary<Vector3i, Block> nextLayer = new Dictionary<Vector3i, Block>();
            foreach (Vector3i pos in layer)
            {
                if (pos.y >= 1)
                {
                    Vector3i beneath = new Vector3i(pos.x, pos.y - 1, pos.z);
                    nextLayer.Add(beneath, World.World.GetBlock(beneath));
                }
            }
            return nextLayer;
        }
        private static IEnumerable<BlockChange> CollapseOverhangsChanges(IEnumerable<Vector3i> basePositions, out IEnumerable<Vector3i> changedStacks)
        {
            List<BlockChange> blockChanges = new List<BlockChange>();
            Dictionary<Vector2i, Vector3i> changedStacksCoords = new Dictionary<Vector2i, Vector3i>();
            foreach(Vector3i basePosition in basePositions)
            {
                int maxY = World.World.MaxYPos(basePosition.XZ).y;
                int caveFloorY = 1;
                bool inCave = false;
                for (int y = basePosition.y; y <= maxY; y++)
                {
                    try
                    {
                        Vector3i pos = basePosition.XZ.X_Z(y);
                        if (IsCave(pos))
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
                            if (IsCave(pos - Vector3i.Up))
                            {
                                //with pos as the base, how many blocks of contiguous solid rock are there (inclusive) 
                                int rockThickness = RockThickness(pos);
                                //collapse only where the solid rock is one or no blocks thick
                                if (rockThickness <= 1)
                                {
                                    //crushed rock, and any other solid blocks which aren't raw rock
                                    int nonRockThickness = NonRockThickness(pos + Vector3i.Up * rockThickness);
                                    int collapsedThickness = rockThickness + nonRockThickness;
                                    int depthDropped = y - caveFloorY;
                                    //collapse the column and replace where it came from with air
                                    blockChanges.AddRange(CollapseColumn(pos, collapsedThickness, depthDropped));
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
        private static IEnumerable<BlockChange> CollapseColumn(Vector3i lowestPos, int thickness, int dropHeight)
        {
            List<BlockChange> blockChanges = new List<BlockChange>();
            try
            {
                //move blocks down
                for (int i = 0; i < thickness; i++)
                {
                    Vector3i oldPos = lowestPos + Vector3i.Up * i;
                    Vector3i newPos = oldPos - Vector3i.Up * dropHeight;
                    if (newPos.y <= 0)
                    {
                        Log.WriteErrorLineLocStr("too low " + lowestPos + " thickness=" + thickness + " drop height=" + dropHeight + " newPos=" + newPos);
                    }
                    Block block = World.World.GetBlock(oldPos);
                    Type newType = CollapsedType(block.GetType());
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
        private static Type CollapsedType(Type currentType)
        {
            CollapsedBlockSwitcher blockSwitcher = new CollapsedBlockSwitcher();
            return blockSwitcher.GetNewBlockType(currentType);
        }
        private static int RockThickness(Vector3i basePos)
        {
            try
            {
                for (int y = 0; y <= World.World.MaxYPos(basePos.XZ).y; y++)
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
                Log.WriteErrorLineLocStr("RockThickness failed " + basePos);
                throw;
            }
            return World.World.MaxYPos(basePos.XZ).y - basePos.y + 1;
        }
        private static int NonRockThickness(Vector3i basePos)
        {
            try
            {
                for (int y = 0; y <= World.World.MaxYPos(basePos.XZ).y; y++)
                {
                    if (IsSolidRock(basePos + Vector3i.Up * y))
                    {
                        return y;
                    }
                }
            }
            catch
            {
                Log.WriteErrorLineLocStr("NonRockThickness failed");
                throw;
            }
            return World.World.MaxYPos(basePos.XZ).y - basePos.y + 1;
        }
        private static int OverhangThickness(Vector3i roofPos)
        {
            for (int y = 0; y < World.World.MaxYPos(roofPos.XZ).y; y++)
            {
                if (IsCave(roofPos + Vector3i.Up * y))
                {
                    return y;
                }
            }
            return World.World.MaxYPos(roofPos.XZ).y - roofPos.y + 1;
        }
        /// <summary>
        /// Returns if this position is somewhere an unsupported column can collapse into i.e. is air or water
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        private static bool IsCave(Vector3i pos)
        {
            try
            {
                Block block = World.World.GetBlock(pos);
                return (block != null && block.IsWater()) || IsCaveType(block.GetType());
            }
            catch
            {
                Log.WriteErrorLineLocStr("IsCave failed " + pos);
                throw;
            }
        }
        private static bool IsCaveType(Type blockType)
        {
            return blockType == typeof(EmptyBlock) || blockType == typeof(WaterBlock) || blockType == typeof(EncasedWaterBlock);
        }
        private static bool IsSolidRock(Vector3i pos)
        {
            Block block = World.World.GetBlock(pos);
            return IsSolidRockType(block.GetType());
        }
        private static bool IsSolidRockType(Type blockType)
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
        public class MarsBlockSwitcher
        {
            public Type GetNewBlockType(Type currentType)
            {
                Dictionary<Type, Type> blockSwitches = new Dictionary<Type, Type>()
                {
                    { typeof(DirtBlock), typeof(EmptyBlock) },
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
                    { typeof(RiverbedBlock), typeof(CrushedSandstoneBlock) },
                    { typeof(RiverSandBlock), typeof(CrushedSandstoneBlock) },
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
                if (blockSwitches.ContainsKey(currentType))
                {
                    return blockSwitches[currentType];
                }
                return currentType;
            }
            public IEnumerable<BlockChange> SwitchBlockTypes(IEnumerable<KeyValuePair<Vector3i, Block>> blocks)
            {
                List<BlockChange> blockChanges = new List<BlockChange>();
                foreach (KeyValuePair<Vector3i, Block> pair in blocks)
                {
                    Vector3i position = pair.Key;
                    Block block = pair.Value;
                    Type newBlockType = GetNewBlockType(block.GetType());
                    BlockChange blockChange = new BlockChange();
                    blockChange.Position = position;
                    blockChange.BlockType = newBlockType;
                    blockChanges.Add(blockChange);
                }
                return blockChanges;
            }
        }
        public class CollapsedBlockSwitcher
        {
            public Type GetNewBlockType(Type currentType)
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
                    { typeof(EncasedWaterBlock), typeof(EmptyBlock) }
                };
                if (blockSwitches.ContainsKey(currentType))
                {
                    return blockSwitches[currentType];
                }
                return currentType;
            }

        }
    }
}