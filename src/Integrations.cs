using RWCustom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Random = UnityEngine.Random;
using ObjectType = AbstractPhysicalObject.AbstractObjectType;
using CritType = CreatureTemplate.Type;
using System.IO;
using MonoMod.RuntimeDetour;

/*
 * Suggested ideas:
 * Gravity modifiers for all creatures
 * Cooldown on each user's redemptions
 * Simulate a random input (e.g. left, right, jump) for an amount of time
 * Make all creatures hate you for an amount of time
 * Revive the player, if possible
 */

namespace TwitchIntegration
{
    public static class Integrations
    {
        // SETTINGS //
        public static bool retryFailedRewards = true;
        public static int maxRetries = 4;
        public static float minPauseTime = 10f;
        
        private static RainWorld rw;

        public static RainWorld RW => rw ?? (rw = UnityEngine.Object.FindObjectOfType<RainWorld>());
        public static RainWorldGame Game => RW.processManager.currentMainLoop as RainWorldGame;
        public static IEnumerable<Player> Players => Game.Players.Where(ply => ply.realizedObject is Player).Select(ply => (Player)ply.realizedObject);
        public static bool InGame => Game != null;

        /*
         * This is the list of all channel points rewards.
         * 
         * Whenever a reward is redeemed on your channel, this mod will look through
         * these methods. If the text inside of a TwitchReward(...) matches the name,
         * it'll call that method. If the method returns true, the redemption will be
         * accepted and marked as fulfulled. If it returns false, the redemption will
         * be cancelled and the user will be refunded.
         * 
         * Try to not let exceptions leave these methods. If one does, the redemption
         * will be automatically refunded, so it's not the end of the world.
         * 
         * Costs and rewards must be configured manually.
         * 
         */

        // Kill all players
        [TwitchReward("Kill Slugcat")]
        public static RewardStatus KillSlugcat()
        {
            if (!InGame) return RewardStatus.Cancel;

            try
            {
                bool didSomething = false;
                foreach(var ply in Players)
                {
                    if (ply.dead) continue;
                    ply.Die();
                    didSomething = true;
                }
                return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
            }
            catch
            {
                return RewardStatus.Cancel;
            }
        }

        // Give all players a spear
        [TwitchReward("Give Spear")]
        public static RewardStatus GiveSpear()
        {
            if (!InGame) return RewardStatus.Cancel;

            try
            {
                bool didSomething = false;
                foreach(var ply in Players)
                {
                    if (ply.room == null) continue;

                    var absSpear = new AbstractSpear(ply.room.world, null, ply.coord, ply.room.game.GetNewID(), false);
                    ply.room.abstractRoom.AddEntity(absSpear);
                    absSpear.RealizeInRoom();
                    var spear = (Spear)absSpear.realizedObject;
                    spear.firstChunk.HardSetPosition(ply.mainBodyChunk.pos);
                    PlaySpawnEffect(ply.room, spear.firstChunk.pos);
                    

                    didSomething = true;
                }
                return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
            }
            catch
            {
                return RewardStatus.Cancel;
            }
        }

        // Accelerates all players in random directions
        [TwitchReward("Fling Slugcat")]
        public static RewardStatus Fling()
        {
            if (!InGame) return RewardStatus.Cancel;

            try
            {
                bool didSomething = false;
                foreach(var ply in Players)
                {
                    if (ply.room == null) continue;

                    ply.AllGraspsLetGoOfThisObject(true);
                    var dir = new Vector2(Random.value * 2f - 1f, Mathf.Lerp(ply.gravity == 0f ? -1f : 0f, 1f, Random.value)).normalized;
                    foreach (var chunk in ply.bodyChunks)
                        chunk.vel += dir * Random.Range(17.5f, 30f) * (ply.gravity == 0f ? 0.5f : 1f);

                    didSomething = true;
                }
                return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
            }
            catch
            {
                return RewardStatus.Cancel;
            }
        }

        // Gives all players a random item
        [TwitchReward("Mystery Gift")]
        public static RewardStatus GiveRandomItem()
        {
            if (!InGame) return RewardStatus.Cancel;

            try
            {
                bool didSomething = false;
                foreach(var ply in Players)
                {
                    if (ply.room == null) continue;

                    var item = MakeRandomItem(ply.room.world, ply.coord);
                    ply.room.abstractRoom.AddEntity(item);
                    item.RealizeInRoom();
                    item.realizedObject.firstChunk.HardSetPosition(ply.mainBodyChunk.pos);
                    PlaySpawnEffect(ply.room, item.realizedObject.firstChunk.pos);

                    didSomething = true;
                }
                return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
            }
            catch
            {
                return RewardStatus.Cancel;
            }
        }

        // Summon a red lizard in a random room exit
        [TwitchReward("Spawn Red Lizard")]
        public static RewardStatus SpawnRedLizard()
        {
            if (!InGame) return RewardStatus.Cancel;

            var ply = Players.FirstOrDefault();
            if (ply == null || ply.room == null) return RewardStatus.TryLater;
            var room = ply.room.abstractRoom;

            if (!room.realizedRoom.CritsAllowed()) return RewardStatus.Cancel;

            // Find a random room exit leading to the player's room
            var nodes = room.nodes.Select((node, i) => new KeyValuePair<int, AbstractRoomNode>(i, node)).Where(node => node.Value.type == AbstractRoomNode.Type.Exit).ToArray();
            if (nodes.Length == 0) return RewardStatus.Cancel;

            // Spawn the lizard
            var crit = new AbstractCreature(ply.room.world, StaticWorld.GetCreatureTemplate(CritType.RedLizard), null, new WorldCoordinate(room.index, -1, -1, -1), ply.room.game.GetNewID());
            crit.Realize();

            // Move the lizard into the pipe
            crit.realizedCreature.inShortcut = true;
            room.world.game.shortcuts.CreatureEnterFromAbstractRoom(crit.realizedCreature, room, nodes[Random.Range(0, nodes.Length)].Key);
            room.AddEntity(crit);

            return RewardStatus.Done;
        }

        // Summon a dead red lizard in a random room exit
        [TwitchReward("Spawn Dead Red Lizard", DisplayName = "Spawn Red Lizard")]
        public static RewardStatus SpawnDeadRedLizard()
        {
            if (!InGame) return RewardStatus.Cancel;

            var ply = Players.FirstOrDefault();
            if (ply == null || ply.room == null) return RewardStatus.TryLater;
            var room = ply.room.abstractRoom;

            if (!room.realizedRoom.CritsAllowed()) return RewardStatus.Cancel;

            // Find a random room exit leading to the player's room
            var nodes = room.nodes.Select((node, i) => new KeyValuePair<int, AbstractRoomNode>(i, node)).Where(node => node.Value.type == AbstractRoomNode.Type.Exit).ToArray();
            if (nodes.Length == 0) return RewardStatus.Cancel;

            // Spawn the lizard (but dead)
            var crit = new AbstractCreature(ply.room.world, StaticWorld.GetCreatureTemplate(CritType.RedLizard), null, new WorldCoordinate(room.index, -1, -1, -1), ply.room.game.GetNewID());
            crit.state.alive = false;
            ((LizardState)crit.state).health = -1f;
            crit.Realize();

            // Move the lizard into the pipe
            crit.realizedCreature.inShortcut = true;
            room.world.game.shortcuts.CreatureEnterFromAbstractRoom(crit.realizedCreature, room, nodes[Random.Range(0, nodes.Length)].Key);
            room.AddEntity(crit);

            return RewardStatus.Done;
        }

        // Summon a red lizard in a random room exit
        [TwitchReward("Spawn Random Creature")]
        public static RewardStatus SpawnRandomCreature()
        {
            if (!InGame) return RewardStatus.Cancel;

            var ply = Players.FirstOrDefault();
            if (ply == null || ply.room == null) return RewardStatus.TryLater;
            var room = ply.room.abstractRoom;

            if (!room.realizedRoom.CritsAllowed()) return RewardStatus.Cancel;

            // Find a random room exit leading to the player's room
            var nodes = room.nodes.Select((node, i) => new KeyValuePair<int, AbstractRoomNode>(i, node)).Where(node => node.Value.type == AbstractRoomNode.Type.Exit).ToArray();
            if (nodes.Length == 0) return RewardStatus.Cancel;

            // Spawn the lizard
            var crit = new AbstractCreature(ply.room.world, StaticWorld.GetCreatureTemplate(RandomCreatureType()), null, new WorldCoordinate(room.index, -1, -1, -1), ply.room.game.GetNewID());
            crit.Realize();

            // Move the lizard into the pipe
            crit.realizedCreature.inShortcut = true;
            room.world.game.shortcuts.CreatureEnterFromAbstractRoom(crit.realizedCreature, room, nodes[Random.Range(0, nodes.Length)].Key);
            room.AddEntity(crit);

            return RewardStatus.Done;
        }

