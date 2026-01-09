# For Streamers

- Subscribe to [Twitch Integration](https://steamcommunity.com/sharedfiles/filedetails/?id=2957244056) on the workshop.
- In the Remix menu in game, enable Twitch Integration and restart the game.
- Click "ENABLE TWITCH" on the main menu to connect your account to the game.
- Back in the Remix menu, select the rewards that you want to enable and click "Create Selected".

# For Modders

## Setting Up

To make a fork of the mod, you'll need to create a new Twitch application.

- Go to the [Twitch Developer Console](https://dev.twitch.tv/console) and create a new application.
- Add `http://localhost:37506/` as an OAuth Redirect URL. Using a different port is fine.
- In `TwitchIntegration\mod\ti_setup.json`, set `"client_id"` to your app's ID and and `"redirect_uri"` to `http://localhost:37506/`.

After building, copy `TwitchIntegration\mod` to the mods folder or set up a symbolic link.

## Using the Mock API

If you aren't a Twitch affiliate, you can still simulate redeeming Channel Points rewards. This requires the Twitch CLI, which you can install with WinGet:
```
winget install Twitch.TwitchCLI
```

To start the Mock API, hold M when clicking on the "ENABLE TWITCH" button on the main menu.

Three Dev Console commands are available:
- `twitch redeem <title>` simulates the redemption of the reward with the given title.
- `twitch skip_timers` immediately ends all timers, ending effects such as inverted controls.
- `twitch stress_test` simulates the redemption of 5 random enabled rewards.

> [!NOTE]
> When using the Mock API, having 20 or more rewards created at once causes some visual bugs on the Remix menu. You can still run `twitch redeem` without the reward being created.
