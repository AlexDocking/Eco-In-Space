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
    using Eco.Gameplay.GameActions;
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

    public class SkidSteerCaloriesPlugin : IInitializablePlugin, IModKitPlugin, IGameActionAware
    {
        public string GetStatus() => "Active";

        public void Initialize(TimedTask timer)
        {
            ActionUtil.AddListener(this);
        }
        public void ActionPerformed(GameAction gameAction)
        {
            if (gameAction is DigOrMine)
            {
                OnDigOrMine(gameAction as DigOrMine);
            }
        }
        public Result ShouldOverrideAuth(GameAction action)
        {
            return Result.None("");
        }
        public void OnDigOrMine(DigOrMine action)
        {
            if (action.ToolUsed is SkidSteerItem)
            {
                action.Citizen.MsgLocStr("Skid steers now need calories", MessageCategory.InfoBox);
                action.CurrentPack.BurnCalories(action.Citizen, 10);
                action.CaloriesToConsume = 10;
                action.CurrentPack.AddPostEffect(() => action.Citizen.Stomach.LoseCalories(10));
            }
        }
    }
}