        // Flings all items from the player's hands
        [TwitchReward("Disarm")]
        public static RewardStatus Disarm()
        {
            if (!InGame) return RewardStatus.Cancel;

            bool didSomething = false;
            foreach(var ply in Players)
            {
                if (ply.room == null) continue;
                didSomething = true;

                foreach(var grasp in ply.grasps)
                {
                    if(grasp != null && !grasp.discontinued)
                    {
                        Vector2 vel = Custom.RNV() * 30f;
                        vel.y = Mathf.Abs(vel.y);
                        foreach (var chunk in grasp.grabbed.bodyChunks)
                            chunk.vel += vel;
                    }
                }
                ply.LoseAllGrasps();
            }

            return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
        }

        // Teleport all players to random accessible spots in their current room
        [TwitchReward("Teleport")]
        public static RewardStatus Teleport()
        {
            if (!InGame) return RewardStatus.Cancel;

            bool didSomething = false;
            foreach(var ply in Players)
            {
                if (ply.room == null) continue;
                if (ply.room.roomSettings.GetEffect(RoomSettings.RoomEffect.Type.VoidSea) != null)
                {
                    didSomething = true;
                    continue;
                }

                var dirs = new IntVector2[] {
                    new IntVector2(0, 1),
                    new IntVector2(1, 0),
                    new IntVector2(-1, 0),
                    new IntVector2(0, -1),
                };
                //Shuffle(ref dirs);
                
                // Try random spots until 2 connected open tiles are found
                bool isRaining = ply.room.roomRain?.intensity > 0f;
                var testTemplate = StaticWorld.GetCreatureTemplate(CritType.Fly);
                for(int i = 0; i < 10000; i++)
                {
                    IntVector2 feetPos = ply.room.RandomTile();

                    // Ignore solid tiles
                    if (ply.room.GetTile(feetPos).Solid)
                        continue;

                    // Ignore tiles with a fall risk
                    if (ply.room.RayTraceTilesForTerrain(feetPos.x, feetPos.y, feetPos.x, 0))
                        continue;

                    // Ignore tiles that aren't connected to a shortcut
                    if (ply.room.readyForAI && !ply.room.aimap.AnyExitReachableFromTile(feetPos, testTemplate))
                        continue;

                    // If it's raining, ignore tiles that can get wet
                    if (isRaining && ply.room.roomRain?.rainReach[feetPos.x] < feetPos.y)
                        continue;

                    bool success = false;
                    for (int j = 0; j < dirs.Length; j++)
                    {
                        IntVector2 headPos = feetPos + dirs[j];
                        if (!ply.room.GetTile(headPos).Solid)
                        {
                            // Move the player
                            Vector2 startPos = ply.bodyChunks[0].pos;
                            ply.bodyChunks[1].HardSetPosition(ply.room.MiddleOfTile(feetPos));
                            ply.bodyChunks[0].HardSetPosition(ply.room.MiddleOfTile(headPos));
                            Vector2 endPos = ply.bodyChunks[0].pos;
                            ply.graphicsModule?.Reset();

                            // Move all held objects
                            foreach (var grasp in ply.grasps)
                            {
                                if (grasp == null) continue;

                                foreach (var chunk in grasp.grabbed.bodyChunks)
                                {
                                    chunk.HardSetPosition(chunk.pos - startPos + endPos);
                                    chunk.vel = new Vector2();
                                }
                                grasp.grabbed.graphicsModule?.Reset();
                            }

                            foreach (var chunk in ply.bodyChunks)
                                chunk.vel = new Vector2();

                            ply.standing = headPos.y > feetPos.y;
                            ply.AllGraspsLetGoOfThisObject(true);
                            didSomething = true;
                            success = true;
                            break;
                        }
                    }
                    if (success) break;
                }
            }

            return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
        }

        // Explode all creatures in the player's room
        [TwitchReward("Detonate Creatures")]
        public static RewardStatus DetonateCreatures()
        {
            if (!InGame) return RewardStatus.Cancel;

            bool didSomething = false;
            foreach(var ply in Players)
            {
                if (ply.room == null) continue;
                var room = ply.room;

                didSomething = true;

                var objs = room.updateList.Where(obj => obj is Creature crit && crit.Template.type != CritType.Slugcat).ToArray();
                float volume = Mathf.Min(1f, 2f / objs.Length);
                foreach (var obj in objs)
                {
                    Vector2 pos = ((Creature)obj).DangerPos;
                    Color color = Color.yellow;

                    room.AddObject(new SootMark(room, pos, 50f, false));
                    room.AddObject(new Explosion(room, null, pos, 5, 110f, 5f, 0.9f, 60f, 0.3f, null, 0.8f, 0f, 0.7f));
                    for (int i = 0; i < 14; i++)
                    {
                        room.AddObject(new Explosion.ExplosionSmoke(pos, Custom.RNV() * 5f * Random.value, 1f));
                    }
                    room.AddObject(new Explosion.ExplosionLight(pos, 160f, 1f, 3, color));
                    room.AddObject(new ExplosionSpikes(room, pos, 9, 4f, 5f, 5f, 90f, color));
                    room.AddObject(new ShockWave(pos, 60f, 0.045f, 4));
                    for (int j = 0; j < 20; j++)
                    {
                        Vector2 a = Custom.RNV();
                        room.AddObject(new Spark(pos + a * Random.value * 40f, a * Mathf.Lerp(4f, 30f, Random.value), color, null, 4, 18));
                    }

                    room.PlaySound(SoundID.Fire_Spear_Explode, pos, volume, 0.9f + Random.value * 0.2f);
                    room.InGameNoise(new Noise.InGameNoise(pos, 8000f, (PhysicalObject)obj, 1f));
                }
            }

            return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
        }

        // Explode all creatures in the player's room, but deadly
        [TwitchReward("Nuke Creatures")]
        public static RewardStatus NukeCreatures()
        {
            if (!InGame) return RewardStatus.Cancel;

            bool didSomething = false;
            foreach(var ply in Players)
            {
                if (ply.room == null) continue;
                var room = ply.room;

                didSomething = true;

                var objs = room.updateList
                    .Where(obj => obj is Creature crit && crit.Template.type != CritType.Slugcat && !crit.grabbedBy.Any(g => g.grabber is Player)).ToArray();
                float volume = Mathf.Min(1f, 2f / objs.Length);
                foreach (var obj in objs)
                {
                    var crit = (Creature)obj;
                    bool small = crit.Template.smallCreature;

                    Vector2 pos = crit.DangerPos;
                    Color color = Color.yellow;
                    room.AddObject(new SootMark(room, pos, 80f, true));
                    room.AddObject(new Explosion(room, null, pos, 7, 250f, 6.2f, small ? 0.9f : 2f, small ? 60f : 280f, 0.25f, null, 0.7f, 160f, 1f));
                    room.AddObject(new Explosion.ExplosionLight(pos, small ? 160f : 280f, 1f, 7, color));
                    room.AddObject(new Explosion.ExplosionLight(pos, 230f, 1f, 3, new Color(1f, 1f, 1f)));
                    room.AddObject(new ExplosionSpikes(room, pos, 14, 30f, 9f, 7f, small ? 90f : 170f, color));
                    room.AddObject(new ShockWave(pos, 330f, 0.045f, 5));

                    room.PlaySound(SoundID.Bomb_Explode, pos, volume, 0.9f + Random.value * 0.2f);
                    room.InGameNoise(new Noise.InGameNoise(pos, 9000f, crit, 1f));
                }
            }

            return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
        }

        // Makes the player a random color for 60 seconds
        [TwitchReward("Randomize Slugcat Colors")]
        public static RewardStatus RandomizeSlugcatColors()
        {
            if (!InGame) return RewardStatus.Cancel;

            Color bodyColor = new Color(Random.Range(0.02f, 1f), Random.Range(0.02f, 1f), Random.Range(0.02f, 1f));
            Color eyeColor = new Color(Random.Range(0.02f, 1f), Random.Range(0.02f, 1f), Random.Range(0.02f, 1f));

            Color SetBodyColor(On.PlayerGraphics.orig_SlugcatColor orig, SlugcatStats.Name name)
            {
                return bodyColor;
            }

            void SetEyeColor(On.PlayerGraphics.orig_ApplyPalette orig, PlayerGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, RoomPalette palette)
            {
                orig(self, sLeaser, rCam, palette);
                sLeaser.sprites[9].color = eyeColor;
            }

            // Update existing player graphics
            void UpdatePlayerColors()
            {
                foreach (var cam in Game.cameras)
                {
                    foreach (var sLeaser in cam.spriteLeasers)
                    {
                        if (!sLeaser.deleteMeNextFrame && sLeaser.drawableObject is PlayerGraphics pg)
                        {
                            pg.ApplyPalette(sLeaser, cam, cam.currentPalette);
                        }
                    }
                }
            }

            // Add a hook to change player colors
            On.PlayerGraphics.SlugcatColor += SetBodyColor;
            On.PlayerGraphics.ApplyPalette += SetEyeColor;
            UpdatePlayerColors();


            // Add a timer to undo that hook
            Timer.Set(() =>
            {
                On.PlayerGraphics.SlugcatColor -= SetBodyColor;
                On.PlayerGraphics.ApplyPalette -= SetEyeColor;
                if (InGame)
                    UpdatePlayerColors();
            }, 60f);

            return RewardStatus.Done;
        }

