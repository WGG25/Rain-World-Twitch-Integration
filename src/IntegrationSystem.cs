using System;
using System.Collections.Generic;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api;
using System.Collections.Concurrent;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using RWCustom;
using TwitchLib.EventSub.Websockets;
using System.Threading.Tasks;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Core.SubscriptionTypes.Channel;
using Debug = UnityEngine.Debug;

namespace TwitchIntegration
{
    internal class IntegrationSystem : IDisposable
    {
        // Rewards
        public bool? ChannelPointsAvailable { get; private set; }
        public readonly Dictionary<string, RewardInfo> Rewards = new();

        // Api
        public readonly TwitchAPI Api;
        public readonly string ChannelId;
        private readonly EventSubWebsocketClient eventSub;
        private readonly ConcurrentQueue<PendingRedemption> _redemptionQueue = new();

        private bool disposed;

        public IntegrationSystem(TwitchAPI api, EventSubWebsocketClient eventSub, string channelId)
        {
            Api = api;
            ChannelId = channelId;
            this.eventSub = eventSub;

            // Scan for integration methods
            foreach (var (method, attribute) in Integrations.Attributes)
            {
                Rewards[attribute.RewardTitle] = new RewardInfo(attribute, method, this);
            }

            this.eventSub.ErrorOccurred += (_, args) => { Plugin.Logger.LogError(args.Message + "\n" + args.Exception); return Task.CompletedTask; };
            this.eventSub.WebsocketConnected += OnConnected;
            this.eventSub.WebsocketDisconnected += OnDisconnected;

            this.eventSub.ChannelPointsCustomRewardRedemptionAdd += OnRedemption;

            RefreshRewards();
        }

        public Task<bool> Connect(Uri uri = null)
        {
            return eventSub.ConnectAsync(uri);
        }

        // Subscribe to topics once websocket connects
        private async Task OnConnected(object sender, WebsocketConnectedArgs e)
        {
            await Api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                "channel.channel_points_custom_reward_redemption.add", "1",
                new() { { "broadcaster_user_id", ChannelId } },
                EventSubTransportMethod.Websocket,
                eventSub.SessionId);
        }

        // Try to reconnect when disconnected
        private async Task OnDisconnected(object sender, EventArgs e)
        {
            var rng = new Random();
            int delay = rng.Next(1000, 2000);

            while (!await eventSub.ReconnectAsync() && !disposed)
            {
                await Task.Delay(delay);
                delay = Math.Min(delay * 2, 30000);
            }
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
            Api.Helix.ChannelPoints.GetCustomRewardAsync(ChannelId).ContinueWith(async task =>
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

                    string notification;
                    try
                    {
                        string type = (await Api.Helix.Users.GetUsersAsync()).Users[0].BroadcasterType;
                        if (type == "affiliate" || type == "partner")
                            notification = "Something went wrong when loading your Channel Points rewards!\nCheck exceptionLog.txt for details.";
                        else
                            notification = "You must be a Twitch affiliate or partner to use channel points!\nMake sure you logged into the right account.";
                    }
                    catch (Exception e)
                    {
                        notification = "Something went wrong when connecting to Twitch!\nCheck exceptionLog.txt for details.";
                        Plugin.Logger.LogError(e);
                        Debug.LogException(e);
                    }
                    var dialog = new Menu.DialogNotify(notification, Custom.rainWorld.processManager, null);
                    Custom.rainWorld.processManager.ShowDialog(dialog);
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

        private Task OnRedemption(object sender, ChannelPointsCustomRewardRedemptionArgs e)
        {

            var redemption = e.Payload.Event;
            if (Rewards.TryGetValue(redemption.Reward.Title, out var rewardInfo))
                _redemptionQueue.Enqueue(new PendingRedemption(rewardInfo, this, redemption));

            return Task.CompletedTask;
        }

        public void Redeem(PendingRedemption redemption)
        {
            var res = Fulfillment.None;

            try
            {
                Integrations.RedeemUserName = redemption.UserName;
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
            Integrations.RedeemUserName = null;

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
            if (disposed) return;
            disposed = true;

            eventSub?.DisconnectAsync().ContinueWith(task =>
            {
                if (!task.Result)
                    Plugin.Logger.LogError("Failed to disconnect from EventSub!");
            });
        }

        public class PendingRedemption
        {
            public readonly RewardInfo Reward;
            public readonly string UserName;
            public int Retries;
            private readonly ChannelPointsCustomRewardRedemption _redemption;
            private readonly IntegrationSystem _system;

            public PendingRedemption(RewardInfo reward, IntegrationSystem system, ChannelPointsCustomRewardRedemption redemption)
            {
                Reward = reward;
                UserName = redemption.UserName;
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
                    || Reward == null
                    || !Reward.AutoFulfill
                    || !Reward.Manageable) return;

                var request = new UpdateCustomRewardRedemptionStatusRequest() { Status = status };
                _system.Api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(_redemption.BroadcasterUserId, _redemption.Reward.Id, new List<string>() { _redemption.Id }, request)
                    .LogFailure();
            }
        }
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    internal sealed class TwitchRewardAttribute : Attribute
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

    internal enum RewardStatus
    {
        TryLater,
        Cancel,
        Done
    }
}
