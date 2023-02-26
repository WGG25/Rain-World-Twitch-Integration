using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api;
using TwitchLib.PubSub;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using TwitchLib.PubSub.Events;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using Debug = UnityEngine.Debug;

namespace TwitchIntegration
{
    internal class IntegrationSystem : IDisposable
    {
        // Rewards
        public readonly Dictionary<string, Reward> Rewards = new();

        // Api
        private readonly TwitchAPI _api;
        private readonly string _channel;
        private readonly TwitchPubSub _pubSub;
        private readonly ConcurrentQueue<Redemption> _redemptions = new();

        public IntegrationSystem(TwitchAPI api, string channel)
        {
            _api = api;
            _channel = channel;
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
        }

        public void CreateRewards()
        {
            _ = CreateRewardsAsync();
        }

        public void RemoveRewards()
        {
            _ = RemoveRewardsAsync();
        }

        private async Task CreateRewardsAsync()
        {
            var tasks = new List<Task<CreateCustomRewardsResponse>>();
            
            var onlineRewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_channel);
            foreach (var reward in Rewards)
            {
                if (!onlineRewards.Data.Any(x => x.Title == reward.Key))
                {
                    CreateCustomRewardsRequest req = new()
                    {
                        Title = reward.Key
                    };
                    tasks.Add(_api.Helix.ChannelPoints.CreateCustomRewardsAsync(_channel, req));
                }
            }

            foreach(var task in tasks)
            {
                var res = (await task).Data[0];
                if (!CacheData.OwnedRewards.Contains(res.Id))
                    CacheData.OwnedRewards.Add(res.Id);
            }

            CacheData.Save();
        }

        private async Task RemoveRewardsAsync()
        {
            var tasks = new List<Task>();
            
            var onlineRewards = await _api.Helix.ChannelPoints.GetCustomRewardAsync(_channel);
            foreach (var reward in onlineRewards.Data)
            {
                if (CacheData.OwnedRewards.Contains(reward.Id))
                    tasks.Add(_api.Helix.ChannelPoints.DeleteCustomRewardAsync(_channel, reward.Id));
            }

            CacheData.OwnedRewards.Clear();
            CacheData.Save();

            await Task.WhenAll(tasks);
        }

        private void OnRedemption(object sender, OnChannelPointsRewardRedeemedArgs e)
        {
            _redemptions.Enqueue(new Redemption(this, e));
        }

        public void Redeem(Redemption redemption)
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
                                Timer.Set(() => Redeem(redemption), UnityEngine.Random.Range(2f, 5f));
                                res = Fulfillment.None;
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
                case Fulfillment.Refund: redemption.MarkFulfilled(); break;
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

        public class Redemption
        {
            public readonly string RewardTitle;
            public readonly string UserName;
            private readonly string _redemptionID;
            private readonly string _rewardID;
            private readonly string _channelID;
            private readonly string _status;
            private readonly IntegrationSystem _system;

            public Redemption(IntegrationSystem system, OnChannelPointsRewardRedeemedArgs args)
            {
                UserName = args.RewardRedeemed.Redemption.User.DisplayName;
                RewardTitle = args.RewardRedeemed.Redemption.Reward.Title;
                _status = args.RewardRedeemed.Redemption.Status;
                _system = system;
                _redemptionID = args.RewardRedeemed.Redemption.Id;
                _rewardID = args.RewardRedeemed.Redemption.Reward.Id;
                _channelID = args.ChannelId;
            }

            public Redemption(string title, string user)
            {
                RewardTitle = title;
                UserName = user;
            }

            public void MarkFulfilled() => MarkStatus(CustomRewardRedemptionStatus.FULFILLED);
            public void MarkCancelled() => MarkStatus(CustomRewardRedemptionStatus.CANCELED);

            private void MarkStatus(CustomRewardRedemptionStatus status)
            {
                if (_redemptionID == null || _status != "UNFULFILLED") return;

                var request = new UpdateCustomRewardRedemptionStatusRequest() { Status = status };
                _system._api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(_channelID, _rewardID, new List<string>() { _redemptionID }, request);
            }
        }

        #region Helper Methods
        static bool ParseCommand(string line, out string command, out string[] args)
        {
            var space = line.IndexOf(' ');
            if (space != -1)
            {
                command = line.Substring(0, space);
                args = Unescape(line.Substring(space + 1));
                return true;
            }
            else
            {
                command = null;
                args = null;
                return false;
            }
        }

        static string Escape(params string[] args)
        {
            return string.Join(",", args.Select(Uri.EscapeDataString).ToArray());
        }

        static string[] Unescape(string args)
        {
            return args.Split(',').Select(Uri.UnescapeDataString).ToArray();
        }
        #endregion Helper Methods
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    sealed class TwitchRewardAttribute : Attribute
    {
        public string RewardTitle { get; private set; }
        public string DisplayName { get; set; }

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