        // Creates a DLL in the player's stomach if it is empty
        [TwitchReward("Swallow DLL")]
        public static RewardStatus SwallowDLL()
        {
            if (!InGame) return RewardStatus.Cancel;

            foreach(var ply in Players)
            {
                if(ply.objectInStomach == null)
                {
                    ply.objectInStomach = new AbstractCreature(Game.world, StaticWorld.GetCreatureTemplate(CritType.DaddyLongLegs), null, ply.coord, Game.GetNewID());
                }
            }

            return RewardStatus.Done;
        }

        // Invert all controls for 15 seconds
        [TwitchReward("Invert Controls")]
        public static RewardStatus InvertControls()
        {
            if (!InGame) return RewardStatus.Cancel;

            Player.InputPackage InvertInput(On.RWInput.orig_PlayerInput orig, int playerNumber, RainWorld rainWorld)
            {
                Player.InputPackage inputs = orig(playerNumber, rainWorld);
                inputs.x *= -1;
                inputs.y *= -1;
                inputs.analogueDir.x *= -1f;
                inputs.analogueDir.y *= -1f;
                inputs.downDiagonal = 0;
                if (inputs.analogueDir.x < -0.05f || inputs.x < 0f)
                    inputs.downDiagonal = -1;
                else if (inputs.analogueDir.x > 0.05f || inputs.x > 0f)
                    inputs.downDiagonal = 1;
                return inputs;
            }

            On.RWInput.PlayerInput += InvertInput;

            Timer.Set(() => {
                On.RWInput.PlayerInput -= InvertInput;
                ShowNotification("Invert Controls has worn off!");
            }, 15f);

            return RewardStatus.Done;
        }

        // Plays a random song
        [TwitchReward("Play Random Song")]
        public static RewardStatus PlayRandomSong()
        {
            var mp = RW.processManager.musicPlayer;
            if (mp == null) return RewardStatus.Cancel;

            mp.GameRequestsSong(new MusicEvent()
            {
                songName = GetRandomSong(),
                prio = Mathf.Max(1f, mp.song?.priority + 0.01f ?? 1f),
                maxThreatLevel = 1f,
                volume = 0.3f,
                fadeInTime = 2f,
                loop = false,
                oneSongPerCycle = false,
                stopAtDeath = false,
                stopAtGate = false,
                roomsRange = -1
            });

            return RewardStatus.Done;
        }

        // Randomizes the player's stats
        [TwitchReward("Randomize Slugcat Stats")]
        public static RewardStatus RandomizeStats()
        {
            if (!InGame) return RewardStatus.Cancel;
            Timer.FastForward("Randomize Stats");

            void UpdateStats()
            {
                Game.session.characterStats = new SlugcatStats(Game.session.characterStats.name, Game.session.characterStats.malnourished);
                foreach (var ply in Players)
                {
                    float mass = 0.7f * ply.slugcatStats.bodyWeightFac;
                    ply.bodyChunks[0].mass = mass / 2f;
                    ply.bodyChunks[1].mass = mass / 2f;
                }
            }

            // Choose new stats
            bool[] luck = new bool[4] { true, true, false, false };
            Shuffle(ref luck);
            float runspeedFac = luck[0] ? Random.Range(0.6f, 1f) : Random.Range(1.5f, 2f);
            float jumpFac = luck[1] ? Random.Range(0.75f, 1f) : Random.Range(1.1f, 2f);
            float corridorClimbSpeedFac = luck[2] ? Random.Range(0.6f, 1f) : Random.Range(1.5f, 3f);
            float poleClimbSpeedFac = luck[3] ? Random.Range(0.6f, 1f) : Random.Range(1.5f, 3f);

            void ApplyRandomStats(On.SlugcatStats.orig_ctor orig, SlugcatStats self, SlugcatStats.Name slugcatNumber, bool malnourished)
            {
                orig(self, slugcatNumber, malnourished);
                self.runspeedFac = runspeedFac;
                self.corridorClimbSpeedFac = corridorClimbSpeedFac;
                self.poleClimbSpeedFac = poleClimbSpeedFac;
            }

            void ApplyJumpBoost(On.Player.orig_Jump orig, Player self)
            {
                orig(self);
                self.jumpBoost *= Mathf.Min(jumpFac, Mathf.Sqrt(jumpFac));
            }

            // Add and remove the hook
            On.SlugcatStats.ctor += ApplyRandomStats;
            On.Player.Jump += ApplyJumpBoost;
            UpdateStats();

            Timer.Set(() =>
            {
                On.SlugcatStats.ctor -= ApplyRandomStats;
                On.Player.Jump -= ApplyJumpBoost;
                if (InGame)
                    UpdateStats();
                if (!Timer.FastForwarding)
                    ShowNotification("Randomize Slugcat Stats has worn off!");
            }, 30f, "Randomize Stats");

            return RewardStatus.Done;
        }

        // Randomizes the room's palette
        [TwitchReward("Randomize Palettes")]
        public static RewardStatus RandomizePalettes()
        {
            if (!InGame) return RewardStatus.Cancel;

            // Generate a new palette that doesn't look completely like clown vomit
            HSLColor skyCol = new HSLColor(Random.value, Random.value, Random.value).FilterBlack();
            HSLColor fogCol = skyCol.Randomize(0.1f).FilterBlack();

            HSLColor blackCol = new HSLColor(Random.value, Random.value * 0.5f, Random.value * 0.4f).FilterBlack();
            HSLColor itemCol = blackCol.Randomize(0.1f).FilterBlack();

            HSLColor waterCol = new HSLColor(Random.value, Random.value, Random.value).FilterBlack();
            HSLColor farWaterCol = waterCol.Push(Random.value * 0.2f - 0.1f, -0.5f, -0.2f).FilterBlack();
            HSLColor surfCol = waterCol.Push(Random.value * 0.2f - 0.1f, Random.value * 0.25f, Random.value * 0.5f - 0.25f);
            HSLColor farSurfCol = surfCol.Push(0f, Random.value * -0.5f, Random.value * 0.2f);
            HSLColor surfHighlightCol = surfCol.Randomize(0.2f);

            HSLColor fogAmount = new HSLColor(0f, 0f, Random.value);
            HSLColor shortcut1 = new HSLColor(Random.value, Random.value, Random.value);
            HSLColor shortcut2 = shortcut1.Randomize(0.2f);
            HSLColor shortcut3 = shortcut2.Randomize(0.2f);
            HSLColor shortcutSymbol = new HSLColor(Random.value, Random.value, Random.value);

            HSLColor darkness = new HSLColor(0f, 0f, Random.value);
            HSLColor rainPalette = new HSLColor(0f, 0f, Random.value);

            Color[,] geometry = new Color[30, 6];
            {
                HSLColor rowCol = new HSLColor(Random.value, Random.value, Random.value);
                HSLColor firstCol = rowCol;
                for (int y = 0; y < 6; y++)
                {
                    if(y == 3)
                        rowCol = firstCol.Randomize(0.5f).Push(0f, Random.value * -0.25f, Random.value * -0.25f);

                    HSLColor columnCol = rowCol;
                    for (int x = 0; x < 30; x++)
                    {
                        geometry[x, y] = columnCol.rgb;
                        columnCol = columnCol.Randomize(0.05f).Push(0f, -0.025f, 0.025f);
                    }
                    rowCol = rowCol.Randomize(0.25f);
                }
            }

            // Apply those changes to the room
            void ApplyPaletteWarp(On.RoomCamera.orig_ApplyFade orig, RoomCamera self)
            {
                orig(self);

                int w = self.paletteTexture.width;
                Color[] colors = self.paletteTexture.GetPixels();
                int topRow = 32 * 7;
                colors[topRow + 0] = skyCol.rgb;
                colors[topRow + 1] = fogCol.rgb;
                colors[topRow + 2] = blackCol.rgb;
                colors[topRow + 3] = itemCol.rgb;
                colors[topRow + 4] = waterCol.rgb;
                colors[topRow + 5] = farWaterCol.rgb;
                colors[topRow + 6] = surfCol.rgb;
                colors[topRow + 7] = farSurfCol.rgb;
                colors[topRow + 8] = surfHighlightCol.rgb;
                colors[topRow + 9] = fogAmount.rgb;
                colors[topRow + 10] = shortcut1.rgb;
                colors[topRow + 11] = shortcut2.rgb;
                colors[topRow + 12] = shortcut3.rgb;
                colors[topRow + 13] = shortcutSymbol.rgb;
                for (int y = 0; y < 6; y++)
                {
                    for (int x = 0; x < 30; x++)
                    {
                        colors[(5 - y) * w + x] = geometry[x, y];
                    }
                }
                self.paletteTexture.SetPixels(colors);
                self.paletteTexture.Apply();
                self.ApplyPalette();
            }

            void RefreshPalettes()
            {
                foreach(var rCam in Game.cameras)
                    rCam.ApplyFade();
            }

            On.RoomCamera.ApplyFade += ApplyPaletteWarp;
            RefreshPalettes();

            Timer.Set(() =>
            {
                On.RoomCamera.ApplyFade -= ApplyPaletteWarp;
                if (InGame)
                    RefreshPalettes();
            }, 40f);

            return RewardStatus.Done;
        }


