using System;
using System.Collections.Generic;
using System.Reflection;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api;
using TwitchLib.PubSub;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using TwitchLib.PubSub.Events;
using Debug = UnityEngine.Debug;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;

namespace TwitchIntegration
{
    internal class IntegrationSystem : IDisposable
    {
        // Rewards
        public readonly Dictionary<string, Reward> Rewards = new();
        public bool? ChannelPointsAvailable { get; private set; }
        private readonly Dictionary<string, CustomReward> _onlineRewardsByTitle = new();
        private readonly HashSet<string> _manageableRewardIds = new();
        private bool _rewardsPaused = true;

        // Api
        public readonly TwitchAPI Api;
        public readonly string ChannelId;
        private readonly TwitchPubSub _pubSub;
        private readonly ConcurrentQueue<PendingRedemption> _redemptionQueue = new();

        public IntegrationSystem(TwitchAPI api, string channel)
        {
            Api = api;
            ChannelId = channel;
            _pubSub = new();

            // Scan for integration methods
            foreach (var m in typeof(Integrations).GetMethods())
            {
                var attribs = m.GetCustomAttributes(typeof(TwitchRewardAttribute), false);
                if (attribs == null || attribs.Length == 0) continue;

                var attrib = (TwitchRewardAttribute)attribs[0];

                Rewards[attrib.RewardTitle] = new Reward(attrib, m);
            }

            _pubSub.OnChannelPointsRewardRedeemed += OnRedemption;
            _pubSub.ListenToChannelPoints(channel);
            _pubSub.Connect();

            // Request existing rewards
            api.Helix.ChannelPoints.GetCustomRewardAsync(channel).ContinueWith(task =>
            {
                if (ValidateSuccess(task))
                {
                    foreach (var reward in task.Result.Data)
                        UpdateOnlineInfo(reward);

                    ChannelPointsAvailable = true;
                }
                else
                {
                    ChannelPointsAvailable = false;
                }
            });

            // Request owned rewards
            api.Helix.ChannelPoints.GetCustomRewardAsync(channel, onlyManageableRewards: true).ContinueWith(task =>
            {
                if (ValidateSuccess(task))
                {
                    foreach (var reward in task.Result.Data)
                        UpdateOnlineInfo(reward, true);
                }
            });
        }

        public void Update()
        {
            bool paused = RWCustom.Custom.rainWorld.processManager.currentMainLoop is not RainWorldGame game || game.pauseMenu?.counter > 40f * Integrations.minPauseTime;

            if(paused != _rewardsPaused)
            {
                _rewardsPaused = paused;

                foreach(var reward in Rewards)
                {
                    var info = GetCachedOnlineInfo(reward.Key);
                    if(info.IsPaused != paused)
                    {
                        
                    }
                }
            }

            while(_redemptionQueue.TryDequeue(out var redemption))
            {
                if(paused)
                    redemption.MarkCancelled();
                else
                    Redeem(redemption);
            }
        }

        private bool ValidateSuccess(Task task)
        {
            if(task.Exception is Exception e)
            {
                if (e is AggregateException ae)
                    foreach (var inner in ae.InnerExceptions)
                        Plugin.Logger.LogError(inner);
                else
                    Plugin.Logger.LogError(e);
            }

            return !task.IsFaulted;
        }

        private void OnRedemption(object sender, OnChannelPointsRewardRedeemedArgs e)
        {
            _redemptionQueue.Enqueue(new PendingRedemption(this, e));
        }

        public CustomReward GetCachedOnlineInfo(string rewardTitle)
        {
            lock (_onlineRewardsByTitle)
            {
                if (_onlineRewardsByTitle.TryGetValue(rewardTitle, out var reward))
                    return reward;
                else
                    return null;
            }
        }

        public void PauseReward(string rewardTitle, bool paused)
        {
            CustomReward reward;
            lock (_onlineRewardsByTitle)
            {
                if (!_onlineRewardsByTitle.TryGetValue(rewardTitle, out reward))
                    reward = null;
            }
            
            if(reward != null)
            {
                if(reward.IsPaused != paused)
                {
                    Plugin.Logger.LogDebug($"{(reward.IsPaused ? "Pausing" : "Unpausing")} reward: {rewardTitle}");
                    UpdateCustomRewardRequest req = new()
                    {
                        IsPaused = paused
                    };

                    Api.Helix.ChannelPoints.UpdateCustomRewardAsync(ChannelId, reward.Id, req)
                        .ContinueWith(task =>
                        {
                            if (ValidateSuccess(task))
                            {
                                foreach (var updatedReward in task.Result.Data)
                                {
                                    UpdateOnlineInfo(updatedReward, true);
                                }
                                CacheData.Save();
                            }
                        });
                }
            }
        }

