using Menu;
using Menu.Remix.MixedUI;
using Menu.Remix.MixedUI.ValueTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.GetCustomReward;
using TwitchLib.Api.Helix.Models.Entitlements;
using UnityEngine;

namespace TwitchIntegration
{
    internal class Config : OptionInterface
    {
        public readonly Configurable<bool> StayLoggedIn;
        public readonly Configurable<int> MaxRetries;
        public readonly Configurable<float> AfkTime;
        private readonly Plugin _plugin;

        // Tab 0: Control panel
        private OpSimpleButton _logOut;
        
        // Tab 1: Rewards
        private readonly Dictionary<string, RewardUI> _rewardsByTitle = new();
        private OpCheckBox _toggleAll;
        private OpSimpleButton _enableRewards;
        private OpSimpleButton _disableRewards;
        private OpSimpleButton _createRewards;
        private OpSimpleButton _deleteRewards;
        private OpSimpleButton _applyChanges;
        private volatile bool _rewardsDirty;

        public Config(Plugin plugin)
        {
            _plugin = plugin;

            StayLoggedIn = config.Bind("stay_logged_in", false);
            MaxRetries = config.Bind("max_retries", 5);
            AfkTime = config.Bind("afk_time", 10f);

            OnConfigChanged += Config_OnConfigChanged;
        }

        private void Config_OnConfigChanged()
        {
            if(!StayLoggedIn.Value)
            {
                if (CacheData.OAuthToken != null)
                {
                    CacheData.OAuthToken = null;
                    CacheData.Save();
                }
            }
        }