        // Knockoff MoodMod
        [TwitchReward("Randomize Daylight")]
        public static RewardStatus RandomizeDaylight()
        {
            if (!InGame) return RewardStatus.Cancel;

            randomizeDaylightTime = Random.value * 0.6f; // 0 is sunrise, 0.25 is midday, 0.5 is sunset 0.75 is midnight, 1 is sunrise
            if (randomizeDaylightTime > 0.2f) randomizeDaylightTime += 0.4f;
            
            // Apply those changes to the room
            void ApplyDaylight(On.RoomCamera.orig_ApplyFade orig, RoomCamera self)
            {
                orig(self);

                int w = self.paletteTexture.width;
                Color[] colors = self.paletteTexture.GetPixels();
                float paletteDarkness = 1f - colors[7 * w + 30].r;

                Color sunsetColor = new Color(1f, 0.56f, 0f);
                Color moonColor = new Color(0.12f, 0.18f, 0.35f);
                float lightness = Mathf.Clamp(Mathf.Sin(randomizeDaylightTime * Mathf.PI * 2f), 0.1f, 1.0f);

                float sunlightAmount = Mathf.Clamp01(Mathf.Sin(randomizeDaylightTime * Mathf.PI * 2f));
                float moonlightAmount = Mathf.Clamp01(-Mathf.Sin(randomizeDaylightTime * Mathf.PI * 2f));
                float sunsetAmount = Mathf.Min(
                    randomizeDaylightTime,
                    Mathf.Abs(randomizeDaylightTime - 0.5f),
                    Mathf.Abs(randomizeDaylightTime - 1f)
                );
                sunsetAmount = Custom.LerpMap(sunsetAmount, 0f, 0.1f, 1f, 0f);

                // Update geometry colors
                for (int y = 3; y < 6; y++)
                {
                    for (int x = 0; x < 30; x++)
                    {
                        Color litColor = colors[y * w + x] * (1f + paletteDarkness);
                        Color darkColor = colors[(y - 3) * w + x] * (1f + paletteDarkness);
                        Color light = litColor - darkColor;
                        Color origLight = light;

                        // Get new sunlight or moonlight color
                        light *= sunlightAmount;
                        light = Color.Lerp(light, sunsetColor * Mathf.Max(origLight.r, origLight.g, origLight.b), sunsetAmount);
                        light += moonColor * moonlightAmount * 0.3f;

                        darkColor *= lightness;
                        darkColor.a = 1f;

                        litColor = darkColor + light;
                        litColor.a = 1f;

                        colors[y * w + x] = litColor;
                        colors[(y - 3) * w + x] = darkColor;
                    }
                }

                // Update individual colors
                for(int x = 0; x < 9; x++)
                {
                    colors[7 * w + x] *= lightness * 0.9f + 0.1f;
                    colors[7 * w + x].a = 1f;
                }

                paletteDarkness = Mathf.Clamp01(paletteDarkness / (1f + paletteDarkness) * lightness);
                colors[7 * w + 30] = new Color(1f - paletteDarkness, 1f - paletteDarkness, 1f - paletteDarkness, 1f);

                self.paletteTexture.SetPixels(colors);
                self.paletteTexture.Apply();
                self.ApplyPalette();
            }

            void RefreshPalettes()
            {
                foreach(var rCam in Game.cameras)
                    rCam.ApplyFade();
            }

            if (randomizeDaylightStacks == 0)
                On.RoomCamera.ApplyFade += ApplyDaylight;
            RefreshPalettes();
            randomizeDaylightStacks++;

            Timer.Set(() =>
            {
                randomizeDaylightStacks--;
                if (randomizeDaylightStacks == 0)
                    On.RoomCamera.ApplyFade -= ApplyDaylight;
                if (InGame)
                    RefreshPalettes();
            }, 60f);

            return RewardStatus.Done;
        }
        private static int randomizeDaylightStacks = 0;
        private static float randomizeDaylightTime;

        // Makes the player very heavy and very strong for 30 seconds
        [TwitchReward("Super Strength")]
        public static RewardStatus SuperStrength()
        {
            if (!InGame) return RewardStatus.Cancel;
            Timer.FastForward("Super Strength");

            void UpdateStats()
            {
                Game.session.characterStats = new SlugcatStats(Game.session.characterStats.name, Game.session.characterStats.malnourished);
                foreach (var ply in Players)
                {
                    float mass = 0.7f * ply.slugcatStats.bodyWeightFac;
                    ply.bodyChunks[0].mass = mass / 2f;
                    ply.bodyChunks[1].mass = mass / 2f;
                }
            }

            void ApplyWeight(On.SlugcatStats.orig_ctor orig, SlugcatStats self, SlugcatStats.Name slugcatNumber, bool malnourished)
            {
                orig(self, slugcatNumber, malnourished);
                self.bodyWeightFac = 20f;
            }

            Player.ObjectGrabability DualWieldEverything(On.Player.orig_Grabability orig, Player self, PhysicalObject obj)
            {
                Player.ObjectGrabability grabability = orig(self, obj);
                if (grabability != Player.ObjectGrabability.CantGrab)
                    grabability = Player.ObjectGrabability.OneHand;
                return grabability;
            }

            void StrongerThrows(On.Player.orig_ThrowObject orig, Player self, int grasp, bool eu)
            {
                PhysicalObject obj = self.grasps[grasp].grabbed;
                orig(self, grasp, eu);

                // Thrown objects go faster
                Vector2 baseVel = self.firstChunk.vel;
                foreach (var chunk in obj.bodyChunks)
                    chunk.vel = (chunk.vel - baseVel) * 1.5f + baseVel;

                // Thrown spears deal more damage
                if (obj is Spear spear) spear.spearDamageBonus += 0.75f;
            }

            // Add and remove the hook
            On.SlugcatStats.ctor += ApplyWeight;
            On.Player.Grabability += DualWieldEverything;
            On.Player.ThrowObject += StrongerThrows;
            UpdateStats();

            Timer.Set(() =>
            {
                On.SlugcatStats.ctor -= ApplyWeight;
                On.Player.Grabability -= DualWieldEverything;
                On.Player.ThrowObject -= StrongerThrows;
                if(InGame)
                    UpdateStats();
                if(!Timer.FastForwarding)
                    ShowNotification("Super Strength has worn off!");
            }, 30f, "Super Strength");

            return RewardStatus.Done;
        }

        // Makes the player very light
        [TwitchReward("Super Weakness")]
        public static RewardStatus SuperWeakness()
        {
            if (!InGame) return RewardStatus.Cancel;
            Timer.FastForward("Super Weakness");

            void UpdateStats()
            {
                Game.session.characterStats = new SlugcatStats(Game.session.characterStats.name, Game.session.characterStats.malnourished);
                foreach (var ply in Players)
                {
                    float mass = 0.7f * ply.slugcatStats.bodyWeightFac;
                    ply.bodyChunks[0].mass = mass / 2f;
                    ply.bodyChunks[1].mass = mass / 2f;
                }
            }

            void ApplyWeight(On.SlugcatStats.orig_ctor orig, SlugcatStats self, SlugcatStats.Name slugcatNumber, bool malnourished)
            {
                orig(self, slugcatNumber, malnourished);
                self.bodyWeightFac = 0.3f;
            }

            // Add and remove the hook
            On.SlugcatStats.ctor += ApplyWeight;
            UpdateStats();

            Timer.Set(() =>
            {
                On.SlugcatStats.ctor -= ApplyWeight;
                if (InGame)
                    UpdateStats();
                if (!Timer.FastForwarding)
                    ShowNotification("Super Weakness has worn off!");
            }, 30f, "Super Weakness");

            return RewardStatus.Done;
        }

