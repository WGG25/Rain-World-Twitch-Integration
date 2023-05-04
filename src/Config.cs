using Menu;
using Menu.Remix;
using Menu.Remix.MixedUI;
using Menu.Remix.MixedUI.ValueTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TwitchIntegration
{
    internal class Config : OptionInterface
    {
        public readonly Configurable<bool> StayLoggedIn;
        public readonly Configurable<int> MaxRetries;
        public readonly Configurable<float> AfkTime;
        public readonly Configurable<bool> ClassicColors;
        public readonly Configurable<bool> DetachOnTeleport;
        public readonly Configurable<bool> ShowNameTags;
        private readonly Plugin _plugin;
        private readonly Dictionary<string, Configurable<bool>> _autoFulfill = new();

        // Tab 0: Control panel
        private OpSimpleButton _logOut;
        
        // Tab 1: Rewards
        private readonly List<RewardUI> _rewardList = new();
        private OpCheckBox _toggleAll;
        private OpSimpleButton _enableRewards;
        private OpSimpleButton _disableRewards;
        private OpSimpleButton _createRewards;
        private OpSimpleButton _deleteRewards;
        private OpSimpleButton _applyChanges;
        private OpSimpleButton _refresh;
        private volatile bool _rewardsDirty;

        public Config(Plugin plugin)
        {
            _plugin = plugin;

            StayLoggedIn = config.Bind("stay_logged_in", false);
            MaxRetries = config.Bind("max_retries", 5);
            AfkTime = config.Bind("afk_time", 5f, new ConfigAcceptableRange<float>(-1f, float.PositiveInfinity));
            ClassicColors = config.Bind("classic_colors", true);
            DetachOnTeleport = config.Bind("detach_on_teleport", true);
            ShowNameTags = config.Bind("show_name_tags", true);

            foreach(var pair in Integrations.Attributes)
            {
                _autoFulfill[pair.Item2.RewardTitle] = config.Bind("autofulfill_" + pair.Item2.RewardTitle.ToLowerInvariant().Replace(' ', '_'), true);
            }

            OnConfigChanged += Config_OnConfigChanged;
        }

        public bool ShouldAutoFulfill(string title)
        {
            return _autoFulfill.TryGetValue(title, out var cfg) && cfg.Value;
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

            _rewardsDirty = true;
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
            y -= itemHeight + spacing;

            // Pause redemptions when the game is paused
            Tabs[0].AddItems(
                new OpUpdown(AfkTime, new Vector2(columnX, y), 100f, 1)
                { description = "Disable reward redemptions after this many seconds when paused." },
                new OpLabel(new Vector2(columnX + 100f + spacing, y), new Vector2(columnWidth - 100f - spacing, 24f), "AFK Delay")
            );
            y -= itemHeight + spacing;

            // Add a spacer between meta and game options
            y -= itemHeight + spacing;

            // Return to the original color generation method
            // Tabs[0].AddItems(
            //     new OpCheckBox(ClassicColors, columnX, y)
            //     { description = "Use brighter, less cohesive color palettes when generating level and slugcat colors." },
            //     new OpLabel(new Vector2(columnX + 24f + spacing, y), new Vector2(columnWidth - 24f - spacing, 24f), "Classic Colors")
            // );
            // y -= itemHeight + spacing;

            // Detach Saint's tongue from terrain when teleporting
            Tabs[0].AddItems(
                new OpCheckBox(DetachOnTeleport, columnX, y)
                { description = "Detach the player and held creatures' tongues from terrain when teleporting." },
                new OpLabel(new Vector2(columnX + 24f + spacing, y), new Vector2(columnWidth - 24f - spacing, 24f), "Detach Player on Teleport")
            );
            y -= itemHeight + spacing;

            // Enable/Disable name tags on spawned creatures
            Tabs[0].AddItems(
                new OpCheckBox(ShowNameTags, columnX, y)
                { description = "Show the name of the user who summoned a creature above it." },
                new OpLabel(new Vector2(columnX + 24f + spacing, y), new Vector2(columnWidth - 24f - spacing, 24f), "Show Name Tags")
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
            const float listMargin = 14f;
            const float listSpacing = 8f;
            const float checkX = listMargin;
            const float checkSpacing = 24f + listSpacing;
            const float rewardTitleX = checkX + checkSpacing * 2f;
            const float rewardTitleWidth = 175f;
            const float fieldX = rewardTitleX + rewardTitleWidth + listSpacing;
            const float fieldWidth = 75f;
            const float fieldSpacing = fieldWidth + listSpacing;
            const float statusX = fieldX + fieldSpacing * 2f;
            const float statusWidth = 600f - listMargin - statusX;
            const int footerItemCount = 6;
            const float footerItemWidth = (600f - spacing * (footerItemCount - 1)) / footerItemCount;
            OpScrollBox sb;

            // Add header
            int rewardCount = _plugin.System?.Rewards.Count ?? 1;

            Tabs[1].AddItems(
                new OpLabel(new Vector2(0f, 600f - titleHeight), new Vector2(600f, titleHeight), "Channel Point Rewards", FLabelAlignment.Center, true),
                _toggleAll = new OpCheckBox(new(false), new Vector2(checkX, headerY))
                { description = "Select or deselect all rewards." },
                new OpLabel(new Vector2(checkX + checkSpacing, headerY), new Vector2(24f, 24f), "Auto\nComp.")
                { description = "Automatically mark reward redemptions as completed." },
                new OpLabel(new Vector2(rewardTitleX, headerY), new Vector2(rewardTitleWidth, 24f), "Title"),
                new OpLabel(new Vector2(fieldX, headerY), new Vector2(fieldWidth, 24f), "Cost")
                { description = "Configures the number of channel points this reward costs." },
                new OpLabel(new Vector2(fieldX + fieldSpacing, headerY), new Vector2(fieldWidth, 24f), "Delay")
                { description = "Configures the global cooldown of this reward in seconds." },
                new OpLabel(new Vector2(statusX, headerY), new Vector2(statusWidth, 24f), "Status"),
                sb = new OpScrollBox(new Vector2(0f, footerHeight + spacing), new Vector2(600f, headerY - footerHeight - spacing * 2f), rewardCount * (rewardHeight + rewardSpacing))
            );

            // Add rewards
            y = Mathf.Floor(rewardSpacing / 2f);

            if (_plugin.System is IntegrationSystem system)
            {
                foreach (var reward in system.Rewards.OrderByDescending(r => r.Key))
                {
                    RewardUI ui = new();
                    ui.Reward = reward.Value;

                    sb.AddItems(
                        ui.Selected = new OpCheckBox(new(false), new Vector2(checkX, y))
                        { description = "Select this reward." },
                        ui.AutoComplete = new OpCheckBox(_autoFulfill[reward.Key], new Vector2(checkX + checkSpacing, y))
                        { description = "Automatically mark redemptions of this reward as completed. Uncheck this if you want to manually accept or refund redemptions." },
                        ui.Title = new OpLabel(new Vector2(rewardTitleX, y), new Vector2(rewardTitleWidth, 24f), reward.Key),
                        ui.Cost = new OpUpdown(new Configurable<int>(reward.Value.DefaultCost, new ConfigAcceptableRange<int>(1, int.MaxValue)), new Vector2(fieldX, y), fieldWidth)
                        { description = "Cost of this reward in channel points." },
                        ui.Delay = new OpUpdown(new Configurable<int>(reward.Value.DefaultDelay, new ConfigAcceptableRange<int>(0, int.MaxValue)), new Vector2(fieldX + fieldSpacing, y), fieldWidth)
                        { description = "Global cooldown of this reward in seconds." },
                        ui.StatusLabel = new OpLabel(new Vector2(statusX, y), new Vector2(statusWidth, 24f), "Generating...", FLabelAlignment.Center)
                    );

                    _rewardList.Add(ui);

                    y += rewardHeight + rewardSpacing;
                }

                _rewardsDirty = true;
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
                _applyChanges = new OpSimpleButton(new Vector2((footerItemWidth + spacing) * 4f, footerY), new Vector2(footerItemWidth, footerHeight), "Upload Changes")
                { description = "Apply changes made to reward cost and delay." },
                _refresh = new OpSimpleButton(new Vector2((footerItemWidth + spacing) * 5f, footerY), new Vector2(footerItemWidth, footerHeight), "Refresh")
                { description = "Update rewards that were changed outside of this menu." }
            );

            _enableRewards.OnClick += ForEachSelected(ui =>
            {
                if (ui.Reward.Manageable && !ui.Reward.Enabled)
                    ui.Reward.Update(enabled: true);
            });
            _disableRewards.OnClick += ForEachSelected(ui =>
            {
                if (ui.Reward.Manageable && ui.Reward.Enabled)
                    ui.Reward.Update(enabled: false);
            });
            _createRewards.OnClick += ForEachSelected(ui =>
            {
                if (!ui.Reward.Created)
                    ui.Reward.Create(ui.Cost.GetValueIntSafe(), ui.Delay.GetValueIntSafe());
            });
            _deleteRewards.OnClick += ForEachSelected(ui =>
            {
                if (ui.Reward.Manageable && ui.Reward.Created)
                    ui.Reward.Delete();
            });
            _applyChanges.OnClick += _ =>
            {
                foreach (var ui in _rewardList)
                {
                    // PatchOnlineInfo should only send an update packet when changes have been made
                    if (ui.Reward.Manageable)
                        ui.Reward.Update(cost: ui.Cost.GetValueIntSafe(), delay: ui.Delay.GetValueIntSafe());
                }
            };
            _refresh.OnClick += _ =>
            {
                if (_plugin.System is IntegrationSystem sys)
                {
                    sys.RefreshRewards();
                }
            };
        }

        private OnSignalHandler ForEachSelected(Action<RewardUI> action)
        {
            return _ =>
            {
                foreach (var ui in _rewardList)
                {
                    if (ui.Selected.GetValueBool())
                        action(ui);
                }

                DeselectAll();
            };
        }

        private void RefreshRewardList()
        {
            if (_plugin.System is not IntegrationSystem system) return;

            foreach (var ui in _rewardList)
            {
                var reward = ui.Reward;

                ((Configurable<int>)ui.Cost.cfgEntry).Value = reward.Cost;
                ui.Cost.SetValueInt(reward.Cost);

                ((Configurable<int>)ui.Delay.cfgEntry).Value = reward.Delay;
                ui.Delay.SetValueInt(reward.Delay);

                string status;
                string longStatus;
                Color statusColor = MenuColorEffect.rgbDarkRed;

                if (system.ChannelPointsAvailable == null)
                {
                    status = "Waiting for server...";
                    longStatus = "Waiting for the server to provide its list of rewards. This should only take a second.";
                }
                else if (system.ChannelPointsAvailable == false)
                {
                    status = "Unavailable!";
                    longStatus = "Something went wrong when authenticating. Make sure that your channel supports channel points! If you are certain you qualify for channel points, try relogging.";
                }
                else if (!reward.Created)
                {
                    status = "Not created!";
                    longStatus = "This reward hasn't been created on your Twitch account. Select this and hit \"Create Rewards\" to add it.";
                }
                else if ((reward.Paused || !reward.Enabled) && !reward.Manageable)
                {
                    status = "Manually disabled!";
                    longStatus = "This reward has been created on your Twitch account manually and is disabled or paused. For automatic control, try deleting the reward manually and creating it via this menu.";
                }
                else if (!reward.Manageable)
                {
                    status = "Manual control!";
                    longStatus = "This reward has been created on your Twitch account manually, so it cannot be managed by this application. For automatic control, try deleting the reward manually and creating it via this menu.";
                }
                else if (!reward.Enabled)
                {
                    status = "Disabled.";
                    longStatus = "This reward is disabled. Select this and hit \"Enable Rewards\" to enabled it.";
                    statusColor = MenuColorEffect.rgbMediumGrey;
                }
                else
                {
                    status = "Ready.";
                    longStatus = "This reward is enabled and ready to use!";
                    statusColor = Color.green;
                }

                ui.Selected.greyedOut = !reward.Manageable;
                ui.AutoComplete.greyedOut = !reward.Manageable;
                ui.Title.color = reward.Enabled ? MenuColorEffect.rgbMediumGrey : MenuColorEffect.rgbDarkGrey;
                ui.Cost.greyedOut = !reward.Manageable;
                ui.Delay.greyedOut = !reward.Manageable;
                ui.StatusLabel.text = status;
                ui.StatusLabel.description = longStatus;
                ui.StatusLabel.color = statusColor;
            }
        }

        private void ToggleAll()
        {
            var selectable = _rewardList.Where(x => x.Reward.Manageable);

            // Cycle through:
            // None
            // All
            // Enabled
            // Disabled

            // All
            if(selectable.All(x => x.Selected.GetValueBool()))
            {
                foreach (var x in selectable)
                    x.Selected.SetValueBool(x.Reward.Enabled);
            }

            // None
            else if(!selectable.Any(x => x.Selected.GetValueBool()))
            {
                foreach (var x in selectable)
                    x.Selected.SetValueBool(true);
            }

            // Enabled
            else if(selectable.All(x => x.Selected.GetValueBool() == x.Reward.Enabled))
            {
                foreach (var x in selectable)
                    x.Selected.SetValueBool(!x.Reward.Enabled);
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
            foreach (var ui in _rewardList)
            {
                if (ui.Reward.Manageable)
                    ui.Selected.SetValueBool(false);
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
                RefreshRewardList();
                _rewardsDirty = false;
            }

            _logOut.greyedOut = CacheData.OAuthToken == null;

            if(_toggleAll.GetValueBool())
            {
                _toggleAll.SetValueBool(false);
                ToggleAll();
            }

            foreach(var ui in _rewardList)
            {
                if(ui.Selected.GetValueBool() && !ui.Reward.Manageable)
                {
                    ui.Selected.SetValueBool(false);
                }
            }

            var selectedRewards = _rewardList.Where(x => x.Selected.GetValueBool());

            _toggleAll.greyedOut = !_rewardList.Any(x => x.Reward.Manageable);
            _createRewards.greyedOut = !selectedRewards.Any(x => !x.Reward.Created);
            _deleteRewards.greyedOut = !selectedRewards.Any(x => x.Reward.Created);
            _enableRewards.greyedOut = !selectedRewards.Any(x => !x.Reward.Enabled && x.Reward.Created);
            _disableRewards.greyedOut = !selectedRewards.Any(x => x.Reward.Enabled && x.Reward.Created);
            _applyChanges.greyedOut = !_rewardList.Any(ui =>
            {
                return ui.Reward.Manageable
                    && ui.Reward.Created
                    && (ui.Cost.GetValueIntSafe() != ui.Reward.Cost
                        || ui.Delay.GetValueIntSafe() != ui.Reward.Delay);
            });
        }

        private class RewardUI
        {
            public RewardInfo Reward;
            public OpLabel Title;
            public OpCheckBox Selected;
            public OpCheckBox AutoComplete;
            public OpUpdown Cost;
            public OpUpdown Delay;
            public OpLabel StatusLabel;
        }
    }
}
