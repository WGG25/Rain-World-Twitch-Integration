using System.Linq;
using BepInEx;
using Menu;
using System.Collections.Generic;
using UnityEngine;
using TwitchLib.Api.Core.Enums;
using DevConsole;
using DevConsole.Commands;
using System;
using Random = UnityEngine.Random;
using System.Security.Permissions;
using BepInEx.Logging;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using MSLogLevel = Microsoft.Extensions.Logging.LogLevel;
using LogLevel = BepInEx.Logging.LogLevel;

#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace TwitchIntegration
{
    [BepInPlugin("slime-cubed.twitchintegration", "Twitch Integration", "2.0.0")]
    internal class Plugin : BaseUnityPlugin
    {
        public static new ManualLogSource Logger { get; private set; }
        public static new Config Config { get; private set; }
        public static MockData MockApi;
        private static BepLoggerFactory _loggerFactory;

        public IntegrationSystem System;

        private LoginPrompt _login;
        private bool _init;

        private static readonly string _clientID = "wtm2ouib4loubtj0tu2l6t69erfsgd";
        private static readonly List<AuthScopes> _authScopes = new()
        {
            AuthScopes.Helix_Channel_Read_Redemptions,
            AuthScopes.Helix_Channel_Manage_Redemptions
        };

        public void Awake()
        {
            Logger = base.Logger;
            _loggerFactory = new();
            MockApi = new(_loggerFactory.CreateLogger<MockHttpClient>());

            On.Menu.MainMenu.ctor += MainMenu_ctor;
            On.RainWorld.OnModsInit += (orig, self) =>
            {
                try
                {
                    orig(self);

                    if (_init) return;
                    _init = true;

                    MachineConnector.SetRegisteredOI("slime-cubed.twitchintegration", Config = new Config(this));
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

            CacheData.Reload();

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
                    _login = new LoginPrompt(_clientID, _authScopes, _loggerFactory, CacheData.OAuthToken);
                    button.menuLabel.text = disableText;
                }
            }, 1);
        }

        public void Update()
        {
            if (_login != null && _login.Done)
            {
                var login = _login;
                _login = null;

                System = new IntegrationSystem(login.Result.Value, login.Result.Key);

                if (Config.StayLoggedIn.Value)
                {
                    CacheData.OAuthToken = login.Result.Value.Settings.AccessToken;
                    CacheData.Save();
                }
            }

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
                            System?.Redeem(new IntegrationSystem.PendingRedemption(System.Rewards[rewardName], "Test User"));
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

        private class BepLoggerFactory : ILoggerFactory
        {
            public BepLoggerFactory()
            {
            }

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new BepLoggerWrapper(categoryName);
            }

            public void Dispose()
            {
            }
        }

        private class BepLoggerWrapper : ILogger
        {
            private readonly string _name;

            public BepLoggerWrapper(string name)
            {
                _name = name;
                Logger.LogDebug("Wrapper created: " + name);
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(MSLogLevel logLevel)
            {
                return logLevel != MSLogLevel.Trace
                    && logLevel != MSLogLevel.Debug
                    && logLevel != MSLogLevel.Information;
            }

            public void Log<TState>(MSLogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                LogLevel level = logLevel switch
                {
                    MSLogLevel.Trace or MSLogLevel.Debug or MSLogLevel.Information => LogLevel.None,
                    MSLogLevel.Warning => LogLevel.Warning,
                    MSLogLevel.Error or MSLogLevel.Critical => LogLevel.Error,
                    _ => LogLevel.None
                };

                if (level == LogLevel.None) return;
                
                Logger.Log(level, _name + ": " + formatter(state, exception));
            }
        }
    }
}