        // Summon a scavenger in a random den
        [TwitchReward("Spawn Scavenger")]
        public static RewardStatus SpawnScavenger()
        {
            if (!InGame) return RewardStatus.Cancel;

            var ply = Players.FirstOrDefault();
            if (ply == null || ply.room == null) return RewardStatus.TryLater;
            var room = ply.room.abstractRoom;

            if (!room.realizedRoom.CritsAllowed()) return RewardStatus.Cancel;

            // Find a random room exit leading to the player's room
            var nodes = room.nodes.Select((node, i) => new KeyValuePair<int, AbstractRoomNode>(i, node)).Where(node => node.Value.type == AbstractRoomNode.Type.Exit).ToArray();
            if (nodes.Length == 0) return RewardStatus.Cancel;

            // Spawn the scav
            var crit = new AbstractCreature(ply.room.world, StaticWorld.GetCreatureTemplate(CritType.Scavenger), null, new WorldCoordinate(room.index, -1, -1, -1), ply.room.game.GetNewID());
            crit.Realize();

            // Move the scav into the pipe
            crit.realizedCreature.inShortcut = true;
            room.world.game.shortcuts.CreatureEnterFromAbstractRoom(crit.realizedCreature, room, nodes[Random.Range(0, nodes.Length)].Key);
            room.AddEntity(crit);

            return RewardStatus.Done;
        }

        // Summon a random friendly lizard in a den in your room
        [TwitchReward("Spawn Friend")]
        public static RewardStatus SpawnFriend()
        {
            if (!InGame) return RewardStatus.Cancel;

            var ply = Players.FirstOrDefault();
            if (ply == null || ply.room == null) return RewardStatus.TryLater;
            var room = ply.room.abstractRoom;

            if (!room.realizedRoom.CritsAllowed()) return RewardStatus.Cancel;

            // Find a random room exit leading to the player's room
            var nodes = room.nodes.Select((node, i) => new KeyValuePair<int, AbstractRoomNode>(i, node)).Where(node => node.Value.type == AbstractRoomNode.Type.Exit).ToArray();
            if (nodes.Length == 0) return RewardStatus.Cancel;

            // Spawn the lizard
            var crit = new AbstractCreature(ply.room.world, StaticWorld.GetCreatureTemplate(RandomLizardType()), null, new WorldCoordinate(room.index, -1, -1, -1), ply.room.game.GetNewID());
            crit.Realize();
            foreach (var absPly in Game.Players)
            {
                SocialMemory.Relationship rel = crit.state.socialMemory.GetOrInitiateRelationship(absPly.ID);
                rel.tempLike = 1f;
                rel.like = 1f;
                rel.know = 0.9f;
            }

            // Move the lizard into the pipe
            crit.realizedCreature.inShortcut = true;
            room.world.game.shortcuts.CreatureEnterFromAbstractRoom(crit.realizedCreature, room, nodes[Random.Range(0, nodes.Length)].Key);
            room.AddEntity(crit);

            return RewardStatus.Done;
        }

        // Displays a random loading screen tip taken from one of a handful of other games
        [TwitchReward("Give Advice")]
        public static RewardStatus GiveAdvice()
        {
            if (!InGame) return RewardStatus.Cancel;

            foreach (var cam in Game.cameras)
            {
                if(cam?.hud?.textPrompt is HUD.TextPrompt tp)
                {
                    tp.AddMessage(GetAdvice(), 10, 160, true, false);
                }
            }

            return RewardStatus.Done;
        }

        // Forces an explosive into the player's hands
        [TwitchReward("Give Explosive")]
        public static RewardStatus GiveExplosive()
        {
            if (!InGame) return RewardStatus.Cancel;

            bool didSomething = false;

            foreach(var ply in Players)
            {
                if (ply.room == null) continue;
                if (ply.FreeHand() == -1)
                    ply.ReleaseGrasp(0);

                AbstractPhysicalObject explosive;
                if (Random.value < 0.7f)
                    explosive = new AbstractPhysicalObject(ply.room.world, ObjectType.ScavengerBomb, null, ply.coord, ply.room.game.GetNewID());
                else
                    explosive = new AbstractSpear(ply.room.world, null, ply.coord, ply.room.game.GetNewID(), true);

                ply.room.abstractRoom.AddEntity(explosive);
                explosive.RealizeInRoom();
                explosive.realizedObject.firstChunk.HardSetPosition(ply.mainBodyChunk.pos);
                PlaySpawnEffect(ply.room, explosive.realizedObject.firstChunk.pos);

                ply.SlugcatGrab(explosive.realizedObject, ply.FreeHand());
                didSomething = true;
            }

            return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
        }

        // Gives the player one of a set of random status effects for a while
        [TwitchReward("Give Status Effect")]
        public static RewardStatus GiveRandomStatusEffect()
        {
            if (!InGame) return RewardStatus.Cancel;

            var effects = ((StatusEffect[])Enum.GetValues(typeof(StatusEffect))).Where(obj => !activeStatusEffects.Contains(obj)).ToArray();
            if (effects.Length == 0) return RewardStatus.Cancel;

            var effect = effects[Random.Range(0, effects.Length)];
            return GiveStatusEffect(effect);
        }

        [TwitchReward("Make Slugcat Light")]
        public static RewardStatus LowGravityStatusEffect() => GiveStatusEffect(StatusEffect.Light);

        [TwitchReward("Make Slugcat Heavy")]
        public static RewardStatus HighGravityStatusEffect() => GiveStatusEffect(StatusEffect.Heavy);

        // Gives the player the mushrom effect
        [TwitchReward("Drugs")]
        public static RewardStatus GiveShroomEffect() => GiveStatusEffect(StatusEffect.Mushroomed);

        private static RewardStatus GiveStatusEffect(StatusEffect effect)
        {
            if (activeStatusEffects.Contains(effect)) return RewardStatus.Cancel;
            activeStatusEffects.Add(effect);

            // Bounce
            void ApplyBouncy(On.Player.orig_Update orig, Player self, bool eu)
            {
                orig(self, eu);
                if (self.canJump > 0)
                    self.wantToJump = 5;
            }

            // Duped
            void ApplyDuped(On.Player.orig_Update orig, Player self, bool eu)
            {
                for (int i = 0; i < 2; i++)
                    orig(self, eu);
            }

            // Wash off some status effects in water
            void WashOffHeavy(On.Player.orig_Update orig, Player self, bool eu)
            {
                if (self.Submersion > 0.5f)
                {
                    ShowNotification("Heavy Status Effect has washed off!");
                    Timer.FastForward("Heavy Status");
                }
                orig(self, eu);
            }
            void WashOffLight(On.Player.orig_Update orig, Player self, bool eu)
            {
                if (self.Submersion > 0.5f)
                {
                    ShowNotification("Light Status Effect has washed off!");
                    Timer.FastForward("Light Status");
                }
                orig(self, eu);
            }

            bool didSomething = false;
            RainWorldGame game = Game;
            switch (effect)
            {
                case StatusEffect.Mushroomed:
                    foreach (var ply in Players)
                    {
                        ply.mushroomCounter = Math.Max(280, ply.mushroomCounter);
                        didSomething = true;
                    }
                    activeStatusEffects.Remove(effect);
                    break;

                case StatusEffect.Duped:
                    didSomething = true;
                    On.Player.Update += ApplyDuped;
                    Timer.Set(() =>
                    {
                        On.Player.Update -= ApplyDuped;
                        activeStatusEffects.Remove(effect);
                        ShowNotification("Duped Status Effect has worn off!");
                    }, 30f);
                    break;

                case StatusEffect.Light:
                    foreach (var ply in Players)
                    {
                        ply.g /= 2f;
                        Timer.Set(() => ply.g *= 2f, 30f, "Light Status");
                        didSomething = true;
                    }
                    if (didSomething)
                    {
                        On.Player.Update += WashOffLight;
                        Timer.Set(() =>
                        {
                            On.Player.Update -= WashOffLight;
                            activeStatusEffects.Remove(effect);
                            if (!Timer.FastForwarding)
                                ShowNotification("Light Status Effect has worn off!");
                        }, 30f, "Light Status");
                    }
                    break;

                case StatusEffect.Heavy:
                    foreach (var ply in Players)
                    {
                        ply.g *= 1.65f;
                        Timer.Set(() => ply.g /= 1.65f, 15f, "Heavy Status");
                        didSomething = true;
                    }
                    if (didSomething)
                    {
                        On.Player.Update += WashOffHeavy;
                        Timer.Set(() =>
                        {
                            On.Player.Update -= WashOffHeavy;
                            activeStatusEffects.Remove(effect);
                            if (!Timer.FastForwarding)
                                ShowNotification("Heavy Status Effect has worn off!");
                        }, 15f, "Heavy Status");
                    }
                    break;

                case StatusEffect.Bouncy:
                    On.Player.Update += ApplyBouncy;
                    Timer.Set(() =>
                    {
                        On.Player.Update -= ApplyBouncy;
                        activeStatusEffects.Remove(effect);
                        ShowNotification("Bouncy Status Effect has worn off!");
                    }, 25f);
                    didSomething = true;
                    break;
            }

            return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
        }
        private enum StatusEffect
        {
            Mushroomed,
            Duped,
            Light,
            Heavy,
            Bouncy
        }
        private static readonly List<StatusEffect> activeStatusEffects = new List<StatusEffect>();
        private class GlowStatusLight : LightSource
        {
            public GlowStatusLight(Vector2 initPos, bool environmentalLight, Color color, UpdatableAndDeletable tiedToObject) : base(initPos, environmentalLight, color, tiedToObject) {}
        }


