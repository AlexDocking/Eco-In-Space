namespace Eco
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Eco.Core.Plugins.Interfaces;
    using Eco.Core.Utils;
    using Eco.Core.Utils.Async;
    using Eco.Gameplay.Components;
    using Eco.Gameplay.Objects;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Systems.Chat;
    using Eco.Mods.TechTree;
    using Eco.Shared.Localization;
    using Eco.Shared.Math;
    using Eco.Shared.Networking;
    using Eco.Shared.Services;
    using Eco.Shared.Utils;
    using Eco.World.Blocks;
    using Eco.World.Utils;

    public class TeleportPlugin : IInitializablePlugin, IModKitPlugin
    {
        public string GetStatus() => "Active";

        public void Initialize(TimedTask timer)
        {
            UserManager.OnUserLoggedIn.Add(OnUserLoggedIn);
            UserManager.OnUserLoggedOut.Add(OnUserLoggedOut);
        }
        public static Dictionary<User, Action> OnMovedHandlers = new Dictionary<User, Action>();
        public static Dictionary<VehicleComponent, Action> VehicleOnMovedHandlers = new Dictionary<VehicleComponent, Action>();
        //OnMoved can get called several times before the teleporter takes effect. The code gets run and the player moves, but other threads don't know it yet
        //Ignore OnMoved if the last teleport was within the last second
        public static Dictionary<User, DateTime> LastTeleportTime = new Dictionary<User, DateTime>();
        private static Dictionary<User, Teleportation> Teleportations = new Dictionary<User, Teleportation>();
        private abstract class Teleportation
        {
            public User user;
            public MountComponent playerVehicle;
            public Teleporter teleporter;
            public Vector3 playerDestination;
            public Quaternion newRotation;
            public Vector3 vehicleDestination;
            public Quaternion vehicleRotation;
            public Teleportation(User user, MountComponent playerVehicle, Teleporter teleporter)
            {
                this.user = user;
                this.playerVehicle = playerVehicle;
                this.teleporter = teleporter;
            }
            public abstract bool IsFinished();
            public abstract bool ExecuteNextStage();
            public abstract float DelayBetweenStages();
        }
        private class StagedTeleportation : Teleportation
        {
            private bool isFinished = false;
            private bool running = false;
            private object runningLock = new object();
            public StagedTeleportation(User user, MountComponent playerVehicle, Teleporter teleporter) : base(user, playerVehicle, teleporter)
            {

            }
            public override float DelayBetweenStages()
            {
                return 5f;
            }
            private void FinishAndCleanup()
            {
                Teleportations.Remove(user);
                isFinished = true;
            }
            /// <summary>
            /// 1. Check calories and abort if failed
            /// 2. Calculate destinations
            /// 3. Dismount the vehicle
            /// 4. Teleport vehicle
            /// If no vehicle, teleport player and finish
            /// </summary>
            /// <returns></returns>
            private bool Stage1()
            {
                lock (this)
                {
                    if (!isFinished)
                    {
                        if (!user.LoggedIn)
                        {
                            FinishAndCleanup();
                            return false;
                        }
                        if (user.Stomach.Calories >= 0 && user.Stomach.Calories >= teleporter.TotalCalorieCost)
                        {
                            this.newRotation = user.Rotation;// Quaternion.LookRotation(Vector3.Right, Vector3.Up);

                            this.playerDestination = teleporter.Destination(user.Position);
                            Vector3? vehicleOffset = null;
                            if (playerVehicle != null)
                            {
                                vehicleOffset = playerVehicle.Parent.Position - user.Position;
                                TeleportPlugin.PrepareVehicleTeleport(playerVehicle);
                                this.vehicleDestination = playerDestination + vehicleOffset.Value;
                                this.vehicleRotation = playerVehicle.Parent.Rotation;
                                TeleportPlugin.TeleportPlayerVehicle(user.Player, playerVehicle, vehicleDestination, vehicleRotation);
                                //playerVehicle.Parent.GetComponent<VehicleComponent>().OnVehicleMoved.Add(VehicleOnMoved);
                                new DelayedAction(Stage2, 1500);
                            }
                            else
                            {
                                TeleportPlayer(user.Player, teleporter, playerDestination, newRotation);
                                FinishAndCleanup();
                            }

                            return true;
                        }
                        else
                        {
                            user.MsgLocStr("Too hungry to teleport! Need " + teleporter.TotalCalorieCost + " calories", MessageCategory.InfoBox);
                            return false;
                        }
                    }
                }
                return false;
            }
            /// <summary>
            /// 500 ms after previous stage
            /// 1. If the user is logged out, abort. Necessary because this stage can repeat
            /// 2. If vehicle hasn't moved to the destination, teleport it every 100 ms until it does
            /// 3. Teleport player
            /// </summary>
            private void Stage2()
            {
                lock (this)
                {
                    if (!isFinished)
                    {
                        if (!user.LoggedIn)
                        {
                            FinishAndCleanup();
                            return;
                        }
                        if (playerVehicle != null)
                        {
                            if (playerVehicle.Parent.Position.WrappedDistance(vehicleDestination) >= 0.5f)
                            {
                                TeleportPlayerVehicle(user.Player, playerVehicle, vehicleDestination, vehicleRotation);

                                new DelayedAction(Stage2, 200);
                                return;
                            }
                            TeleportPlayerVehicle(user.Player, playerVehicle, vehicleDestination, vehicleRotation);
                        }
                        TeleportPlayer(user.Player, teleporter, playerDestination + Vector3.Up * 3f, newRotation);
                        new DelayedAction(Stage3, 500);
                    }
                }
            }
            /// <summary>
            /// 1000 ms after previous stage
            /// 1. If player hasn't moved close enough to the destination, teleport the player every 100 ms until they are
            /// 2. Teleport the vehicle and player back to their targets, as they will have been moved by physics in the meantime
            /// 3. Remount the vehicle
            /// </summary>
            private void Stage3()
            {
                lock (this)
                {
                    if (!isFinished)
                    {
                        if (!user.LoggedIn || playerVehicle == null)
                        {
                            FinishAndCleanup();
                            return;
                        }
                        if (user.Player.Position.WrappedDistance(playerDestination + Vector3.Up * 3) >= 1f)
                        {
                            InternalTeleportPlayer(user.Player, playerDestination + Vector3.Up * 3, newRotation);
                            new DelayedAction(Stage3, 100);
                            return;
                        }
                        InternalTeleportPlayer(user.Player, playerDestination, newRotation);
                        TeleportPlayerVehicle(user.Player, playerVehicle, vehicleDestination, vehicleRotation);
                        Remount(user.Player, playerVehicle);
                        new DelayedAction(Stage4, 50);
                    }
                }
            }
            /// <summary>
            /// 50 ms after previous stage
            /// 1. Remount the vehicle, in case it didn't work first time, even though they will have moved slightly in that time
            /// </summary>
            private void Stage4()
            {
                lock (this)
                {
                    if (!isFinished)
                    {
                        Remount(user.Player, playerVehicle);
                        FinishAndCleanup();
                    }
                }
            }
            public override bool ExecuteNextStage()
            {
                lock (runningLock)
                {
                    if (running)
                    {
                        return false;
                    }
                    running = true;
                    return Stage1();
                }
            }

            public override bool IsFinished()
            {
                return isFinished;
            }
        }
        private class StagedTeleportation2 : Teleportation
        {
            public int stage = 0;
            public StagedTeleportation2(User user, MountComponent playerVehicle, Teleporter teleporter) : base(user, playerVehicle, teleporter)
            {

            }
            public override bool IsFinished()
            {
                return stage > 1;
            }
            public override bool ExecuteNextStage()
            {
                bool success = true;
                if (stage == 0)
                {
                    if (user.Stomach.Calories >= teleporter.TotalCalorieCost)
                    {
                        this.newRotation = user.Rotation;// Quaternion.LookRotation(Vector3.Right, Vector3.Up);

                        this.playerDestination = teleporter.Destination(user.Position);
                        Vector3? vehicleOffset = null;
                        if (playerVehicle != null)
                        {
                            vehicleOffset = playerVehicle.Parent.Position - user.Position;
                            //TeleportPlugin.PrepareVehicleTeleport(playerVehicle);
                            this.vehicleDestination = playerDestination + vehicleOffset.Value;
                            this.vehicleRotation = playerVehicle.Parent.Rotation;
                            TeleportPlugin.TeleportPlayerVehicle(user.Player, playerVehicle, vehicleDestination, vehicleRotation);
                        }
                        success = true;
                    }
                    else
                    {
                        user.MsgLocStr("Too hungry to teleport! Need " + teleporter.TotalCalorieCost + " calories", MessageCategory.InfoBox);
                        success = false;
                    }
                }
                else if (stage == 1)
                {
                    //move the vehicle again because it will have moved since we teleported it, as there is a delay between putting it in a new chunk and the player being taken to it
                    if (playerVehicle != null)
                    {
                        TeleportPlugin.TeleportPlayerVehicle(user.Player, playerVehicle, vehicleDestination, vehicleRotation);
                    }
                    InternalTeleportPlayer(user.Player, playerDestination, newRotation);
                    if (playerVehicle != null)
                    {
                        Remount(user.Player, playerVehicle);
                        Unclip(user.Player, playerVehicle);
                    }
                    success = true;
                }
                stage += 1;
                return success;
            }
            public override float DelayBetweenStages()
            {
                return 0.5f;
            }
        }
        public static List<Teleporter> Teleporters = new List<Teleporter>()
        {
            new Teleporter(48, 50, 50, 70, 215, 225, 200, -500, -1),
            new Teleporter(230, 233, 60, 65, 226, 230, -1, -500, -1, damageCost:100),
            new Teleporter(236, 238, 170, 172, 226, 230, -1, -80, -1)
        };
        public static Action MakeOnMoved(User user)
        {
            Action action = () =>
            {
                lock (Teleportations)
                {
                    //if there is teleportation in progress
                    if (Teleportations.ContainsKey(user))
                    {
                        //if it has been long enough since the last stage we can do the next stage
                        if (LastTeleportTime[user].AddSeconds(Teleportations[user].DelayBetweenStages()) < DateTime.Now)
                        {
                            TeleportFar(user, Teleportations[user].teleporter);
                            LastTeleportTime[user] = DateTime.Now;
                        }
                    }
                    //forget about teleportations more than one second ago, as they are probably from a different event
                    else if (LastTeleportTime.ContainsKey(user) && LastTeleportTime[user].AddSeconds(1f) < DateTime.Now)
                    {
                        LastTeleportTime.Remove(user);
                    }
                    if (!LastTeleportTime.ContainsKey(user))
                    {
                        Teleporter? teleporter = ActivatedTeleporter(user);
                        if (teleporter.HasValue)
                        {
                            TeleportFar(user, teleporter.Value);
                            LastTeleportTime.TryAdd(user, DateTime.Now);
                        }
                    }
                }
            };
            return action;
        }
        public static Teleporter? ActivatedTeleporter(User user)
        {
            foreach (Teleporter teleporter in Teleporters)
            {
                if (teleporter.InZone(user.Position))
                {
                    return teleporter;
                }
            }
            return null;
        }
        public static bool TeleportFar(User user, Teleporter teleporter)
        {
            Teleportation teleportation;
            if (Teleportations.ContainsKey(user))
            {
                teleportation = Teleportations[user];
            }
            else
            {
                MountComponent playerVehicle = FindCurrentVehicle(user.Player);
                teleportation = new StagedTeleportation(user, playerVehicle, teleporter);
                Teleportations.Add(user, teleportation);
            }
            if (!teleportation.ExecuteNextStage())
            {
                Teleportations.Remove(user);
                return false;
            }
            if (teleportation.IsFinished())
            {
                Teleportations.Remove(user);
                return true;
            }            
            
            return false;
        }
        //returns whether the teleportation process completed
        public static bool Teleport(User user, Teleporter teleporter)
        {
            try
            {
                if (user.Stomach.Calories >= teleporter.TotalCalorieCost)
                {
                    Quaternion newRotation = user.Rotation;// Quaternion.LookRotation(Vector3.Right, Vector3.Up);

                    MountComponent playerVehicle = FindCurrentVehicle(user.Player);
                    Vector3? vehicleOffset = null;
                    if (playerVehicle != null)
                    {
                        vehicleOffset = playerVehicle.Parent.Position - user.Position;
                        PrepareVehicleTeleport(playerVehicle);
                    }

                    Vector3 teleportDestination = teleporter.Destination(user.Position);
                    InternalTeleportPlayer(user.Player, teleportDestination, newRotation);
                    if (playerVehicle != null)
                    {
                        Vector3 vehicleTeleportDestination = teleportDestination + vehicleOffset.Value;
                        TeleportPlayerVehicle(user.Player, playerVehicle, vehicleTeleportDestination, playerVehicle.Parent.Rotation);
                        Remount(user.Player, playerVehicle);
                        Unclip(user.Player, playerVehicle);
                    }
                    return true;
                }
                else
                {
                    user.MsgLocStr("Too hungry to teleport! Need " + teleporter.TotalCalorieCost + " calories", MessageCategory.InfoBox);
                }
            }
            catch
            {
                return false;
            }
            return false;
        }
        /// <summary>
        /// If the vehicle clips into the ground upon arrival, move the player and vehicle up to unclip from the ground
        /// </summary>
        /// <param name="player"></param>
        /// <param name="playerVehicle"></param>
        public static void Unclip(Player player, MountComponent playerVehicle)
        {
            return;
            if (playerVehicle == null)
            {
                return;
            }
            IEnumerable<Vector3i> groundBelow = playerVehicle.Parent.GroundBelow();
            //if there is ground
            if (groundBelow.Count() > 0)
            {
                //be extra generous with the unclipping with a 0.1 leeway. The 0.5 is to get the top of the ground block instead of the centre
                float clipAmount = 0.1f + 0.5f + groundBelow.Max(blockPos => blockPos.y) - playerVehicle.Parent.Bounds.Bottom;
                Unclip(player, playerVehicle, clipAmount);

            }
            else
            {
                //if there is no ground
                Unclip(player, playerVehicle, 1f);
                Unclip(player, playerVehicle);
            }

        }
        public static void Unclip(Player player, MountComponent playerVehicle, float clipAmount)
        {
            if (playerVehicle == null)
            {
                return;
            }
            if (clipAmount > 0)
            {
                Log.WriteErrorLineLocStr("Saved from clipping " + clipAmount);
                Vector3 vehicleOffset = playerVehicle.Parent.Position - player.Position;
                PrepareVehicleTeleport(playerVehicle);

                Vector3 teleportDestination = player.Position + Vector3.Up * clipAmount;
                InternalTeleportPlayer(player, teleportDestination, player.Rotation);

                Vector3 vehicleTeleportDestination = teleportDestination + vehicleOffset;
                TeleportPlayerVehicle(player, playerVehicle, vehicleTeleportDestination, playerVehicle.Parent.Rotation);

            }
        }
        public static void TeleportPlayer(Player player, Teleporter? teleporter, Vector3 newPosition, Quaternion newRotation)
        {
            InternalTeleportPlayer(player, newPosition, newRotation);
            if (teleporter != null)
            {
                //Show the damage screen effect and lose 5x the damage cost in calories
                player.User.Stomach.LoseCalories(5 * teleporter.Value.damageCost, true);
                player.MsgLocStr("Whoosh!", MessageCategory.InfoBox);
            }
        }
        private static void InternalTeleportPlayer(Player player, Vector3 newPosition, Quaternion newRotation)
        {
            Log.WriteErrorLineLocStr("User Was moved");
            player.SetPosition(newPosition);
        }
        public static void TeleportPlayerVehicle(Player player, MountComponent mountComponent, Vector3 vehicleTeleportDestination, Quaternion vehicleRotation)
        {
            if (mountComponent == null)
            {
                return;
            }
            Log.WriteErrorLineLocStr("Vehicle was moved");
            mountComponent.Parent.Position = vehicleTeleportDestination;
            mountComponent.Parent.Rotation = vehicleRotation;
            mountComponent.Parent.SyncPositionAndRotation();
            //might not do anything
            mountComponent.Parent.PlaceWorldObjectBlocks();
            for (int i = 0; i < mountComponent.Parent.Components.Count; i++)
            {
                WorldObjectComponent worldObjectComponent = mountComponent.Parent.Components[i];
                Log.WriteErrorLineLocStr(mountComponent.Parent.Name + " has " + worldObjectComponent.GetType().Name);
            }

            //might not do anything
            mountComponent.Parent.TickComponents();
            //might not do anything
            mountComponent.Parent.Tick();
            World.World.AwakeNear(vehicleTeleportDestination.Floor);

            //might not do anything
            mountComponent.Parent.UpdateEnabledAndOperating();
            //might not do anything
            mountComponent.Parent.SetDirty();
        }
        public static void Remount(Player player, MountComponent mountComponent)
        {
            if (mountComponent == null)
            {
                return;
            }
            Log.WriteErrorLineLocStr("Remount");
            mountComponent.Driver = player;
            mountComponent.Parent.GetComponent<VehicleComponent>().Driver = player;

            //might not do anything
            mountComponent.Parent.SetDirty();
            //might not do anything
            mountComponent.Parent.TickComponents();
            //might not do anything
            mountComponent.Parent.Tick();

            //might not do anything
            mountComponent.Parent.UpdateEnabledAndOperating();


            mountComponent.Driver = player;
            mountComponent.Parent.GetComponent<VehicleComponent>().Driver = player;

            //might not do anything
            mountComponent.Parent.SetDirty();
            //might not do anything
            mountComponent.Parent.TickComponents();
            //might not do anything
            mountComponent.Parent.Tick();

            //might not do anything
            mountComponent.Parent.UpdateEnabledAndOperating();
        }
        public static MountComponent FindCurrentVehicle2(Player player)
        {
            MountComponent playerVehicle = null;
            Action<WorldObject> forEach = (WorldObject worldObject) =>
            {
                MountComponent mountComponent;
                if (worldObject.TryGetComponent<MountComponent>(out mountComponent))
                {
                    if (mountComponent.Driver == player)
                    {
                        playerVehicle = mountComponent;
                    }
                }
            };
            WorldObjectManager.ForEach(forEach);

            return playerVehicle;
        }
        public static MountComponent FindCurrentVehicle(Player player)
        {

            IEnumerable<MountComponent> mounts = WorldObjectUtil.AllObjsWithComponent<MountComponent>();
            foreach (MountComponent mountComponent in mounts)
            {
                if (mountComponent.Driver == player)
                {
                    return mountComponent;
                }
            }
            return null;
        }
        public static void PrepareVehicleTeleport(MountComponent mountComponent)
        {
            if (mountComponent == null)
            {
                return;
            }
            Log.WriteErrorLineLocStr("Dismount");

            ///mountComponent.DismountAll();
            //might not do anything
            mountComponent.Parent.TickComponents();
            //might not do anything
            mountComponent.Parent.Tick();

            //might not do anything
            mountComponent.Parent.UpdateEnabledAndOperating();
            //might not do anything
            mountComponent.Parent.SetDirty();
            //might not do anything
            mountComponent.Parent.TickComponents();
            //might not do anything
            mountComponent.Parent.Tick();

            //might not do anything
            mountComponent.Parent.UpdateEnabledAndOperating();
            //might not do anything
            mountComponent.Parent.SetDirty();
        }
        public static void OnUserLoggedIn(User user)
        {
            Action onMovedHandler = MakeOnMoved(user);
            OnMovedHandlers.Add(user, onMovedHandler);
            user.OnMoved.Add(onMovedHandler);
        }
        public static void OnUserLoggedOut(User user)
        {
            user.OnMoved.Remove(OnMovedHandlers[user]);
            OnMovedHandlers.Remove(user);
        }

        public struct Teleporter
        {
            public float xMin;
            public float xMax;
            public float yMin;
            public float yMax;
            public float zMin;
            public float zMax;
            public float xDest;
            public float yDest;
            public float zDest;
            public int damageCost;
            public Teleporter(float xMin, float xMax, float yMin, float yMax, float zMin, float zMax, float xDest, float yDest, float zDest, int damageCost = 0)
            {
                this.xMin = xMin;
                this.xMax = xMax;
                this.yMin = yMin;
                this.yMax = yMax;
                this.zMin = zMin;
                this.zMax = zMax;
                this.xDest = xDest;
                this.yDest = yDest;
                this.zDest = zDest;
                this.damageCost = damageCost;
            }
            public int TotalCalorieCost { get { return this.damageCost * 5; } }
            public bool InZone(Vector3 position)
            {
                if ((xMin == -1 || xMin <= position.x) && (xMax == -1 || xMax >= position.x))
                {
                    if ((yMin == -1 || yMin <= position.y) && (yMax == -1 || yMax >= position.y))
                    {
                        if ((zMin == -1 || zMin <= position.z) && (zMax == -1 || zMax >= position.z))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            public Vector3 Destination(Vector3 position)
            {
                //if -1, keep player x coord, else teleport to specific x
                float x = xDest == -1 ? position.x : xDest;
                //if -1, keep player z coord, else teleport to specific z
                float z = zDest == -1 ? position.z : zDest;
                //if negative, teleport to the ground below the altitide, otherwise if positive teleport directly to the specific y
                float y = yDest < 0 ? TeleportHeight(position, new Vector2(x, z).Floor) : yDest;
                return new Vector3(x, y, z);
            }
            public float TeleportHeight(Vector3 playerPosition, Vector2i destinationPositionXZ)
            {
                float teleportHeight = destinationPositionXZ.y;
                IEnumerable<Vector3i> playerBlockSurfaces = MapIterators.VoxelColumn(playerPosition.XZ.Floor);
                IEnumerable<Vector3i> targetBlockSurfaces = MapIterators.VoxelColumn(destinationPositionXZ);

                //put the player on top of the highest block at or lower than the magnitude of yDest
                if (playerBlockSurfaces.Count() > 0 && targetBlockSurfaces.Count() > 0)
                {
                    float groundHeight = playerBlockSurfaces.Where(block => block.y <= playerPosition.y && !(World.World.GetBlock(WrappedWorldPosition3i.Create(block.x, block.y, block.z)) is EmptyBlock)).Last().y;
                    float heightOffset = playerPosition.y - groundHeight;
                    float targetHeight = -yDest;
                    targetHeight = targetBlockSurfaces.Where(block => block.y <= targetHeight && !(World.World.GetBlock(WrappedWorldPosition3i.Create(block.x, block.y, block.z)) is EmptyBlock)).Last().y;
                    teleportHeight = targetHeight + heightOffset;
                    Log.WriteErrorLineLocStr("Height Offset " + heightOffset);
                    Log.WriteErrorLineLocStr("Destination Surface Height Last " + targetHeight.ToString());
                }
                else
                {
                    teleportHeight = playerPosition.y;
                }
                //add a little to not clip the ground
                return teleportHeight + 0.1f;
            }
        }
    }
    public static class HelperMethods
    {
        //Stomach.BurnCalories can go negative if too many calories are taken
        public static void LoseCalories(this Stomach stomach, float amount, bool damage = false)
        {
            if (damage)
            {
                if (stomach.Calories > 0)
                {
                    stomach.Owner.TryDamage(null, 0);
                }
                else
                {
                    //false means don't use calorie modifiers
                    stomach.Owner.ConsumeCalories(250);
                    stomach.Owner.Tick();
                    stomach.Owner.TryDamage(null, 0);
                }
            }
            stomach.BurnCalories(Math.Min(stomach.Calories, amount), false);
        }
    }
}