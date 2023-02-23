using System.Linq;
using BepInEx;
using Menu;
using System.Collections.Generic;
using TwitchLib.Api;
using UnityEngine;
using TwitchLib.Api.Core.Enums;
using DevConsole;
using DevConsole.Commands;
using System;
using Random = UnityEngine.Random;
using System.Security.Permissions;
using BepInEx.Logging;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace TwitchIntegration
{
    [BepInPlugin("slime-cubed.twitchintegration", "Twitch Integration", "2.0.0")]
    internal class Plugin : BaseUnityPlugin
    {
        public static new ManualLogSource Logger { get; private set; }
        public IntegrationSystem System;

        private TwitchAPI _api;
        private LoginPrompt _login;

        private static readonly string _clientID = "wtm2ouib4loubtj0tu2l6t69erfsgd";
        private static readonly List<AuthScopes> _authScopes = new()
        {
            AuthScopes.Helix_Channel_Read_Redemptions,
            AuthScopes.Helix_Channel_Manage_Redemptions
        };

        public Plugin()
        {
            Logger = base.Logger;
        }

        public void Awake()
        {
            _api = new TwitchAPI();
            _api.Settings.ClientId = _clientID;
            _api.Settings.Scopes = _authScopes;

            On.Menu.MainMenu.ctor += MainMenu_ctor;
            On.RainWorld.OnModsInit += (orig, self) =>
            {
                try
                {
                    orig(self);

                    MachineConnector.SetRegisteredOI("slime-cubed.twitchintegration", new Config(this));
                    AddCommands();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            };
        }

        private void MainMenu_ctor(On.Menu.MainMenu.orig_ctor orig, MainMenu self, ProcessManager manager, bool showRegionSpecificBkg)
        {
            orig(self, manager, showRegionSpecificBkg);

            const string enableText = "ENABLE TWITCH";
            const string disableText = "DISABLE TWITCH";

            float buttonWidth = MainMenu.GetButtonWidth(self.CurrLang);
            var pos = new Vector2(683f - buttonWidth / 2f, 0f);
            var size = new Vector2(buttonWidth, 30f);

            var button = new SimpleButton(self, self.pages[0], (_login == null && System == null) ? enableText : disableText, "TOGGLE_TWITCH", pos, size);
            self.AddMainMenuButton(button, () =>
            {
                if (_login != null || System != null)
                {
                    System?.Dispose();
                    System = null;
                    _login?.Dispose();
                    _login = null;

                    button.menuLabel.text = enableText;
                }
                else
                {
                    var data = new CacheData();

                    _login = new LoginPrompt(_api, data.OAuthToken);
                    button.menuLabel.text = disableText;
                }
            }, 0);
        }

        public void Update()
        {
            if (_login != null && _login.Done)
            {
                var login = _login;
                _login = null;
                System = new IntegrationSystem(_api, login.Validation);

                var data = new CacheData();
                data.Save();
            }
        }

        private void AddCommands()
        {
            try
            {
                AddCommandsInternal();
            }
            catch { }
        }

        private void AddCommandsInternal()
        {
            new CommandBuilder("twitch")
                .Run(args =>
                {
                    switch (args[0])
                    {
                        case "redeem":
                            if (args.Length < 2)
                            {
                                GameConsole.WriteLine("No reward specified to redeem!");
                                break;
                            }
                            string rewardName = System?.Rewards.Keys.FirstOrDefault(s => s.Equals(args[1], StringComparison.OrdinalIgnoreCase));
                            if (rewardName == null)
                            {
                                GameConsole.WriteLine("Unknown reward title!");
                                break;
                            }
                            GameConsole.WriteLine($"Redeeming \"{rewardName}\"...");
                            System?.Redeem(new IntegrationSystem.Redemption(rewardName, "Test User"));
                            break;

                        case "skip_timers":
                            Timer.FastForwardAll();
                            break;

                        case "stress_test":
                            var rewards = System?.Rewards.Keys.ToArray();
                            if (rewards == null) break;
                            for (int i = 0; i < 5; i++)
                            {
                                System?.Redeem(new IntegrationSystem.Redemption(rewards[Random.Range(0, rewards.Length)], "Test User"));
                            }
                            break;

                        default:
                            GameConsole.WriteLine("Unknown subcommand!");
                            break;
                    }
                })
                .Help("twitch [subcommand] [arg]")
                .AutoComplete(args =>
                {
                    if (args.Length == 0) return new string[] { "redeem", "skip_timers", "stress_test" };
                    else if (args.Length == 1 && args[0] == "redeem") return System?.Rewards.Keys;
                    return null;
                })
                .Register();
        }
    }
}