        [TwitchReward("Make it Rain")]
        public static RewardStatus MakeItRain()
        {
            if (!InGame || makingItRain) return RewardStatus.Cancel;
            makingItRain = true;
            makingItRainTimer = 0f;

            Hook rainApproachingHook = null;

            void StopRain()
            {
                On.GlobalRain.Update -= DoRain;
                On.Creature.Die -= InvulnDeer;
                On.RainWorldGame.ShutDownProcess -= StopOnShutDown;
                rainApproachingHook.Dispose();
                makingItRain = false;

                if (Game != null)
                {
                    foreach (var roomRain in Game.world.activeRooms.Select(room => room.roomRain))
                    {
                        if (roomRain.intensity < 0.05f)
                            roomRain.intensity = 0f;
                    }
                }
            }

            const float buildup = 5f;
            const float duration = 15f;
            void DoRain(On.GlobalRain.orig_Update orig, GlobalRain self)
            {
                orig(self);

                makingItRainTimer += 1f / 40f;
                float targetIntensity;
                if (makingItRainTimer < buildup)
                {
                    targetIntensity = Custom.LerpMap(makingItRainTimer, 0f, buildup, 0f, 0.2f);
                }
                else
                {
                    targetIntensity = Mathf.Max(
                        Custom.LerpMap(makingItRainTimer, buildup, buildup + duration, 0.2f, 0f),
                        Mathf.Min(0.5f - 0.5f * Mathf.Cos(Mathf.InverseLerp(buildup, buildup + duration, makingItRainTimer) * Mathf.PI * 2f), 0.75f)
                    );
                }
                self.Intensity = Mathf.Max(self.Intensity, targetIntensity);

                if (makingItRainTimer > buildup + duration)
                    StopRain();
            }

            void InvulnDeer(On.Creature.orig_Die orig, Creature self)
            {
                if (self.Template.type != CritType.Deer)
                    orig(self);
            }

            void StopOnShutDown(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame self)
            {
                orig(self);
                StopRain();
            }

            float SimulateRainApproaching(Func<RainCycle, float> orig, RainCycle self)
            {
                float targetIntensity;
                float endBuildup = buildup + duration * 0.25f;
                float startCooldown = buildup + duration * 0.75f;
                if (makingItRainTimer < endBuildup)
                {
                    targetIntensity = Mathf.InverseLerp(0f, endBuildup, makingItRainTimer);
                }
                else
                {
                    targetIntensity = Mathf.InverseLerp(buildup + duration, startCooldown, makingItRainTimer);
                }

                return Math.Min(
                    orig(self),
                    1f - targetIntensity
                );
            }

            On.GlobalRain.Update += DoRain;
            On.Creature.Die += InvulnDeer;
            On.RainWorldGame.ShutDownProcess += StopOnShutDown;
            rainApproachingHook = new Hook(
                typeof(RainCycle).GetProperty(nameof(RainCycle.RainApproaching)).GetGetMethod(),
                (Func<Func<RainCycle, float>, RainCycle, float>)SimulateRainApproaching
            );

            return RewardStatus.Done;
        }
        private static bool makingItRain;
        private static float makingItRainTimer;

        [TwitchReward("Toggle Glow")]
        public static RewardStatus ToggleGlow()
        {
            if (!InGame) return RewardStatus.Cancel;

            var save = (Game.session as StoryGameSession)?.saveState;
            if (save == null) return RewardStatus.Cancel;

            save.theGlow = !save.theGlow;

            foreach (var ply in Players)
            {
                ply.glowing = save.theGlow || Game.setupValues.playerGlowing;
                if (ply.graphicsModule is PlayerGraphics pg && !ply.glowing)
                    pg.lightSource?.Destroy();
            }

            return RewardStatus.Done;
        }

        // Helper methods can also go here
        // These won't do anything on their own unless you apply the TwitchReward attribute
        #region Helpers

        static void PlaySpawnEffect(Room room, Vector2 pos, float rad = 100f)
        {
            room.AddObject(new Explosion.ExplosionLight(pos, rad, 1f, 4, new Color(1f, 0.8f, 1f)));
        }

        public static void ShowNotification(string text)
        {
            if (!InGame) return;

            var ply = Players.FirstOrDefault();
            var cam = Game.cameras.FirstOrDefault();
            if(ply != null && ply.room != null)
            {
                ply.room.AddObject(new RedemptionNotification(ply.firstChunk.pos + new Vector2(0f, 40f), text));
            }
            else if(cam != null && cam.room != null)
            {
                cam.room.AddObject(new RedemptionNotification(cam.pos + cam.sSize / 2f, text));
            }
        }

        static void Shuffle<T>(ref T[] array)
        {
            array = array.OrderBy(k => Random.value).ToArray();
        }

        static readonly Func<World, WorldCoordinate, EntityID, AbstractPhysicalObject>[] itemFactories = new Func<World, WorldCoordinate, EntityID, AbstractPhysicalObject>[]
        {
            (world, pos, id) => new AbstractSpear(world, null, pos, id, false),
            (world, pos, id) => new AbstractSpear(world, null, pos, id, true),
            (world, pos, id) => new BubbleGrass.AbstractBubbleGrass(world, null, pos, id, 1f, -1, -1, null),
            (world, pos, id) => new DataPearl.AbstractDataPearl(world, ObjectType.DataPearl, null, pos, id, -1, -1, null, DataPearl.AbstractDataPearl.DataPearlType.Misc),
            (world, pos, id) => new OverseerCarcass.AbstractOverseerCarcass(world, null, pos, id, new Color(Random.value, Random.value, Random.value), Random.Range(0, 3)),
            (world, pos, id) => new WaterNut.AbstractWaterNut(world, null, pos, id, -1, -1, null, false),
            (world, pos, id) => new SporePlant.AbstractSporePlant(world, null, pos, id, -1, -1, null, false, true),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.KarmaFlower, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.DangleFruit, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.FlyLure, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.Mushroom, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.SlimeMold, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.FirecrackerPlant, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.FlareBomb, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.NeedleEgg, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.JellyFish, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, ObjectType.PuffBall, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractPhysicalObject(world, ObjectType.Rock, null, pos, id),
            (world, pos, id) => new AbstractPhysicalObject(world, ObjectType.ScavengerBomb, null, pos, id),
            (world, pos, id) => new AbstractPhysicalObject(world, ObjectType.Lantern, null, pos, id),
        };
        static AbstractPhysicalObject MakeRandomItem(World world, WorldCoordinate pos)
        {
            if (Random.Range(0, 100) == 0)
            {
                var rock = new AbstractPhysicalObject(world, ObjectType.Rock, null, pos, world.game.GetNewID());
                PensiveRock.Mark(rock);
                return rock;
            }
            else
            {
                var factory = itemFactories[Random.Range(0, itemFactories.Length)];
                return factory(world, pos, world.game.GetNewID());
            }
        }

        static readonly string[] songBlacklist = new string[]
        {
            "TitleRollRain.mp3"
        };
        static readonly string[] songExtensionWhitelist = new string[]
        {
            ".mp3", ".ogg"
        };
        static string GetRandomSong()
        {
            DirectoryInfo directoryInfo = new DirectoryInfo("./Assets/Futile/Resources/Music/Songs");
            FileInfo[] files = directoryInfo.GetFiles();
            var songNames = files.Select(f => f.Name).Where(f => songExtensionWhitelist.Any(f.EndsWith) && !songBlacklist.Contains(f)).ToArray();
            var songName = songNames[Random.Range(0, songNames.Length)];
            return songName.Substring(0, songName.Length - 4);
        }

