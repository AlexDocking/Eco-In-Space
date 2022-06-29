namespace EcoInSpace
{
    using Eco.Core.Utils.Async;
    using Eco.Gameplay.GameActions;
    using Eco.Mods.TechTree;
    using Eco.Shared.Math;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static partial class OxygenRoomManager
    {
        /// <summary>
        /// A multiple room system that has oxygen requirements and availability
        /// </summary>
        public class OxygenRoom
        {
            public float Airtightness
            {
                get
                {
                    return CalculateAirtightness(RoomSystem.WallCompositions);
                }
            }

            public float AirtightnessPenalty
            {
                get
                {
                    return CalculateAirtightnessPenalty(Airtightness);
                }
            }

            public float AirtightnessTier
            { get { return RoomSystem.AirtightnessTier; } }

            public float AverageTier
            { get { return RoomSystem.AverageTier; } }

            public bool Breathable
            {
                get
                {
                    return EffectiveFilteredFraction >= breathableRequirement;
                }
            }

            public List<WasteFilterObject> ConnectedWasteFilters
            {
                get
                {
                    return filterNetworks.SelectMany(network => network.ConnectedWasteFilters).ToList();
                }
            }

            public float EffectiveFilteredFraction
            {
                get
                {
                    return FilteredFraction * Airtightness;
                }
            }

            public float EffectiveFilteredVolume
            {
                get
                {
                    if (!IsPressurised)
                    {
                        return 0f;
                    }
                    return FilteredVolume * Airtightness;
                }
            }

            /// <summary>
            /// Fraction of the room volume which is purified
            /// </summary>
            public float FilteredFraction
            {
                get
                {
                    if (!IsPressurised)
                    {
                        return 0f;
                    }
                    return filterNetworks.Select(network => network.Efficiency).Sum();
                }
            }

            public float FilteredVolume
            {
                get
                {
                    return FilteredFraction * Volume;
                }
            }

            public List<PipeNetwork> FilterNetworks
            {
                get
                {
                    return filterNetworks;
                }
                set
                {
                    filterNetworks = value;
                }
            }

            public bool FullyPowered
            {
                get
                {
                    return FilterNetworks.All(network => network.FullyPowered);
                }
            }

            public bool IsPressurised
            {
                get
                {
                    return RoomSystem.IsPressurised;
                }
            }

            public Vector3i Position
            {
                get
                {
                    return RoomSystem.AverageEmptyPos;
                }
            }

            public List<WasteFilterObject> PoweredWasteFilters
            {
                get
                {
                    return filterNetworks.SelectMany(network => network.PoweredWasteFilters).ToList();
                }
            }

            //An oxygen room has one connected space, perhaps made up of many individual rooms, connected without doors
            public ConnectedRoom RoomSystem { get; private set; }

            public int Volume
            { get { return RoomSystem.Volume; } }

            //M^3 filtered by each powered filter
            public const int FilteredVolumePerFilter = 2000;

            /// <summary>
            /// Fraction of the room volume which would be purified if all the waste filters were powered
            /// </summary>
            private float MaximumFilteredFraction
            {
                get
                {
                    return filterNetworks.Select(network => network.MaximumEfficiency).Sum();
                }
            }

            //What fraction of the air must be filtered to be able to breathe
            private const float breathableRequirement = 1f;
            private List<PipeNetwork> filterNetworks = new List<PipeNetwork>();
            public OxygenRoom(ConnectedRoom room)
            {
                this.RoomSystem = room;
            }
            public static float AirtightnessToTier(float airtightness)
            {
                return (float)Math.Sqrt(4 * (airtightness - 0.2f) / 0.8f);
            }
            public static float CalculateAirtightness(IEnumerable<WallTypeComposition> wallCompositions)
            {
                return TierToAirtightness(wallCompositions.Select(wall => wall.AirtightnessTier * wall.Count).Sum() / wallCompositions.Select(wall => wall.Count).Sum());
            }
            public static float CalculateAirtightnessPenalty(float airtightness)
            {
                return 1f - airtightness;
            }
            public static int NumFiltersRequiredForVolume(int totalRoomVolume, float airtightness)
            {
                return (int)Math.Ceiling(totalRoomVolume / (airtightness * (float)OxygenRoom.FilteredVolumePerFilter));
            }
            public static float TierToAirtightness(float tier)
            {
                //T4:1^2   = 1
                //T3:0.8^2 = 0.64
                //T2:0.6^2 = 0.36
                //T1:0.4^2 = 0.16
                //T0:0.2^2 = 0.04 => 25x as much filtering capacity needed as T4
                return (float)Math.Pow(0.2f + 0.8f * tier / 4, 2);
            }
            /// <summary>
            /// Whether it is possible for this room to be breathable if the airtightness aka tier is maximised. Option as whether to consider all connected waste filters or only the ones with power
            /// </summary>
            /// <param name="requiredAirtightness"></param>
            /// <returns>True if there are enough waste filters to filter all the air in the case of no leakage i.e. Tier 4, false if there would still be too few</returns>
            public bool RequiredAirtightness(bool ignorePowerStatus, out float requiredAirtightness)
            {
                float filteredFraction = ignorePowerStatus ? MaximumFilteredFraction : FilteredFraction;

                requiredAirtightness = breathableRequirement / filteredFraction;
                //there is enough capacity to filter all the air in this room, if the room were at maximum airtightness
                if (FilteredFraction >= breathableRequirement)
                {
                    return true;
                }
                //there is not enough capacity to filter all the air in this room, even with maximum airtightness
                return false;
            }
            public bool RequiredTier(bool ignorePowerStatus, out float requiredTier)
            {
                bool possible = RequiredAirtightness(ignorePowerStatus, out float requiredAirtightness);
                requiredTier = AirtightnessToTier(requiredAirtightness);
                return possible;
            }
        }
    }
}