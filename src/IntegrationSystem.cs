using System;
using System.Collections.Generic;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api;
using TwitchLib.PubSub;
using System.Collections.Concurrent;
using TwitchLib.PubSub.Events;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.PubSub.Models.Responses.Messages.Redemption;
using RWCustom;
using Microsoft.Extensions.Logging;
using Debug = UnityEngine.Debug;
using BepLogLevel = BepInEx.Logging.LogLevel;
using TwitchLib.Api.Helix.Models.Search;

namespace TwitchIntegration
{
    internal class IntegrationSystem : IDisposable, ILogger<TwitchPubSub>
    {
        // Rewards
        public bool? ChannelPointsAvailable { get; private set; }
        public readonly Dictionary<string, RewardInfo> Rewards = new();

        // Api
        public readonly TwitchAPI Api;
        public readonly MockData MockApi;
        public readonly string ChannelId;
        private readonly TwitchPubSub _pubSub;
        private readonly ConcurrentQueue<PendingRedemption> _redemptionQueue = new();

        public IntegrationSystem(TwitchAPI api, string channel, MockData mockApi = null)
        {
            Api = api;
            MockApi = mockApi;
            ChannelId = channel;

            // Scan for integration methods
            foreach (var pair in Integrations.Attributes)
            {
                Rewards[pair.Item2.RewardTitle] = new RewardInfo(pair.Item2, pair.Item1, this);
            }

            if (MockApi == null)
            {
                Plugin.Logger.LogInfo("Connecting PubSub...");
                _pubSub = new(this);
                _pubSub.OnListenResponse += OnListenResponse;
                _pubSub.OnPubSubServiceConnected += SendTopics;
                _pubSub.OnChannelPointsRewardRedeemed += OnRedemption;
                _pubSub.ListenToChannelPoints(channel);
                _pubSub.Connect();
            }
            else
            {
                Plugin.Logger.LogInfo("Using mock API! PubSub skipped.");
            }

            RefreshRewards();
        }

        public void RefreshRewards()
        {
            // Clear old rewards
            foreach (var reward in Rewards)
            {
                reward.Value.UpdateOnlineInfo(null);
                reward.Value.Manageable = false;
            }

            // Request existing rewards
            Api.Helix.ChannelPoints.GetCustomRewardAsync(ChannelId).ContinueWith(task =>
            {
                if (Utils.ValidateSuccess(task))
                {
                    foreach (var reward in task.Result.Data)
                    {
                        if (Rewards.TryGetValue(reward.Title, out var info))
                        {
                            info.UpdateOnlineInfo(reward);
                        }
                    }

                    ChannelPointsAvailable = true;
                }
                else
                {
                    ChannelPointsAvailable = false;
                }
            });

            // Request owned rewards
            Api.Helix.ChannelPoints.GetCustomRewardAsync(ChannelId, onlyManageableRewards: true).ContinueWith(task =>
            {
                if (Utils.ValidateSuccess(task))
                {
                    foreach (var reward in task.Result.Data)
                    {
                        if (Rewards.TryGetValue(reward.Title, out var info))
                        {
                            info.UpdateOnlineInfo(reward);
                            info.Manageable = true;
                        }
                    }
                }
            });
        }

        private void SendTopics(object sender, EventArgs e)
        {
            ((TwitchPubSub)sender).SendTopics(Api.Settings.AccessToken);
        }

        private void OnListenResponse(object sender, OnListenResponseArgs e)
        {
            if(e.Successful)
            {
                Plugin.Logger.LogInfo("Connected to PubSub!");
            }
            else
            {
                Plugin.Logger.LogError($"Failed to connect to PubSub!\nTopic: {e.Topic}\nError: {e.Response.Error}");
            }
        }