        static readonly Weighted<CritType>[] critTypes = new Weighted<CritType>[]
        {
            new Weighted<CritType>(0.15f, CritType.TempleGuard),
            new Weighted<CritType>(0.25f, CritType.RedLizard),
            new Weighted<CritType>(1f, CritType.YellowLizard),
            new Weighted<CritType>(1f, CritType.GreenLizard),
            new Weighted<CritType>(1f, CritType.BlueLizard),
            new Weighted<CritType>(0.5f, CritType.CyanLizard),
            new Weighted<CritType>(1f, CritType.BlackLizard),
            new Weighted<CritType>(1f, CritType.WhiteLizard),
            new Weighted<CritType>(1f, CritType.Salamander),
            new Weighted<CritType>(1f, CritType.Vulture),
            new Weighted<CritType>(0.5f, CritType.KingVulture),
            new Weighted<CritType>(1f, CritType.BigNeedleWorm),
            new Weighted<CritType>(1f, CritType.SmallNeedleWorm),
            new Weighted<CritType>(0.25f, CritType.RedCentipede),
            new Weighted<CritType>(1f, CritType.Centipede),
            new Weighted<CritType>(1f, CritType.SmallCentipede),
            new Weighted<CritType>(1f, CritType.Centiwing),
            new Weighted<CritType>(1f, CritType.Scavenger),
            new Weighted<CritType>(1f, CritType.TubeWorm),
            new Weighted<CritType>(1f, CritType.VultureGrub),
            new Weighted<CritType>(1f, CritType.Hazer),
            new Weighted<CritType>(1f, CritType.Spider),
            new Weighted<CritType>(1f, CritType.SpitterSpider),
            new Weighted<CritType>(1f, CritType.BigSpider),
            new Weighted<CritType>(0.4f, CritType.BrotherLongLegs),
            new Weighted<CritType>(0.25f, CritType.DaddyLongLegs),
            new Weighted<CritType>(1f, CritType.Snail),
            new Weighted<CritType>(1f, CritType.JetFish),
            new Weighted<CritType>(1f, CritType.LanternMouse),
            new Weighted<CritType>(0.75f, CritType.DropBug),
            new Weighted<CritType>(1f, CritType.EggBug),
        };
        static CritType RandomCreatureType()
        {
            return RandomWeighted(critTypes);
        }

        static CritType RandomLizardType()
        {
            return RandomWeighted(critTypes.Where(type => StaticWorld.GetCreatureTemplate(type.value).TopAncestor().type == CritType.LizardTemplate));
        }

        static HSLColor Randomize(this HSLColor col, float dist)
        {
            Vector3 offset = Random.onUnitSphere * dist;
            return col.Push(offset.x, offset.y, offset.z);
        }

        static HSLColor FilterBlack(this HSLColor col)
        {
            return new HSLColor(col.hue, col.saturation, Mathf.Max(col.lightness, 0.02f));
        }

        static HSLColor Push(this HSLColor col, float hue, float saturation, float lightness)
        {
            col.hue = ((col.hue + hue) % 1f + 1f) % 1f;
            col.lightness = Mathf.Clamp01(col.lightness + saturation);
            col.saturation = Mathf.Clamp01(col.saturation + lightness);
            return col;
        }

        private struct Weighted<T>
        {
            public float weight;
            public T value;

            public Weighted(float weight, T value)
            {
                this.weight = weight;
                this.value = value;
            }
        }

        private static T RandomWeighted<T>(IEnumerable<Weighted<T>> source)
        {
            float seekTo = source.Aggregate(0f, (sum, val) => sum + val.weight) * Random.value;
            Weighted<T> res = default;
            foreach (var val in source)
            {
                res = val;
                seekTo -= val.weight;
                if (seekTo <= 0) break;
            }
            return res.value;
        }

        private static bool CritsAllowed(this Room room)
        {
            if (room.abstractRoom.name == "SS_AI") return false;
            if (room.abstractRoom.exits <= 1) return false;
            return true;
        }

