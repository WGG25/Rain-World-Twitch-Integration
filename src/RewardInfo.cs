using System;
using System.Reflection;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;

namespace TwitchIntegration
{
    internal class RewardInfo
    {
        private readonly IntegrationSystem _system;
        private CustomReward _onlineInfo;
        private bool _manageable;

        public string Title { get; }
        public string DisplayName { get; }
        public int DefaultCost { get; }
        public int DefaultDelay { get; }
        public bool AvailableInMenu { get; }
        public RedemptionHandler Handler { get; }

        public bool Manageable
        {
            get => _manageable || !Created;
            set => _manageable = value;
        }
        public bool AutoFulfill => Plugin.Config?.ShouldAutoFulfill(Title) ?? false;
        public string Id => _onlineInfo?.Id;
        public bool Created => _onlineInfo != null;
        public bool Enabled => _onlineInfo?.IsEnabled ?? false;
        public bool Paused => _onlineInfo?.IsPaused ?? false;
        public int Cost => _onlineInfo?.Cost ?? DefaultCost;
        public int Delay => _onlineInfo?.GlobalCooldownSetting is GlobalCooldownSetting cooldown ? (cooldown.IsEnabled ? cooldown.GlobalCooldownSeconds : 0) : DefaultDelay;
        
        public RewardInfo(TwitchRewardAttribute attribute, MethodInfo method, IntegrationSystem system)
        {
            _system = system;

            Title = attribute.RewardTitle;
            DefaultCost = attribute.DefaultCost;
            DefaultDelay = attribute.DefaultDelay;
            DisplayName = attribute.DisplayName;
            AvailableInMenu = attribute.AvailableInMenu;
            Handler = Delegate.CreateDelegate(typeof(RedemptionHandler), method, false) as RedemptionHandler;
            
            if (Handler == null)
                Plugin.Logger.LogError("Failed to create redemption handler: " + method.Name);
        }

        public void Create(int cost, int delay)
        {
            CreateCustomRewardsRequest req = new()
            {
                Title = Title,
                Cost = cost,
                IsGlobalCooldownEnabled = delay > 0,
                GlobalCooldownSeconds = delay > 0 ? delay : null,
                IsEnabled = true
            };
            _system.Api.Helix.ChannelPoints.CreateCustomRewardsAsync(_system.ChannelId, req)
                .ContinueWith(task => {
                    if (Utils.ValidateSuccess(task))
                    {
                        UpdateOnlineInfo(task.Result.Data[0]);
                        Manageable = true;
                    }
                });
        }

        public void Delete()
        {
            if (Id != null)
            {
                _system.Api.Helix.ChannelPoints.DeleteCustomRewardAsync(_system.ChannelId, Id)
                    .ContinueWith(task =>
                    {
                        if (Utils.ValidateSuccess(task))
                        {
                            Manageable = false;
                            UpdateOnlineInfo(null);
                        }
                    });
            }
        }

        public void Update(int? cost = null, int? delay = null, bool? enabled = null, bool? paused = null)
        {
            if (cost != null && cost != Cost
                || delay != null && delay != Delay
                || enabled != null && enabled != Enabled
                || paused != null && paused != Paused)
            {
                UpdateCustomRewardRequest req = new()
                {
                    Cost = cost,
                    IsGlobalCooldownEnabled = delay > 0,
                    GlobalCooldownSeconds = delay > 0 ? delay : null,
                    IsEnabled = enabled,
                    IsPaused = paused
                };
                _system.Api.Helix.ChannelPoints.UpdateCustomRewardAsync(_system.ChannelId, Id, req)
                    .ContinueWith(task => {
                        if (Utils.ValidateSuccess(task))
                            UpdateOnlineInfo(task.Result.Data[0]);
                    });
            }
        }

        public void UpdateOnlineInfo(CustomReward info)
        {
            _onlineInfo = info;

            Plugin.Config?.RewardsChanged();
        }
    }

    public delegate RewardStatus RedemptionHandler();
}