        public void PatchOnlineInfo(string rewardTitle, int? cost, int? delay, bool? enabled)
        {
            CustomReward reward;
            lock (_onlineRewardsByTitle)
            {
                if (!_onlineRewardsByTitle.TryGetValue(rewardTitle, out reward))
                    reward = null;
            }

            if (reward != null)
            {
                // Only update an existing reward if the settings are different
                if (cost.HasValue && reward.Cost != cost.Value
                    || delay.HasValue && (reward.GlobalCooldownSetting.IsEnabled ? reward.GlobalCooldownSetting.GlobalCooldownSeconds : 0) != delay.Value
                    || enabled.HasValue && reward.IsEnabled != enabled.Value)
                {
                    UpdateCustomRewardRequest req = new()
                    {
                        Cost = cost,
                        IsGlobalCooldownEnabled = delay.HasValue ? delay.Value > 0 : null,
                        GlobalCooldownSeconds = delay.HasValue && delay > 0 ? delay : null,
                        IsEnabled = enabled
                    };
                    Plugin.Logger.LogDebug($"Updating reward: {rewardTitle}, cost={cost}, delay={delay}, enabled={enabled}");
                    Api.Helix.ChannelPoints.UpdateCustomRewardAsync(ChannelId, reward.Id, req)
                        .ContinueWith(task =>
                        {
                            if (ValidateSuccess(task))
                            {
                                foreach (var updatedReward in task.Result.Data)
                                    UpdateOnlineInfo(updatedReward);
                            }
                        });
                }
            }
            else
            {
                // Create a new reward
                CreateCustomRewardsRequest req = new()
                {
                    Title = rewardTitle,
                    Cost = cost.Value,
                    IsGlobalCooldownEnabled = delay > 0,
                    GlobalCooldownSeconds = delay > 0 ? delay : null,
                    IsEnabled = enabled.Value
                };
                Plugin.Logger.LogDebug($"Creating reward: {rewardTitle}, cost={cost}, delay={delay}, enabled={enabled}");
                Api.Helix.ChannelPoints.CreateCustomRewardsAsync(ChannelId, req)
                    .ContinueWith(task =>
                    {
                        if (ValidateSuccess(task))
                        {
                            foreach (var newReward in task.Result.Data)
                            {
                                UpdateOnlineInfo(newReward, true);
                            }
                            CacheData.Save();
                        }
                    });
            }
        }

        public void DeleteOnlineInfo(string rewardTitle)
        {
            CustomReward reward;
            lock (_onlineRewardsByTitle)
            {
                if (_onlineRewardsByTitle.TryGetValue(rewardTitle, out reward))
                {
                    lock (_manageableRewardIds)
                    {
                        _manageableRewardIds.Remove(reward.Id);
                    }
                }
                else
                {
                    reward = null;
                }

                _onlineRewardsByTitle.Remove(rewardTitle);
            }

            if (reward != null)
            {
                Plugin.Logger.LogDebug($"Deleting reward: {rewardTitle}");
                Api.Helix.ChannelPoints.DeleteCustomRewardAsync(ChannelId, reward.Id)
                    .LogFailure();
            }
            else
            {
                Plugin.Logger.LogDebug($"Skipped deleting non-existent reward: {rewardTitle}");
            }

            Plugin.Config?.RewardsChanged();
        }

        private void UpdateOnlineInfo(CustomReward reward, bool markManageable = false)
        {
            lock(_onlineRewardsByTitle)
            {
                _onlineRewardsByTitle[reward.Title] = reward;
            }

            if(markManageable)
            {
                lock(_manageableRewardIds)
                {
                    _manageableRewardIds.Add(reward.Id);
                }
            }

            Plugin.Config?.RewardsChanged();
        }

        public bool CanManageReward(string rewardId)
        {
            lock(_manageableRewardIds)
            {
                return _manageableRewardIds.Contains(rewardId);
            }
        }