        private static readonly string[] adviceList = new string[]
        {
            "Remember to use flasks (1-5 on the keyboard).",
            "You can remove a Skill Gem from an item by right clicking on it.",
            "Put a # before your chat message to talk globally.",
            "Instances reset after being empty for 15 minutes.",
            "Drop an item in the chat box to link it to other players.",
            "Players in the same party will join the same instances.",
            "Right click on a player or message them in chat to invite them to your party.",
            "Support gems need to be placed in linked sockets to affect another gem.",
            "Hold Shift to attack from your current location.",
            "You can access your stash in town.",
            "Waypoints can be used to travel quickly between some areas.",
            "Flasks on your belt refill as you kill enemies.",
            "Remember to allocate your passive skill points on the passives screen.",
            "To reach the next difficulty level, find the exit at the end of the current difficulty.",
            "To use a scroll or orb, right click it then left click the target item.",
            "Hold Shift and click a stack of items to unstack them.",
            "Never tell anyone your password.",
            "Some items need to be identified with a Scroll of Wisdom before they can be used.",
            "Cruel, Ruthless and Merciless difficulties incur an experience penalty on death.",
            "Skill Gems gain experience as you do while they are equipped.",
            "Bosses and rare monsters are more likely to drop powerful equipment.",
            "If you cannot use a gem, placing it in an item will stop you from using the item.",
            "Partying with other players makes enemies harder and rewards better.",
            "Don't let enemies surround you.",
            "You can change your key bindings in the Options menu.",
            "You can move the minimap around using the arrow keys.",
            "Your stash can be used to move items between your characters.",
            "Being polite will get you invited back to play with people again!",
            "There will be bugs.",
            "You can only remove or place Flasks in your belt while your inventory is open.",
            "Some chests have a better chance of dropping items than others.",
            "Monsters that deal cold damage can freeze you.",
            "Lightning damage can shock you, increasing damage taken.",
            "Fire damage can ignite targets, causing them to burn for extra damage.",
            "Some containers can be opened by attacking them.",
            "Monsters with the very rare \"Wealth\" mod drop a huge number of items.",
            "Activating a new Waypoint is a way of saving your exploration through Wraeclast.",
            "At vendors, you can trade an Iron Ring and a Skill Gem for a resistance ring.",
            "Competitive BSing is fine. Hate speech? Not so much.",
            "Life happens. We get it. But for players who keep leaving games, LeaverBuster will issue a penalty.",
            "Did someone make some great calls? Honor them after the game.",
            "Did a teammate help make this game great? Honor them after the game.",
            "Don't let another player's frustration control your next move.",
            "Teammate kept a cool head after a bad start? Honor them with GG <3",
            "Everyone misses a skill shot occasionally, even you.",
            "Mistakes are opportunities, you know. -Reignover",
            "Is someone flaming in game? Mute unhelpful player chat or pings through the TAB menu.",
            "Even Jinx keeps cool and focuses on blowing up one thing: the enemy Nexus.",
            "Games can get heated, but racial slurs have no place in League.",
            "In-game mistakes can be new opportunities. Rethink your strategy.",
            "Everyone loves a killer comeback story. Don't give up!",
            "You can't play a team game without a team -- don't be a leaver!",
            "Use smart pings to alert your teammates to threats. You'll win more games.",
            "Smart pings keep your hands where you need 'em!",
            "Did a teammate keep their cool, despite the odds? Honor them at end of game.",
            "I can't always land skill shots. I'm only human. - GorillA",
            "If a player stands out, honor them after the game!",
            "Players who work with their team unlock cool privileges, like loot!",
            "Keep focused, keep cool: you'll win more.",
            "Above Honor Level 3? Get honored this game and get a loading-screen flair next game.",
            "No team is perfect, but you're still a team. Get out there and fight to win!",
            "Support your teammates, respect the game, and level up in Honor.",
            "Staying cool under pressure takes practice. Take a deep breath if you need one.",
            "Fight with honor.",
            "You can't control other's tempers, but you can keep a cool head.",
            "When a teammate stays focused despite the odds, honor them for staying cool!",
            "Keep the game awesome. Report purposefully unhelpful players.",
            "Use pings to keep your teammates informed or make suggestions.",
            "Back-to-back games? Recharge with a few toe touches.",
            "It sounds dumb, but a deep breath can help prevent tilt.",
            "If someone makes you uncomfortable in game, report them.",
            "Be un-tiltable!",
            "Don't be the one to tilt your teammates and risk losing the game.",
            "If a teammate pulls the game back from the brink, honor them after the game!",
            "It's not easy to keep calm sometimes, but if you do, you're going to win more games",
            "Part of being tilt-proof means getting good at expecting the unexpected!",
            "Upgrade your cannons to improve your defense against intruders",
            "Taking a break from Clash of Clans? Buy a Shield to protect your trophies!",
            "Improve your army! Build the Laboratory and upgrade your troops",
            "Out of gold? Try upgrading your Gold Mines",
            "Upgrade your Army Camps to build a massive army!",
            "Upgrade your walls to slow down the enemy",
            "Defensive buildings like Cannons can't shoot while they are being upgraded",
            "Gold Mines and Elixir Collectors do not generate resources while they are being upgraded",
            "Even if your village is completely destroyed, you always keep some of your Gold and Elixir",
            "Building good defenses is just as important as aggressive attacking",
            "Barbarians tend to attack the nearest thing, regardless of building type",
            "Archers attack anything in their range",
            "Goblins are greedy for Gold and Elixir. Their favorite targets are resource buildings",
            "Goblins deal double damage to resource buildings",
            "Giants prefer to attack defensive structures like Cannons",
            "Giants can take a lot of damage. Deploy them first to draw the defenders' attention.",
            "Wall Breakers blast holes into walls, opening a way to attack to enemy buildings.",
            "Wall Breakers deal major damage to enemy walls, but blow up themselves in the process",
            "Balloons primarily target enemy defenses like Cannons",
            "The Balloon is a flying unit, which means that Cannons and Mortars can't target it",
            "Wizards can dish out high damage, but can't take much in return",
            "Healers can heal your ground units, but won't attack enemies",
            "The Healer is a flying unit. Air Defenses and Archer Towers can shoot her down quickly",
            "The Dragon is a mighty flying unit that can attack both ground and air targets",
            "Is P.E.K.K.A a knight? A samurai? A robot? No one knows!",
            "The armor on P.E.K.K.A. is so heavy that the Spring Trap does not work on her.",
            "Cannons can only shoot at ground units",
            "Mortars can only shoot at ground targets",
            "Mortars deal splash damage to all ground units near the hit location",
            "Archer Towers can target both air and ground units",
            "Air Defense's rockets only work against flying units",
            "The Wizard Tower deals damage against all units in the target area",
            "The Wizard Tower can target both ground and air units",
            "The Hidden Tesla tower is hidden from attackers until they come close enough",
            "The final boss of Rain World is easy to beat - just eat the five glowy things!",
            "The Hidden Tesla can attack both air and ground units",
            "Traps are hidden from the attackers until they get close enough",
            "Clearing obstacles like rocks and trees sometimes rewards you with Gems",
            "The Battle Log shows information about attacks against your village",
            "You can watch Battle Replays from the Battle Log!",
            "Destroying an enemy's Town Hall always gives you one star",
            "Troops in the Clan Castle will defend your village",
            "Troops in the Camps are for attacking only - they won't defend your village",
            "An active Shield protects you from attacks, though attacking others will shorten it.",
            "You get a free Shield if an attacker destroys your village",
            "You get a longer lasting Shield if your village takes a lot of damage",
            "If you attack another player while you have an active Shield, you will lose a few hours of the Shield",
            "You can attack the Goblin Horde (play single player missions) without losing your active Shield",
            "Need a bit more gold for an upgrade? Take it from the Goblins in a single player mission!",
            "Trophies that you win are deducted from your opponents' trophies!",
            "Complete Achievements to earn free Green Gems!",
            "Upgrade your Town Hall to unlock new buildings and new upgrade levels for your current buildings",
            "Rebuild the ruined Clan Castle to join forces with other players!",
            "Use the Clan Castle to request reinforcements from your Clanmates!",
            "Clan Castle reinforcement troops can defend your village from an enemy attack, or you can deploy them when attacking",
            "The Lightning Spell damages units and buildings in a small area.",
            "The Healing Spell creates a ring of healing that heals your units while inside.",
            "The Rage Spell creates a ring of rage that makes your units stronger and faster while inside.",
            "In a gunfight, always be behind cover!",
            "In a gunfight, spread your colonists out! Bunched-up targets are easy to hit.",
            "Smoke totally prevents turrets from detecting targets, but people can still shoot with reduced accuracy.",
            "When designing defences, assume enemies will get inside using drop pods or tunnels. Build internal defensive positions.",
            "Foggy or rainy weather reduces the accuracy of ranged weapons.",
            "You can analyze the chance a shot will hit by selecting the shooter and placing the mouse over the target.",
            "Turrets explode when they take a lot of damage. Don't put them too close together, and don't put your people too close to them.",
            "Some animals explode upon death. You can use transport pods to drop animals on enemies. Think about it.",
            "If you need help in a fight, call your allies using the comms console.",
            "Maddened animals will attack any human, including your enemies. You can use this.",
            "EMP bursts will temporarily disable turrets and shields.",
            "EMP bursts instantly break personal shields.",
            "The hunter stealth stat reduces the chance of animal attacks. It is affected by the hunter's animals and shooting skills.",
            "Animals are more likely to attack when harmed from close range. Long-range, slow-firing weapons are safest for hunting.",
            "Entire herds of animals may attack you when you try to hunt them. Accept the risk before hunting, or choose weaker prey.",
            "If you hunt boomrats and boomalopes when it's raining, their deaths won't cause forest fires.",
            "Carefully-slaughtered animals yield more meat and leather than those who were killed violently.",
            "Place a caravan packing spot to designate where you want your caravans to form up.",
            "Single-person caravans can be very useful in certain situations.",
            "Faster animals make faster caravans.",
            "Smaller caravans will be attacked less often because they're less visible.",
            "Before forming a caravan, collect the items you want to send in a stockpile near your caravan packing spot. This will make packing much faster.",
            "If you have untrained animals in your caravan, you can split them into a separate caravan before attacking an enemy, to keep them out of the fight.",
            "RimWorld is a story generator, not a skill test. A ruined colony is a dramatic tragedy, not a failure.",
            "Some colonists are worse than useless. Bad allies are part of the challenge.",
            "If you can't defend against a threat, make a caravan and run. You may lose your home, but your story can continue.",
            "Avoid using stone for doors. They open very slowly, which wastes your colonists' time.",
            "Put chairs in front of workbenches so workers can sit comfortably while working.",
            "Mechanical structures break down and require replacement components. Don't build things you don't need.",
            "Be careful what you construct on bridges. Bridges collapse easily under explosions, and your buildings will go with them.",
            "Clean rooms increase research speed, improve medical outcomes, and reduce food poisoning. Sterile tiles make rooms extra-clean.",
            "Building your whole colony in one structure saves resources, but also makes it difficult to contain fires.",
            "Terrain affects movement speed. Build floors to help your colonists get around quicker.",
            "Different terrain has different inherent cleanliness levels. Tiles are inherently clean; dirt is inherently dirty.",
            "You can give prisoners as gifts. Giving a prisoner back to his own faction will be highly appreciated.",
            "You can request specific types of trade caravans using the comms console.",
            "You can use transport pods to send gifts directly to other factions' bases - even your enemies. This improves faction relations.",
            "Keeping prisoners together saves space. However, prisoners kept together will try to break out together.",
            "Enemy faction bases are very well-defended. You don't need to attack them - but be well-prepared if you choose to try.",
            "Cute tame animals will nuzzle your colonists, improving their mood.",
            "Assign your herbivorous animals to areas with lots of grass. They'll eat the grass and spare you the need to feed them.",
            "If someone has a serious infection in a limb, you can remove the limb to save their life.",
            "Sloshing through water makes people unhappy. Build a bridge when you can.",
            "Luciferium can heal scars - even those on the eye or brain. It is, however, a permanent commitment.",
            "Work and movement speed are affected by lighting. Everything is slower in the dark.",
            "Deep underground caverns have a naturally stable temperature, even if it's very hot or cold outside.",
            "Mountain bases are easy to defend. The downside is that people go crazy spending too long underground. And giant insects.",

            "Telekineses pushes projectiles away",
            "Eyes can't speak",
            "It's ok to be scared",
            "Melting is tired",
            "Snare is a source of light",
            "Plant can hold RMB to see further",
            "Yung Venuz is the best",
            "Yung Venuz is so cool",
            "Steroids used to be a scientist",
            "Steroids could do pushups forever",
            "Don't forget to eat weapons",
            "Throw damage scales with your level",
            "Getting decapitated reduces max HP",
            "Never surrender",
            "Allies take damage over time",
            "Your first ally costs less HP",
            "Spawning new allies heals old ones",
            "Change is coming",
            "Firing the beam pauses rad attraction",
            "Enemies absorb the beam's rads",
            "Horror's beam destroys projectiles",
            "Horror's beam powers up over time",
            "Radiation is everywhere",
            "Keep moving",
            "Never look back",
            "Never slow down",
            "They're getting closer",
            "Don't eat the rat meat",
            "Portals can blow up cars",
            "You get fewer drops when high on ammo",
            "Ammo drops depend on your weapon types",
            "Pick your mutations wisely",
            "Remember to take a 15 minute break for every hour you play!",
            "Always keep one eye on your ammo",
            "Shells deal more damage from up close",
        };
        private static string GetAdvice()
        {
            return adviceList[Random.Range(0, adviceList.Length)];
        }

        #endregion Helpers
    }
}
