using Menu.Remix.MixedUI;
using UnityEngine;

namespace TwitchIntegration
{
    internal class Config : OptionInterface
    {
        private readonly Plugin _plugin;
        private OpHoldButton _create;
        private OpHoldButton _delete;

        public Config(Plugin plugin)
        {
            _plugin = plugin;
        }

        public override void Initialize()
        {
            base.Initialize();

            Tabs = new OpTab[]
            {
                new OpTab(this)
            };

            _create = new OpHoldButton(new Vector2(20f, 20f), new Vector2(200f, 80f), "Create Rewards", 40);
            _delete = new OpHoldButton(new Vector2(220f, 20f), new Vector2(200f, 80f), "Delete Rewards", 40);

            _create.OnPressDone += _ => _plugin.System?.CreateRewards();
            _delete.OnPressDone += _ => _plugin.System?.RemoveRewards();

            Tabs[0].AddItems(_create, _delete);
        }

        public override void Update()
        {
            base.Update();

            _create.greyedOut = _plugin.System == null;
            _delete.greyedOut = _plugin.System == null;
        }
    }
}
