using System.Linq;
using BepInEx;
using Menu;
using UnityEngine;
using DevConsole;
using DevConsole.Commands;
using System;
using Random = UnityEngine.Random;
using System.Security.Permissions;
using BepInEx.Logging;
using System.IO;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace TwitchIntegration
{
    [BepInPlugin(ModID, "Twitch Integration", "3.0.0")]
    internal class Plugin : BaseUnityPlugin
    {
        public const string ModID = "slime-cubed.twitchintegration";
        public static new ManualLogSource Logger { get; private set; }
        public static new Config Config { get; private set; }
        public static SetupFile SetupFile { get; private set; }

        public IntegrationSystem System;

        private LoginPrompt _login;
        private bool _init;

        public void Awake()
        {
            Logger = base.Logger;

            On.Menu.MainMenu.ctor += MainMenu_ctor;
            NameLabel.AddHooks();

            On.RainWorld.OnModsInit += (orig, self) =>
            {
                try
                {
                    orig(self);

                    if (_init) return;
                    _init = true;

                    var mod = ModManager.InstalledMods.Find(mod => mod.id == ModID);

                    MachineConnector.SetRegisteredOI(ModID, Config = new Config(this));
                    AddCommands();
                    AddMiscHooks();

                    SetupFile = SetupFile.Load(Path.Combine(mod.path, "ti_setup.json"));
                }
                catch (Exception e)
                {
                    Logger.LogError(e);
                    Debug.LogException(e);
                }
            };
        }

        private const string enableTwitchText = "ENABLE TWITCH";
        private const string disableTwitchText = "DISABLE TWITCH";
        private void MainMenu_ctor(On.Menu.MainMenu.orig_ctor orig, MainMenu self, ProcessManager manager, bool showRegionSpecificBkg)
        {
            orig(self, manager, showRegionSpecificBkg);

            CacheData.Reload();

            float buttonWidth = MainMenu.GetButtonWidth(self.CurrLang);
            var pos = new Vector2(683f - buttonWidth / 2f, 0f);
            var size = new Vector2(buttonWidth, 30f);

            var button = new SimpleButton(self, self.pages[0], (_login == null && System == null) ? enableTwitchText : disableTwitchText, "TOGGLE_TWITCH", pos, size);
            self.AddMainMenuButton(button, () => Connect(button), 1);
        }

        private void Connect(SimpleButton button)
        {
            if (string.IsNullOrEmpty(SetupFile.ClientID) || string.IsNullOrEmpty(SetupFile.RedirectUri))
            {
                string message = "Please finish Twitch Integration setup:";
                if (string.IsNullOrEmpty(SetupFile.ClientID))
                    message += "\n\"client_id\" is missing from ti_setup.json";
                if (string.IsNullOrEmpty(SetupFile.RedirectUri))
                    message += "\n\"redirect_uri\" is missing from ti_setup.json";

                var dialog = new DialogNotify(message, button.menu.manager, null);
                button.menu.manager.ShowDialog(dialog);
            }
            else if (System != null)
            {
                System?.Dispose();
                System = null;

                MockApi.Stop();

                button.menuLabel.text = enableTwitchText;
            }
            else
            {
                bool mock = Input.GetKey(KeyCode.M);
                _login = new LoginPrompt(button.menu.manager, mock);
                button.menu.manager.ShowDialog(_login);
                button.menuLabel.text = disableTwitchText;

                _login.Success += system =>
                {
                    Logger.LogDebug($"Successfully logged into Twitch! Mock: {mock}");
                    System = system;
                    _login = null;
                    if (Config.StayLoggedIn.Value && !mock)
                    {
                        CacheData.OAuthToken = System.Api.Settings.AccessToken;
                        CacheData.Save();
                    }
                };

                _login.Failure += () =>
                {
                    Logger.LogDebug("Failed to log into Twitch!");
                    button.menuLabel.text = enableTwitchText;
                    _login = null;
                };
            }
        }

        public void Update()
        {
            System?.Update();
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
                            if (MockApi.Instance != null)
                            {
                                MockApi.Instance.TriggerRedeem(System.Rewards[rewardName]);
                            }
                            else
                            {
                                System?.Redeem(new IntegrationSystem.PendingRedemption(System.Rewards[rewardName], "Test User"));
                            }
                            break;

                        case "skip_timers":
                            Timer.FastForwardAll();
                            break;

                        case "stress_test":
                            var rewards = System?.Rewards.Values.Where(x => x.Enabled).ToArray();
                            if (rewards == null) break;
                            for (int i = 0; i < 5; i++)
                            {
                                System?.Redeem(new IntegrationSystem.PendingRedemption(rewards[Random.Range(0, rewards.Length)], "Test User"));
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

        private void AddMiscHooks()
        {
            On.OverWorld.WorldLoaded += OverWorld_WorldLoaded1;
        }

        // Prevent crash when switching regions carrying a swallowed DLL
        private void OverWorld_WorldLoaded1(On.OverWorld.orig_WorldLoaded orig, OverWorld self, bool warpUsed)
        {
            orig(self, warpUsed);

            for (int m = 0; m < self.game.Players.Count; m++)
            {
                if (self.game.Players[m].realizedCreature is Player player
                    && player.objectInStomach is AbstractCreature crit
                    && crit.creatureTemplate.AI)
                {
                    crit.abstractAI.NewWorld(self.activeWorld);
                }
            }
        }
    }
}
