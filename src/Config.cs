using Menu.Remix.MixedUI;
using UnityEngine;

namespace TwitchIntegration
{
    internal class Config : OptionInterface
    {
        public readonly Configurable<bool> StayLoggedIn;

        private readonly Plugin _plugin;
        private OpHoldButton _create;
        private OpHoldButton _delete;
        private OpHoldButton _logOut;

        public Config(Plugin plugin)
        {
            _plugin = plugin;

            StayLoggedIn = config.Bind("stay_logged_in", false);

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
            base.Initialize();

            Tabs = new OpTab[]
            {
                new OpTab(this)
            };

            // Labels
            Tabs[0].AddItems(
                new OpLabel(new(400f - 6f, 550f - 6f), new(200f, 50f), "Twitch Integration\nControl Panel", FLabelAlignment.Right, true)
            );

            // Reward control
            _create = new OpHoldButton(new Vector2(10f, 570f), new Vector2(100f, 24f), "Create Rewards", 40);
            _delete = new OpHoldButton(new Vector2(10f, 540f), new Vector2(100f, 24f), "Delete Rewards", 40);

            _create.OnPressDone += btn => { btn.Reset(); _plugin.System?.CreateRewards(); };
            _delete.OnPressDone += btn => { btn.Reset(); _plugin.System?.RemoveRewards(); };

            Tabs[0].AddItems(_create, _delete);

            // Token control
            _logOut = new OpHoldButton(new Vector2(10f, 510f), new Vector2(100f, 24f), "Log Out", 40);

            _logOut.OnPressDone += btn =>
            {
                if (CacheData.OAuthToken != null)
                {
                    CacheData.OAuthToken = null;
                    CacheData.Save();
                }

                btn.Reset();
                btn.greyedOut = true;
            };

            Tabs[0].AddItems(_logOut);

            // Config
            Tabs[0].AddItems(
                new OpCheckBox(StayLoggedIn, 10f, 480f),
                new OpLabel(40f, 480f, "Stay Logged In")
            );
        }

        public override void Update()
        {
            base.Update();

            _create.greyedOut = _plugin.System == null;
            _delete.greyedOut = _plugin.System == null;
            _logOut.greyedOut = CacheData.OAuthToken == null;
        }
    }
}