        public void Redeem(PendingRedemption redemption)
        {
            var res = Fulfillment.None;

            if (Rewards.TryGetValue(redemption.RewardTitle, out var reward))
            {
                try
                {
                    switch(reward.Handler())
                    {
                        case RewardStatus.TryLater:
                            if (Integrations.retryFailedRewards)
                            {
                                redemption.Retries++;
                                if(redemption.Retries > Integrations.maxRetries)
                                {
                                    res = Fulfillment.Refund;
                                }
                                else
                                {
                                    Timer.Set(() => Redeem(redemption), UnityEngine.Random.Range(2f, 5f));
                                    res = Fulfillment.None;
                                }
                            }
                            else
                            {
                                res = Fulfillment.Refund;
                            }
                            break;

                        case RewardStatus.Done:
                            Integrations.ShowNotification((reward.RewardInfo.DisplayName ?? redemption.RewardTitle) + " redeemed by " + redemption.UserName);
                            res = Fulfillment.Fulfull;
                            break;

                        case RewardStatus.Cancel:
                            res = Fulfillment.Refund;
                            break;
                    }
                }
                catch(Exception e)
                {
                    Debug.Log($"Redemption failed! Reward: {redemption.RewardTitle}\nSee exception log for more info");
                    Debug.LogException(e);
                    res = Fulfillment.Refund;
                }
            }

            switch(res)
            {
                case Fulfillment.Fulfull: redemption.MarkFulfilled(); break;
                case Fulfillment.Refund: redemption.MarkCancelled(); break;
            }
        }

        private enum Fulfillment
        {
            None,
            Fulfull,
            Refund
        }

        public void Dispose()
        {
            _pubSub.Disconnect();
        }

        public delegate RewardStatus RedemptionHandler();

        public class Reward
        {
            public TwitchRewardAttribute RewardInfo { get; }
            public RedemptionHandler Handler { get; }

            public Reward(TwitchRewardAttribute attribute, MethodInfo method)
            {
                RewardInfo = attribute;
                Handler = Delegate.CreateDelegate(typeof(RedemptionHandler), method, false) as RedemptionHandler;
                if (Handler == null)
                {
                    Debug.Log("Failed to create redemption handler: " + method.Name);
                    Handler = DefaultHandler;
                }
            }

            private RewardStatus DefaultHandler()
            {
                Debug.Log($"Tried to redeem faulty reward: {RewardInfo.RewardTitle}");
                return RewardStatus.Cancel;
            }
        }

        public class PendingRedemption
        {
            public readonly string RewardTitle;
            public readonly string UserName;
            public int Retries;
            private readonly OnChannelPointsRewardRedeemedArgs _source;
            private readonly IntegrationSystem _system;

            public PendingRedemption(IntegrationSystem system, OnChannelPointsRewardRedeemedArgs args)
            {
                UserName = args.RewardRedeemed.Redemption.User.DisplayName;
                RewardTitle = args.RewardRedeemed.Redemption.Reward.Title;
                _system = system;
                _source = args;
            }

            public PendingRedemption(string title, string user)
            {
                RewardTitle = title;
                UserName = user;
            }

            public void MarkFulfilled() => MarkStatus(CustomRewardRedemptionStatus.FULFILLED);
            public void MarkCancelled() => MarkStatus(CustomRewardRedemptionStatus.CANCELED);

            private void MarkStatus(CustomRewardRedemptionStatus status)
            {
                var redemption = _source?.RewardRedeemed.Redemption;
                if (redemption == null
                    || redemption.Status != "UNFULFILLED"
                    || redemption.Reward.ShouldRedemptionsSkipRequestQueue
                    || !_system.CanManageReward(redemption.Reward.Id)) return;

                var request = new UpdateCustomRewardRedemptionStatusRequest() { Status = status };
                _system.Api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(redemption.ChannelId, redemption.Reward.Id, new List<string>() { redemption.Id }, request)
                    .LogFailure();
            }
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    sealed class TwitchRewardAttribute : Attribute
    {
        public string RewardTitle { get; private set; }
        public string DisplayName { get; set; }
        public int DefaultCost { get; set; } = 1;
        public int DefaultDelay { get; set; } = 0;

        public TwitchRewardAttribute(string rewardTitle)
        {
            RewardTitle = rewardTitle;
        }
    }

    public enum RewardStatus
    {
        TryLater,
        Cancel,
        Done
    }
}