        public void Update()
        {
            var game = Custom.rainWorld.processManager.currentMainLoop as RainWorldGame;

            while (_redemptionQueue.TryDequeue(out var redemption))
            {
                if((game == null || Plugin.Config.AfkTime.Value >= 0f && game.pauseMenu?.counter > Plugin.Config.AfkTime.Value * 40f) && !redemption.Reward.AvailableInMenu)
                    redemption.MarkCanceled();
                else
                    Redeem(redemption);
            }
        }

        private void OnRedemption(object sender, OnChannelPointsRewardRedeemedArgs e)
        {
            if (Rewards.TryGetValue(e.RewardRedeemed.Redemption.Reward.Title, out var rewardInfo))
                _redemptionQueue.Enqueue(new PendingRedemption(rewardInfo, this, e.RewardRedeemed.Redemption));
        }

        public void Redeem(PendingRedemption redemption)
        {
            var res = Fulfillment.None;

            try
            {
                var reward = redemption.Reward;
                switch(reward.Handler())
                {
                    case RewardStatus.TryLater:
                        if(redemption.Retries >= Plugin.Config.MaxRetries.Value)
                        {
                            res = Fulfillment.Refund;
                        }
                        else
                        {
                            redemption.Retries++;
                            Timer.Set(() => Redeem(redemption), UnityEngine.Random.Range(2f, 5f));
                            res = Fulfillment.None;
                        }
                        break;

                    case RewardStatus.Done:
                        Integrations.ShowNotification((reward.DisplayName ?? reward.Title) + " redeemed by " + redemption.UserName);
                        res = Fulfillment.Fulfull;
                        break;

                    case RewardStatus.Cancel:
                        res = Fulfillment.Refund;
                        break;
                }
            }
            catch(Exception e)
            {
                Plugin.Logger.LogError($"Redemption failed! Reward: {redemption.Reward.Title}\nSee exception log for more info");
                Debug.LogException(e);
                res = Fulfillment.Refund;
            }

            switch(res)
            {
                case Fulfillment.Fulfull: redemption.MarkFulfilled(); break;
                case Fulfillment.Refund: redemption.MarkCanceled(); break;
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
            _pubSub?.Disconnect();
        }

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            BepLogLevel level = logLevel switch
            {
                LogLevel.Trace or LogLevel.Debug => BepLogLevel.None,
                LogLevel.Information => BepLogLevel.Info,
                LogLevel.Warning => BepLogLevel.Warning,
                LogLevel.Error or LogLevel.Critical => BepLogLevel.Error,
                _ => BepLogLevel.None
            };

            if (level == BepLogLevel.None) return;

            Plugin.Logger.Log(level, formatter(state, exception));
        }

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        IDisposable ILogger.BeginScope<TState>(TState state) => null;

        public class PendingRedemption
        {
            public readonly RewardInfo Reward;
            public readonly string UserName;
            public int Retries;
            private readonly Redemption _redemption;
            private readonly IntegrationSystem _system;

            public PendingRedemption(RewardInfo reward, IntegrationSystem system, Redemption redemption)
            {
                Reward = reward;
                UserName = redemption.User.DisplayName;
                _system = system;
                _redemption = redemption;
            }

            public PendingRedemption(RewardInfo reward, string user)
            {
                Reward = reward;
                UserName = user;
            }

            public void MarkFulfilled() => MarkStatus(CustomRewardRedemptionStatus.FULFILLED);
            public void MarkCanceled() => MarkStatus(CustomRewardRedemptionStatus.CANCELED);

            private void MarkStatus(CustomRewardRedemptionStatus status)
            {
                if (_redemption == null
                    || _redemption.Status != "UNFULFILLED"
                    || _redemption.Reward.ShouldRedemptionsSkipRequestQueue
                    || Reward == null
                    || !Reward.AutoFulfill
                    || !Reward.Manageable) return;

                var request = new UpdateCustomRewardRedemptionStatusRequest() { Status = status };
                _system.Api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(_redemption.ChannelId, _redemption.Reward.Id, new List<string>() { _redemption.Id }, request)
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
        public bool AvailableInMenu { get; set; } = false;

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