        public override void Initialize()
        {
            const float titleHeight = 35f;
            const float spacing = 6f;

            base.Initialize();

            Tabs = new OpTab[]
            {
                new OpTab(this, "Control Panel"),
                new OpTab(this, "Rewards")
            };

            // Control Panel //
            const float columnWidth = 300f;
            const float itemHeight = 30f;
            const float columnX = (int)(600f - columnWidth) / 2;

            // Title
            Tabs[0].AddItems(
                new OpLabel(new Vector2(0f, 600f - titleHeight), new Vector2(600f, titleHeight), "Twitch Integration Control Panel", FLabelAlignment.Center, true)
            );
            float y = 600f - titleHeight - itemHeight - spacing * 2f;

            // Store token on login
            Tabs[0].AddItems(
                new OpCheckBox(StayLoggedIn, columnX, y)
                { description = "Store login information so that visiting the authentication page is not required." },
                new OpLabel(new Vector2(columnX + 24f + spacing, y), new Vector2(columnWidth - 24f - spacing, 24f), "Stay Logged In")
            );
            y -= itemHeight + spacing;

            // Remove stored token immediately
            Tabs[0].AddItems(
                _logOut = new OpSimpleButton(new Vector2(columnX, y), new Vector2(columnWidth, 24f), "Log Out")
                { description = "Remove all stored login information." }
            );

            // 
            Tabs[0].AddItems(
                new OpUpdown(AfkTime, new Vector2(columnX, y), 100f, 1)
                { description = "Pause redemptions after this many seconds on the pause menu." },
                new OpLabel(new Vector2(columnX + 24f + spacing, y), new Vector2(columnWidth - 24f - spacing, 24f), "Stay Logged In")
            );
            y -= itemHeight + spacing;

            _logOut.OnClick += btn =>
            {
                if (CacheData.OAuthToken != null)
                {
                    CacheData.OAuthToken = null;
                    CacheData.Save();
                }
            };
            y -= itemHeight + spacing;

            // Rewards //
            const float headerHeight = 24f;
            const float footerHeight = 24f;
            const float headerY = 600f - titleHeight - spacing - headerHeight;
            const float footerY = 0f;
            const float rewardHeight = 24f;
            const float rewardSpacing = 11f;
            const int footerItemCount = 5;
            const float footerItemWidth = (600f - spacing * (footerItemCount - 1)) / footerItemCount;
            OpScrollBox sb;

            // Add header
            int rewardCount = _plugin.System?.Rewards.Count ?? 1;

            Tabs[1].AddItems(
                new OpLabel(new Vector2(0f, 600f - titleHeight), new Vector2(600f, titleHeight), "Channel Point Rewards", FLabelAlignment.Center, true),
                _toggleAll = new OpCheckBox(new(false), new Vector2(19f, headerY))
                { description = "Select or deselect all rewards." },
                new OpLabel(new Vector2(54f, headerY), new Vector2(125f, 24f), "Title"),
                new OpLabel(new Vector2(187f, headerY), new Vector2(125f, 24f), "Cost")
                { description = "Configures the number of channel points this reward costs." },
                new OpLabel(new Vector2(320f, headerY), new Vector2(125f, 24f), "Delay")
                { description = "Configures the global cooldown of this reward in seconds." },
                new OpLabel(new Vector2(453f, headerY), new Vector2(125f, 24f), "Status"),
                sb = new OpScrollBox(new Vector2(0f, footerHeight + spacing), new Vector2(600f, headerY - footerHeight - spacing * 2f), rewardCount * (rewardHeight + rewardSpacing))
            );

            // Add rewards
            y = Mathf.Floor(rewardSpacing / 2f);

            if (_plugin.System is IntegrationSystem system)
            {
                foreach (var reward in system.Rewards.OrderBy(r => r.Key))
                {
                    RewardUI ui = new();

                    sb.AddItems(
                        ui.Selected = new OpCheckBox(new(false), new Vector2(19, y))
                        { description = "Select this reward." },
                        ui.Title = new OpLabel(new Vector2(54f, y), new Vector2(125f, 24f), reward.Key),
                        ui.Cost = new OpUpdown(new Configurable<int>(reward.Value.RewardInfo.DefaultCost, new ConfigAcceptableRange<int>(1, int.MaxValue)), new Vector2(187f, y), 125f)
                        { description = "Cost of this reward in channel points." },
                        ui.Delay = new OpUpdown(new Configurable<int>(reward.Value.RewardInfo.DefaultDelay, new ConfigAcceptableRange<int>(0, int.MaxValue)), new Vector2(320f, y), 125f)
                        { description = "Global cooldown of this reward in seconds." },
                        ui.StatusLabel = new OpLabel(new Vector2(453f, y), new Vector2(125f, 24f), "Generating...", FLabelAlignment.Center)
                    );

                    _rewardsByTitle[reward.Key] = ui;

                    y += rewardHeight + rewardSpacing;
                }

                RefreshStatusLabels();
            }
            else
            {
                _toggleAll.greyedOut = true;
            }

            sb.ScrollToTop();

            // Add footer
            Tabs[1].AddItems(
                _enableRewards = new OpSimpleButton(new Vector2((footerItemWidth + spacing) * 0f, footerY), new Vector2(footerItemWidth, footerHeight), "Enable Selected")
                { description = "Allow viewers to redeem these rewards." },
                _disableRewards = new OpSimpleButton(new Vector2((footerItemWidth + spacing) * 1f, footerY), new Vector2(footerItemWidth, footerHeight), "Disable Selected")
                { description = "Disallow viewers from seeing or redeeming these rewards." },
                _createRewards = new OpSimpleButton(new Vector2((footerItemWidth + spacing) * 2f, footerY), new Vector2(footerItemWidth, footerHeight), "Create Selected")
                { description = "Add these rewards to your Twitch channel." },
                _deleteRewards = new OpSimpleButton(new Vector2((footerItemWidth + spacing) * 3f, footerY), new Vector2(footerItemWidth, footerHeight), "Delete Selected")
                { description = "Remove these rewards from your Twitch channel." },
                _applyChanges = new OpSimpleButton(new Vector2((footerItemWidth + spacing) * 4f, footerY), new Vector2(footerItemWidth, footerHeight), "Apply Changes")
                { description = "Apply changes made to reward cost and delay." }
            );

            _enableRewards.OnClick += ForEachSelected((title, ui) =>
            {
                if (ui.Status == RewardStatus.Disabled)
                    _plugin.System.PatchOnlineInfo(title, null, null, true);
            });
            _disableRewards.OnClick += ForEachSelected((title, ui) =>
            {
                if (ui.Status == RewardStatus.Ready)
                    _plugin.System.PatchOnlineInfo(title, null, null, false);
            });
            _createRewards.OnClick += ForEachSelected((title, ui) =>
            {
                if (ui.Status == RewardStatus.NotCreated)
                    _plugin.System.PatchOnlineInfo(title, ui.Cost.GetValueInt(), ui.Delay.GetValueInt(), true);
            });
            _deleteRewards.OnClick += ForEachSelected((title, ui) =>
            {
                if (ui.Status == RewardStatus.Ready || ui.Status == RewardStatus.Disabled)
                    _plugin.System.DeleteOnlineInfo(title);
            });
            _applyChanges.OnClick += _ =>
            {
                foreach (var pair in _rewardsByTitle)
                {
                    // PatchOnlineInfo should only send an update packet when changes have been made
                    if (pair.Value.Status == RewardStatus.Ready || pair.Value.Status == RewardStatus.Disabled)
                        _plugin.System.PatchOnlineInfo(pair.Key, pair.Value.Cost.GetValueInt(), pair.Value.Delay.GetValueInt(), null);
                }
            };
        }

        private OnSignalHandler ForEachSelected(Action<string, RewardUI> action)
        {
            return _ =>
            {
                foreach (var pair in _rewardsByTitle)
                {
                    if (pair.Value.Selected.GetValueBool())
                        action(pair.Key, pair.Value);
                }

                DeselectAll();
            };
        }

        private void RefreshStatusLabels()
        {
            if (_plugin.System is not IntegrationSystem system) return;

            foreach (var pair in _rewardsByTitle)
            {
                var onlineInfo = system.GetCachedOnlineInfo(pair.Key);

                var ui = pair.Value;
                if (onlineInfo != null)
                {
                    ui.Cost.SetValueInt(onlineInfo.Cost);
                    ui.Delay.SetValueInt(onlineInfo.GlobalCooldownSetting.IsEnabled ? onlineInfo.GlobalCooldownSetting.GlobalCooldownSeconds : 0);
                }

                bool enabled = false;
                bool selectable = false;
                bool editable = false;
                RewardStatus status;

                if (system.ChannelPointsAvailable is not bool available)
                {
                    status = RewardStatus.WaitingForServer;
                }
                else if (!available)
                {
                    status = RewardStatus.Unavailable;
                }
                else if (onlineInfo == null)
                {
                    selectable = true;
                    editable = true;
                    status = RewardStatus.NotCreated;
                }
                else if (!system.CanManageReward(onlineInfo.Id))
                {
                    enabled = onlineInfo.IsPaused || !onlineInfo.IsEnabled;
                    status = enabled ? RewardStatus.ManualDisabled : RewardStatus.ManualControl;
                }
                else
                {
                    enabled = onlineInfo.IsEnabled;
                    selectable = true;
                    editable = true;
                    status = enabled ? RewardStatus.Ready : RewardStatus.Disabled;
                }

                ui.StatusLabel.text = status switch
                {
                    RewardStatus.WaitingForServer => "Waiting for server...",
                    RewardStatus.Unavailable => "Unavailable!",
                    RewardStatus.NotCreated => "Not created!",
                    RewardStatus.ManualControl => "Manual control!",
                    RewardStatus.ManualDisabled => "Manually disabled!",
                    RewardStatus.Disabled => "Disabled.",
                    RewardStatus.Ready => "Ready.",
                    _ => "Unknown!"
                };

                ui.StatusLabel.description = status switch
                {
                    RewardStatus.WaitingForServer => "Waiting for the server to provide its list of rewards.\nThis should only take a second.",
                    RewardStatus.Unavailable => "Something went wrong when authenticating. Make sure that your channel supports channel points!\nIf you are certain you qualify for channel points, try relogging.",
                    RewardStatus.NotCreated => "This reward hasn't been created on your Twitch account. Select this and hit \"Create Rewards\" to add it.",
                    RewardStatus.ManualControl => "This reward has been created on your Twitch account manually, so it cannot be managed by this application.\nFor automatic control, try deleting the reward manually and creating it via this menu.",
                    RewardStatus.ManualDisabled => "This reward has been created on your Twitch account manually and is disabled or paused.\nFor automatic control, try deleting the reward manually and creating it via this menu.",
                    RewardStatus.Disabled => "This reward is disabled.\nSelect this and hit \"Enable Rewards\" to enabled it.",
                    RewardStatus.Ready => "This reward is enabled and ready to use!",
                    _ => "Unknown!"
                };

                ui.StatusLabel.color = status switch
                {
                    RewardStatus.WaitingForServer or RewardStatus.Disabled => MenuColorEffect.rgbMediumGrey,
                    RewardStatus.Ready => Color.green,
                    _ => MenuColorEffect.rgbDarkRed
                };

                ui.Selected.greyedOut = !selectable;
                ui.Title.color = enabled ? MenuColorEffect.rgbMediumGrey : MenuColorEffect.rgbDarkGrey;
                ui.Cost.greyedOut = !editable;
                ui.Delay.greyedOut = !editable;

                ui.Enabled = enabled;
                ui.Selectable = selectable;
                ui.Editable = editable;
                ui.Status = status;
            }
        }

        private enum RewardStatus
        {
            WaitingForServer,
            Unavailable,
            NotCreated,
            ManualControl,
            ManualDisabled,
            Ready,
            Disabled
        }

        private void ToggleAll()
        {
            var selectable = _rewardsByTitle.Values.Where(x => x.Selectable);

            // Cycle through:
            // None
            // All
            // Enabled
            // Disabled

            // All
            if(selectable.All(x => x.Selected.GetValueBool()))
            {
                foreach (var x in selectable)
                    x.Selected.SetValueBool(x.Enabled);
            }

            // None
            else if(!selectable.Any(x => x.Selected.GetValueBool()))
            {
                foreach (var x in selectable)
                    x.Selected.SetValueBool(true);
            }

            // Enabled
            else if(selectable.All(x => x.Selected.GetValueBool() == x.Enabled))
            {
                foreach (var x in selectable)
                    x.Selected.SetValueBool(!x.Enabled);
            }

            // Disabled or mixed
            else
            {
                foreach (var x in selectable)
                    x.Selected.SetValueBool(false);
            }
        }

        private void DeselectAll()
        {
            foreach (var pair in _rewardsByTitle)
            {
                if (pair.Value.Selectable)
                    pair.Value.Selected.SetValueBool(false);
            }
        }

        public void RewardsChanged()
        {
            _rewardsDirty = true;
        }

        public override void Update()
        {
            base.Update();

            if (_rewardsDirty)
            {
                RefreshStatusLabels();
                _rewardsDirty = false;
            }

            _logOut.greyedOut = CacheData.OAuthToken == null;

            if(_toggleAll.GetValueBool())
            {
                _toggleAll.SetValueBool(false);
                ToggleAll();
            }

            foreach(var pair in _rewardsByTitle)
            {
                var reward = pair.Value;
                if(reward.Selected.GetValueBool() && !reward.Selectable)
                {
                    reward.Selected.SetValueBool(false);
                }
            }

            var selectedRewards = _rewardsByTitle.Values.Where(x => x.Selected.GetValueBool());

            _toggleAll.greyedOut = !_rewardsByTitle.Values.Any(x => x.Selectable);
            _createRewards.greyedOut = !selectedRewards.Any(x => x.Status == RewardStatus.NotCreated);
            _deleteRewards.greyedOut = !selectedRewards.Any(x => x.Status == RewardStatus.Ready || x.Status == RewardStatus.Disabled);
            _enableRewards.greyedOut = !selectedRewards.Any(x => x.Status == RewardStatus.Disabled);
            _disableRewards.greyedOut = !selectedRewards.Any(x => x.Status == RewardStatus.Ready);
            _applyChanges.greyedOut = !_rewardsByTitle.Any(pair =>
            {
                return _plugin.System is IntegrationSystem system
                    && system.GetCachedOnlineInfo(pair.Key) is CustomReward info
                    && (pair.Value.Status == RewardStatus.Ready || pair.Value.Status == RewardStatus.Disabled)
                    && !string.IsNullOrEmpty(pair.Value.Cost.value)
                    && !string.IsNullOrEmpty(pair.Value.Delay.value)
                    && (pair.Value.Cost.GetValueInt() != info.Cost
                        || pair.Value.Delay.GetValueInt() != (info.GlobalCooldownSetting.IsEnabled ? info.GlobalCooldownSetting.GlobalCooldownSeconds : 0));
            });
        }

        private class RewardUI
        {
            public OpLabel Title;
            public OpCheckBox Selected;
            public OpUpdown Cost;
            public OpUpdown Delay;
            public OpLabel StatusLabel;
            public bool Enabled;
            public bool Selectable;
            public bool Editable;
            public RewardStatus Status;
        }
    }
}
