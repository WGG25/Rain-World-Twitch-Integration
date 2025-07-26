using RWCustom;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;
using ObjectType = AbstractPhysicalObject.AbstractObjectType;
using CritType = CreatureTemplate.Type;
using MSCObjectType = MoreSlugcats.MoreSlugcatsEnums.AbstractObjectType;
using MSCCritType = MoreSlugcats.MoreSlugcatsEnums.CreatureTemplateType;
using DLCObjectType = DLCSharedEnums.AbstractObjectType;
using DLCCritType = DLCSharedEnums.CreatureTemplateType;
using System.IO;
using MonoMod.RuntimeDetour;
using System.Reflection;
using Music;
using MoreSlugcats;
using Expedition;
using System.Runtime.CompilerServices;

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
    internal static class Integrations
    {
        public static RainWorld RW => Custom.rainWorld;
        public static RainWorldGame Game => RW.processManager.currentMainLoop as RainWorldGame;
        public static IEnumerable<Player> Players => Game?.Players.Where(ply => ply.realizedObject is Player).Select(ply => (Player)ply.realizedObject) ?? Enumerable.Empty<Player>();
        public static bool InGame => Game != null;
        public static string RedeemUserName { get; set; }

        public static (MethodInfo, TwitchRewardAttribute)[] Attributes
        {
            get => _attributes ??= typeof(Integrations).GetMethods()
                .Select(x => (x, x.GetCustomAttribute<TwitchRewardAttribute>()))
                .Where(x => x.Item2 != null)
                .ToArray();
        }
        private static (MethodInfo, TwitchRewardAttribute)[] _attributes;

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


                    AbstractSpear absSpear;

                    int rand = Random.Range(0, 20);
                    if (rand == 0)
                    {
                        absSpear = new AbstractSpear(ply.room.world, null, ply.coord, ply.room.game.GetNewID(), true);
                    }
                    else if (rand == 1)
                    {
                        absSpear = new AbstractSpear(ply.room.world, null, ply.coord, ply.room.game.GetNewID(), false, true);
                    }
                    else if (rand == 2)
                    {
                        absSpear = new AbstractSpear(ply.room.world, null, ply.coord, ply.room.game.GetNewID(), false, Random.value);
                    }
                    else
                    {
                        absSpear = new AbstractSpear(ply.room.world, null, ply.coord, ply.room.game.GetNewID(), false);
                    }

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
            NameLabel.AddNameLabel(crit, RedeemUserName);
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
            NameLabel.AddNameLabel(crit, RedeemUserName);
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
            NameLabel.AddNameLabel(crit, RedeemUserName);
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

                    IntVector2 floorPos = feetPos;
                    while(!ply.room.GetTile(floorPos).Solid && floorPos.y >= 0)
                    {
                        floorPos.y--;
                    }
                    floorPos.y++;

                    if (ply.room.GetTile(feetPos).Solid                                                            // Solid tile
                        || floorPos.y <= 0 && !ply.room.water                                                      // Fall risk
                        || ply.room.readyForAI && !ply.room.aimap.AnyExitReachableFromTile(feetPos, testTemplate)  // Unreachable
                        || isRaining && ply.room.roomRain?.rainReach[feetPos.x] < feetPos.y                        // Rainy
                        || ply.room.waterObject is Water water && !water.IsTileAccessible(floorPos, testTemplate)) // Above lethal water
                    {
                        continue;
                    }

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

                            // Remove tongue attachments
                            if(Plugin.Config.DetachOnTeleport.Value)
                            {
                                if (ply.tongue != null && ply.tongue.Attached)
                                    ply.tongue.Release();

                                foreach (var grasp in ply.grasps)
                                {
                                    if(grasp?.grabbed is TubeWorm worm)
                                    {
                                        foreach (var tongue in worm.tongues)
                                            if (tongue.Attached)
                                                tongue.Release();
                                    }
                                }
                            }

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

                var objs = room.updateList.Where(obj => obj is Creature crit && crit.Template.type != CritType.Slugcat && crit.Template.type != MSCCritType.SlugNPC).ToArray();
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
                    .Where(obj => obj is Creature crit && crit.Template.type != CritType.Slugcat && crit.Template.type != MSCCritType.SlugNPC && !crit.grabbedBy.Any(g => g.grabber is Player)).ToArray();
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

            var colors = new Color[3];

            if (Plugin.Config.ClassicColors.Value)
            {
                for (int i = 0; i < 3; i++)
                    colors[i] = new Color(Random.Range(0.02f, 1f), Random.Range(0.02f, 1f), Random.Range(0.02f, 1f));
            }
            else
            {
                var body = new HSLColor(Random.value, Mathf.Pow(Random.value, 0.75f), Custom.ClampedRandomVariation(0.5f, 0.5f, 0.6f)).FilterBlack();
                var eye = new HSLColor(Custom.WrappedRandomVariation(body.hue, 0.5f, 0.3f), Random.value, Custom.PushFromHalf(body.lightness, 3f)).FilterBlack();
                var extra = new HSLColor(Custom.WrappedRandomVariation(body.hue + 0.5f, 0.5f, 0.8f), Random.value, Random.value).FilterBlack();

                colors[0] = body.rgb;
                colors[1] = eye.rgb;
                colors[2] = extra.rgb;
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

            bool EnableCustomColors(On.PlayerGraphics.orig_CustomColorsEnabled orig)
            {
                return true;
            }

            Color GetCustomColors(On.PlayerGraphics.orig_CustomColorSafety orig, int index)
            {
                return index >= 0 && index < colors.Length ? colors[index] : orig(index);
            }

            Color GetJollyColor(On.PlayerGraphics.orig_JollyColor orig, int playerNumber, int index)
            {
                if (index >= 0 && index < colors.Length)
                {
                    return colors[index];
                }
                return orig(playerNumber, index);
            }

            Timer.FastForward("Randomize Colors");

            // Add a hook to change player colors
            On.PlayerGraphics.CustomColorsEnabled += EnableCustomColors;
            On.PlayerGraphics.CustomColorSafety += GetCustomColors;
            On.PlayerGraphics.JollyColor += GetJollyColor;
            UpdatePlayerColors();


            // Add a timer to undo the hooks
            Timer.Set(() =>
            {
                On.PlayerGraphics.CustomColorsEnabled -= EnableCustomColors;
                On.PlayerGraphics.CustomColorSafety -= GetCustomColors;
                On.PlayerGraphics.JollyColor -= GetJollyColor;
                if (InGame)
                    UpdatePlayerColors();
            }, 60f, "Randomize Colors");

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

            Player.InputPackage InvertInput(On.RWInput.orig_PlayerInput_int orig, int playerNumber)
            {
                Player.InputPackage inputs = orig(playerNumber);
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

            On.RWInput.PlayerInput_int += InvertInput;

            Timer.Set(() => {
                On.RWInput.PlayerInput_int -= InvertInput;
                ShowNotification("Invert Controls has worn off!");
            }, 15f);

            return RewardStatus.Done;
        }

        // Plays a random song
        [TwitchReward("Play Random Song", AvailableInMenu = true)]
        public static RewardStatus PlayRandomSong()
        {
            var mp = RW.processManager.musicPlayer;
            if (mp == null) return RewardStatus.Cancel;

            var songName = GetRandomSong();
            if (songName == null) return RewardStatus.Cancel;

            Debug.Log("Play random song " + songName);

            var song = new Song(mp, songName, MusicPlayer.MusicContext.StoryMode)
            {
                fadeOutAtThreat = 1f,
                Loop = false,
                priority = 1f,
                baseVolume = 0.3f,
                fadeInTime = 2f,
                stopAtDeath = false,
                stopAtGate = false
            };

            if (mp.song == null)
            {
                mp.song = song;
                mp.song.playWhenReady = true;
            }
            else
            {
                mp.nextSong = song;
                mp.nextSong.playWhenReady = false;
            }

            return RewardStatus.Done;
        }

        // Randomizes the player's stats
        [TwitchReward("Randomize Slugcat Stats")]
        public static RewardStatus RandomizeStats()
        {
            if (!InGame) return RewardStatus.Cancel;
            Timer.FastForward("Randomize Stats");

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

            HSLColor skyCol, fogCol, blackCol, itemCol, waterCol, farWaterCol,
                surfCol, farSurfCol, surfHighlightCol, fogAmount, shortcut1,
                shortcut2, shortcut3, shortcutSymbol, darkness;

            Color[,] geometry = new Color[30, 6];

            if (Plugin.Config.ClassicColors.Value)
            {
                // Generate a new palette that doesn't look completely like clown vomit
                skyCol = new HSLColor(Random.value, Random.value, Random.value).FilterBlack();
                fogCol = skyCol.Randomize(0.1f).FilterBlack();

                blackCol = new HSLColor(Random.value, Random.value * 0.5f, Random.value * 0.4f).FilterBlack();
                itemCol = blackCol.Randomize(0.1f).FilterBlack();

                waterCol = new HSLColor(Random.value, Random.value, Random.value).FilterBlack();
                farWaterCol = waterCol.Push(Random.value * 0.2f - 0.1f, -0.5f, -0.2f).FilterBlack();
                surfCol = waterCol.Push(Random.value * 0.2f - 0.1f, Random.value * 0.25f, Random.value * 0.5f - 0.25f);
                farSurfCol = surfCol.Push(0f, Random.value * -0.5f, Random.value * 0.2f);
                surfHighlightCol = surfCol.Randomize(0.2f);

                fogAmount = new HSLColor(0f, 0f, Random.value);
                shortcut1 = new HSLColor(Random.value, Random.value, Random.value);
                shortcut2 = shortcut1.Randomize(0.2f);
                shortcut3 = shortcut2.Randomize(0.2f);
                shortcutSymbol = new HSLColor(Random.value, Random.value, Random.value);

                float averageLightness = 0f;
                {
                    HSLColor rowCol = new HSLColor(Random.value, Random.value, Random.value);
                    HSLColor firstCol = rowCol;
                    for (int y = 0; y < 6; y++)
                    {
                        if (y == 3)
                            rowCol = firstCol.Randomize(0.5f).Push(0f, Random.value * -0.25f, Random.value * -0.25f);

                        HSLColor columnCol = rowCol;
                        for (int x = 0; x < 30; x++)
                        {
                            geometry[x, y] = columnCol.rgb;
                            columnCol = columnCol.Randomize(0.05f).Push(0f, -0.025f, 0.025f);
                        }
                        rowCol = rowCol.Randomize(0.25f);
                    }

                    averageLightness += rowCol.lightness / (30 * 6);
                }

                darkness = new HSLColor(0f, 0f, Mathf.Clamp01(2f - 2f * averageLightness));
            }
            else
            {
                darkness = new HSLColor(0f, 0f, Custom.PushFromHalf(Random.value, 1.75f));
                float bright = 1f - darkness.lightness;

                skyCol = new HSLColor(Random.value, 0.75f * Mathf.Pow(Random.value, 2f), 1f - bright);
                fogCol = skyCol.Randomize(0.1f);
                blackCol = new HSLColor(Random.value, Random.value * 0.2f, Mathf.Min(bright, Mathf.Pow(Random.value, 2f))).FilterBlack();
                itemCol = blackCol.Randomize(0.05f).FilterBlack();

                waterCol = new HSLColor(Random.value, Random.value, Custom.ClampedRandomVariation(0.05f + bright * 0.3f, 0.5f, 0.2f));
                farWaterCol = HSLColor.Lerp(waterCol.Randomize(0.2f), fogCol, Random.value * 0.5f + 0.5f);
                surfCol = new HSLColor(waterCol.hue, Custom.ClampedRandomVariation(waterCol.saturation + 0.2f, 0.4f, 0.5f), Custom.ClampedRandomVariation(waterCol.lightness + 0.3f, 0.5f, 0.5f));
                farSurfCol = HSLColor.Lerp(surfCol.Randomize(0.2f), fogCol, Random.value * 0.5f + 0.5f);
                surfHighlightCol = surfCol.Randomize(0.2f);
                surfHighlightCol.lightness = Mathf.Clamp01(surfHighlightCol.lightness + 0.2f);

                fogAmount = new HSLColor(0f, 0f, Random.value);
                shortcut3 = new HSLColor(Random.value, Random.value, Random.value);
                shortcut1 = new HSLColor(Custom.WrappedRandomVariation(shortcut3.hue, 0.3f, 0.3f), Custom.ClampedRandomVariation(shortcut3.saturation, 0.3f, 0.4f), Random.value * shortcut3.lightness);
                shortcut2 = HSLColor.Lerp(shortcut1, shortcut3, Random.value * 0.5f + 0.25f);
                shortcutSymbol = new HSLColor(Random.value, Random.value, Random.value);

                var geoMain = new HSLColor(Random.value, Mathf.Pow(Random.value, 1.75f), 0.7f * bright);
                var geoBright = new HSLColor(Custom.WrappedRandomVariation(geoMain.hue, 0.3f, 0.6f), Custom.ClampedRandomVariation(geoMain.saturation, 0.3f, 0.4f), Custom.ClampedRandomVariation(geoMain.lightness + 0.3f, 0.3f, 0.5f));
                var geoDark = new HSLColor(Custom.WrappedRandomVariation(geoMain.hue, 0.3f, 0.6f), Custom.ClampedRandomVariation(geoMain.saturation, 0.3f, 0.4f), Custom.ClampedRandomVariation(geoMain.lightness - 0.4f * bright, 0.3f * bright, 0.2f));
                geoDark = HSLColor.Lerp(geoDark, fogCol, Random.value * fogAmount.lightness);

                for (int y = 0; y < 5; y++)
                {
                    HSLColor start;
                    HSLColor end;
                    if (y < 3)
                    {
                        start = HSLColor.Lerp(geoMain, geoDark, 0.5f + y * 0.25f).Randomize(0.1f);
                        end = HSLColor.Lerp(geoBright, geoMain, 0.5f + y * 0.25f).Randomize(0.1f);
                    }
                    else
                    {
                        start = HSLColor.Lerp(geoMain, geoDark, y == 5 ? 1f : 0.75f);
                        end = geoMain;
                    }
                    HSLColor[] sections = new HSLColor[Random.Range(3, 6)];

                    float pow = Random.Range(0.5f, 1.5f);
                    for(int i = 0; i < sections.Length; i++)
                    {
                        sections[i] = HSLColor.Lerp(start, end, Mathf.Pow(i / (sections.Length - 1f), pow));
                    }

                    for (int x = 0; x < 30; x++)
                    {
                        float t = x / 29f * (sections.Length - 1f);

                        int from = Mathf.FloorToInt(t);
                        int to = Mathf.CeilToInt(t);
                        geometry[x, y] = HSLColor.Lerp(sections[from], sections[to], Custom.PushFromHalf(t - from, 2f)).rgb;
                    }
                }
                for(int x = 0; x < 30; x++)
                {
                    geometry[x, 5] = geometry[x, 4];
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
                colors[topRow + 30] = darkness.rgb;
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
            var type = Random.value < 0.2f && ModManager.MSC ? DLCCritType.ScavengerElite : CritType.Scavenger;
            var crit = new AbstractCreature(ply.room.world, StaticWorld.GetCreatureTemplate(type), null, new WorldCoordinate(room.index, -1, -1, -1), ply.room.game.GetNewID());
            NameLabel.AddNameLabel(crit, RedeemUserName);
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
            NameLabel.AddNameLabel(crit, RedeemUserName);
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

        [TwitchReward("Lizard Cannon")]
        public static RewardStatus LizardCannon()
        {
            if (!InGame) return RewardStatus.Cancel;

            var ply = Players.FirstOrDefault();
            if (ply == null || ply.room == null) return RewardStatus.TryLater;
            var room = ply.room.abstractRoom;

            if (!room.realizedRoom.CritsAllowed()) return RewardStatus.Cancel;

            // Add hooks
            if (!lizardCannonHooksAdded)
            {
                lizardCannonHooksAdded = true;

                On.Lizard.SpitOutOfShortCut += (orig, self, pos, newRoom, spitOutAllSticks) =>
                {
                    orig(self, pos, newRoom, spitOutAllSticks);

                    if (launchLizards.TryGetValue(self.abstractCreature, out var state)
                        && !state.launched)
                    {
                        state.launched = true;
                        state.dir = newRoom.ShorcutEntranceHoleDirection(pos).ToVector2();
                        state.framesLeft = 10;
                    }
                };

                On.Lizard.Update += (orig, self, eu) =>
                {
                    orig(self, eu);

                    if (launchLizards.TryGetValue(self.abstractCreature, out var state)
                        && state.launched)
                    {
                        if (state.framesLeft-- <= 0)
                        {
                            launchLizards.Remove(self.abstractCreature);
                        }
                        else
                        {
                            self.JawOpen = 1f;
                            if(self.AI is LizardAI ai)
                            {
                                foreach (var player in Players)
                                {
                                    if (player.room == self.room)
                                        ai.tracker.SeeCreature(player.abstractCreature);
                                }
                            }
                            foreach (var chunk in self.bodyChunks)
                            {
                                chunk.vel += state.dir * state.framesLeft + Vector2.up;
                            }
                        }
                    }
                };
            }

            // Find a random room exit leading to the player's room
            var nodes = room.nodes.Select((node, i) => new KeyValuePair<int, AbstractRoomNode>(i, node)).Where(node => node.Value.type == AbstractRoomNode.Type.Exit).ToArray();
            if (nodes.Length == 0) return RewardStatus.Cancel;

            // Spawn the lizard
            var crit = new AbstractCreature(ply.room.world, StaticWorld.GetCreatureTemplate(RandomLizardType()), null, new WorldCoordinate(room.index, -1, -1, -1), ply.room.game.GetNewID());
            NameLabel.AddNameLabel(crit, RedeemUserName);
            crit.Realize();

            // Move the lizard into the pipe
            crit.realizedCreature.inShortcut = true;
            room.world.game.shortcuts.CreatureEnterFromAbstractRoom(crit.realizedCreature, room, nodes[Random.Range(0, nodes.Length)].Key);
            room.AddEntity(crit);

            if (crit.realizedObject is Lizard liz)
            {
                launchLizards.Add(crit, new CannonBoostState());
                liz.JawOpen = 1f;
            }

            return RewardStatus.Done;
        }
        static bool lizardCannonHooksAdded = false;
        static readonly ConditionalWeakTable<AbstractCreature, CannonBoostState> launchLizards = new();
        class CannonBoostState
        {
            public bool launched;
            public Vector2 dir;
            public int framesLeft;
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
                    string advice = GetAdvice();
                    if (advice == null)
                        return RewardStatus.Cancel;
                    tp.AddMessage(advice, 10, 160, true, false);
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

                var rand = Random.value;

                if(rand < 0.01f && ModManager.MSC)
                    explosive = new AbstractPhysicalObject(ply.room.world, DLCObjectType.SingularityBomb, null, ply.coord, ply.room.game.GetNewID());
                if (rand < 0.7f)
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

        // Gives the player the mushroom effect
        [TwitchReward("Drugs")]
        public static RewardStatus GiveShroomEffect() => GiveStatusEffect(StatusEffect.Mushroomed);

        private static RewardStatus GiveStatusEffect(StatusEffect effect)
        {
            if (activeStatusEffects.Contains(effect)) return RewardStatus.Cancel;
            if (effect == StatusEffect.Mushroomed && Players.Any(p => p.mushroomCounter > 0)) return RewardStatus.Cancel;
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
                if (!self.slatedForDeletetion
                    && self.enteringShortCut == null)
                {
                    for (int i = 0; i < 2; i++)
                        orig(self, eu);
                }
                else
                {
                    orig(self, eu);
                }
            }

            // Gravity
            void ApplyLight(On.Player.orig_UpdateMSC orig, Player self)
            {
                orig(self);

                if (self.Submersion > 0.5f)
                {
                    ShowNotification("Light Status Effect has washed off!");
                    Timer.FastForward("Light Status");
                }
                else if (!self.monkAscension)
                    self.gravity = self.customPlayerGravity * 0.5f;
            }

            void ApplyHeavy(On.Player.orig_UpdateMSC orig, Player self)
            {
                orig(self);

                if (self.Submersion > 0.5f)
                {
                    ShowNotification("Heavy Status Effect has washed off!");
                    Timer.FastForward("Heavy Status");
                }
                else if (!self.monkAscension)
                    self.gravity = self.customPlayerGravity * 1.65f;
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
                    didSomething = true;
                    On.Player.UpdateMSC += ApplyLight;
                    Timer.FastForward("Heavy Status");
                    Timer.Set(() =>
                    {
                        On.Player.UpdateMSC -= ApplyLight;
                        activeStatusEffects.Remove(effect);
                        if (!Timer.FastForwarding)
                        {
                            ShowNotification("Light Status Effect has worn off!");
                        }
                        foreach (var ply in Players)
                        {
                            ply.gravity = ply.customPlayerGravity;
                        }
                    }, 30f, "Light Status");
                    break;

                case StatusEffect.Heavy:
                    didSomething = true;
                    On.Player.UpdateMSC += ApplyHeavy;
                    Timer.FastForward("Light Status");
                    Timer.Set(() =>
                    {
                        On.Player.UpdateMSC -= ApplyHeavy;
                        activeStatusEffects.Remove(effect);
                        if (!Timer.FastForwarding)
                        {
                            ShowNotification("Heavy Status Effect has worn off!");
                        }
                        foreach (var ply in Players)
                        {
                            ply.gravity = ply.customPlayerGravity;
                        }
                    }, 15f, "Heavy Status");
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
                        if (roomRain != null && roomRain.intensity < 0.05f)
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

        [TwitchReward("Shorten Cycle")]
        public static RewardStatus ShortenCycle()
        {
            if (!InGame || Game.world.rainCycle is not RainCycle rc) return RewardStatus.Cancel;

            rc.timer += 40 * 60;
            return RewardStatus.Done;
        }

        [TwitchReward("Lengthen Cycle")]
        public static RewardStatus LengthenCycle()
        {
            if (!InGame || Game.world.rainCycle is not RainCycle rc) return RewardStatus.Cancel;

            int target = rc.timer - 40 * 60;
            rc.timer = Mathf.Max(target, 0);
            rc.pause += rc.timer - target;

            return RewardStatus.Done;
        }

        [TwitchReward("Grant High Agility")]
        public static RewardStatus GrantHighAgility()
        {
            if (!InGame || !ModManager.Expedition || !ModManager.MSC) return RewardStatus.Cancel;

            static void EnableExpStats(On.SlugcatStats.orig_ctor orig, SlugcatStats self, SlugcatStats.Name slugcat, bool malnourished)
            {
                int oldSave = RW.options.saveSlot;
                RW.options.saveSlot = -oldSave - 1;
                orig(self, slugcat, malnourished);
                RW.options.saveSlot = oldSave;
            }

            Timer.FastForward("Agility");

            var exp = EnableExpeditionUnlocks("unl-agility");
            var isRiv = new Hook(
                typeof(Player).GetProperty(nameof(Player.isRivulet)).GetGetMethod(),
                (Player self) => true
            );
            On.SlugcatStats.ctor += EnableExpStats;

            UpdateStats();

            Timer.Set(() =>
            {
                exp.Dispose();
                isRiv.Dispose();
                On.SlugcatStats.ctor -= EnableExpStats;
                UpdateStats();
                if (!Timer.FastForwarding)
                    ShowNotification("High Agility has worn off!");
            }, 30f, "Agility");

            return RewardStatus.Done;
        }

        [TwitchReward("Grant Explosive Jump")]
        public static RewardStatus GrantExplosiveJump()
        {
            if (!InGame || !ModManager.Expedition || !ModManager.MSC) return RewardStatus.Cancel;

            Timer.FastForward("Explosive Jump");

            var exp = EnableExpeditionUnlocks("unl-explosivejump");
            var hasExpJump = new Hook(
                typeof(ExpeditionGame).GetProperty(nameof(ExpeditionGame.explosivejump)).GetGetMethod(),
                (Func<bool>)(() => true)
            );
            
            Timer.Set(() =>
            {
                exp.Dispose();
                hasExpJump.Dispose();
                if (!Timer.FastForwarding)
                    ShowNotification("Explosive Jump has worn off!");
            }, 30f, "Explosive Jump");

            return RewardStatus.Done;
        }

        [TwitchReward("Spawn Slugpup")]
        public static RewardStatus SpawnSlugpup()
        {
            if (!InGame || !ModManager.MSC) return RewardStatus.Cancel;

            var ply = Players.FirstOrDefault();
            if (ply == null || ply.room == null) return RewardStatus.TryLater;
            var room = ply.room.abstractRoom;

            if (!room.realizedRoom.CritsAllowed()) return RewardStatus.Cancel;

            // Find a random room exit leading to the player's room
            var nodes = room.nodes.Select((node, i) => new KeyValuePair<int, AbstractRoomNode>(i, node)).Where(node => node.Value.type == AbstractRoomNode.Type.Exit).ToArray();
            if (nodes.Length == 0) return RewardStatus.Cancel;

            // Spawn the pup
            var crit = new AbstractCreature(ply.room.world, StaticWorld.GetCreatureTemplate(MSCCritType.SlugNPC), null, new WorldCoordinate(ply.room.world.offScreenDen.index, -1, -1, 0), ply.room.game.GetNewID());
            NameLabel.AddNameLabel(crit, RedeemUserName);
            (crit.state as PlayerNPCState).foodInStomach = 1;
            crit.Realize();

            // Move the pup into the pipe
            crit.realizedCreature.inShortcut = true;
            room.world.game.shortcuts.CreatureEnterFromAbstractRoom(crit.realizedCreature, room, nodes[Random.Range(0, nodes.Length)].Key);
            room.AddEntity(crit);

            return RewardStatus.Done;
        }

        [TwitchReward("Switch Rooms")]
        public static RewardStatus SwitchRooms()
        {
            if (!InGame) return RewardStatus.Cancel;

            bool didSomething = false;
            foreach (var ply in Players)
            {
                if (ply.room != null && ply.enteringShortCut == null
                    && (ply.room.shelterDoor is not ShelterDoor door || !door.IsClosing))
                {
                    var shortcuts = ply.room.shortcuts.Where(sc => sc.shortCutType == ShortcutData.Type.RoomExit);
                    int count = shortcuts.Count();

                    if (count > 0)
                    {
                        didSomething = true;
                        ply.AllGraspsLetGoOfThisObject(true);
                        ply.enteringShortCut = shortcuts.ElementAt(Random.Range(0, count)).StartTile;
                        if (ModManager.MSC && ply.tongue != null && ply.tongue.Attached)
                        {
                            ply.tongue.Release();
                        }
                        ply.SuperHardSetPosition(ply.room.MiddleOfTile(ply.enteringShortCut.Value));
                    }
                }
            }

            return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
        }

        [TwitchReward("Wake Up Early")]
        public static RewardStatus WakeUpEarly()
        {
            if (forcePrecycle || !ModManager.MSC) return RewardStatus.Cancel;

            forcePrecycle = true;

            if(!precycleHooksAdded)
            {
                precycleHooksAdded = true;

                On.OverWorld.LoadFirstWorld += (orig, self) =>
                {
                    if (forcePrecycle)
                    {
                        bool wasForcePrecycle = self.game.rainWorld.setup.forcePrecycles;
                        try
                        {
                            self.game.rainWorld.setup.forcePrecycles = true;
                            orig(self);
                        }
                        finally
                        {
                            self.game.rainWorld.setup.forcePrecycles = wasForcePrecycle;
                            forcePrecycle = false;
                        }
                    }
                    else
                    {
                        orig(self);
                    }
                };
            }

            return RewardStatus.Done;
        }
        private static bool forcePrecycle;
        private static bool precycleHooksAdded;

        [TwitchReward("Alert Enemies")]
        public static RewardStatus AlertEnemies()
        {
            if (!InGame) return RewardStatus.Cancel;

            bool didSomething = false;

            foreach(var room in Game.world.activeRooms)
            {
                foreach(var crit in room.abstractRoom.creatures)
                {
                    if(crit?.abstractAI?.RealAI is ArtificialIntelligence ai && ai.tracker != null)
                    {
                        foreach (var ply in Game.Players)
                        {
                            if (ply != null && ply.state.alive)
                            {
                                didSomething = true;
                                ai.tracker.SeeCreature(ply);
                            }
                        }
                    }
                }
            }

            return didSomething ? RewardStatus.Done : RewardStatus.TryLater;
        }

        [TwitchReward("Disable Gravity")]
        public static RewardStatus DisableGravity()
        {
            if (!InGame) return RewardStatus.Cancel;

            Timer.FastForward("Antigrav");

            static void DefaultAntiGrav(On.Room.orig_ctor orig, Room self, RainWorldGame game, World world, AbstractRoom abstractRoom, bool devUI)
            {
                orig(self, game, world, abstractRoom, devUI);

                self.gravity = 0f;
            }

            On.Room.ctor += DefaultAntiGrav;

            foreach (var room in Game.world.activeRooms)
            {
                room.gravity = 0f;
            }

            Timer.Set(() =>
            {
                On.Room.ctor -= DefaultAntiGrav;
                foreach (var room in Game.world.activeRooms)
                {
                    room.gravity = 1f;
                }

            }, 10f, "Antigrav");

            return RewardStatus.Done;
        }

        // Helper methods can also go here
        // These won't do anything on their own unless you apply the TwitchReward attribute
        #region Helpers

        class ToggledUnlocks : IDisposable
        {
            readonly string[] toRevoke;

            public ToggledUnlocks(string[] toGrant)
            {
                toRevoke = toGrant
                    .Where(unl => !ExpeditionGame.activeUnlocks.Contains(unl))
                    .ToArray();

                ExpeditionGame.activeUnlocks.AddRange(toRevoke);
            }

            public void Dispose()
            {
                ExpeditionGame.activeUnlocks.RemoveAll(toRevoke.Contains);
            }
        }

        static ToggledUnlocks EnableExpeditionUnlocks(params string[] unlocks)
        {
            return new ToggledUnlocks(unlocks);
        }

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
            (world, pos, id) => new DangleFruit.AbstractDangleFruit(world, null, pos, id, -1, -1, false, null),
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
        static readonly Func<World, WorldCoordinate, EntityID, AbstractPhysicalObject>[] mscItemFactories = new Func<World, WorldCoordinate, EntityID, AbstractPhysicalObject>[]
        {
            (world, pos, id) => new AbstractSpear(world, null, pos, id, false, true),
            (world, pos, id) => new AbstractSpear(world, null, pos, id, false, Random.value),
            (world, pos, id) => new AbstractConsumable(world, DLCObjectType.GooieDuck, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, DLCObjectType.DandelionPeach, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, DLCObjectType.GlowWeed, null, pos, id, -1, -1, null),
            (world, pos, id) => new AbstractConsumable(world, DLCObjectType.Seed, null, pos, id, -1, -1, null),
            (world, pos, id) => new LillyPuck.AbstractLillyPuck(world, null, pos, id, 3, -1, -1, null),
            (world, pos, id) => new FireEgg.AbstractBugEgg(world, null, pos, id, Random.value),
        };
        static AbstractPhysicalObject MakeRandomItem(World world, WorldCoordinate pos)
        {
            int rand = Random.Range(0, 40);
            if (rand == 0)
            {
                var rock = new AbstractPhysicalObject(world, ObjectType.Rock, null, pos, world.game.GetNewID());
                PensiveRock.Mark(rock);
                return rock;
            }
            else if (rand == 99 && ModManager.MSC)
            {
                var bomb = new AbstractPhysicalObject(world, DLCObjectType.SingularityBomb, null, pos, world.game.GetNewID());
                return bomb;
            }
            else
            {
                var factories = ModManager.MSC ? itemFactories.Concat(mscItemFactories) : itemFactories;
                var factory = factories.ElementAt(Random.Range(0, factories.Count()));
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
            var files = AssetManager.ListDirectory("music/songs");
            var songNames = files
                .Select(Path.GetFileName)
                .Where(f => f != null 
                    && songExtensionWhitelist.Any(f.EndsWith)
                    && !songBlacklist.Contains(f))
                .ToArray();

            if(songNames.Length == 0)
            {
                Plugin.Logger.LogWarning("No songs to play!");
                return null;
            }

            var songName = songNames[Random.Range(0, songNames.Length)];
            return Path.GetFileNameWithoutExtension(songName);
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

            new Weighted<CritType>(1f, new (nameof(DLCCritType.AquaCenti))),
            new Weighted<CritType>(1f, new (nameof(DLCCritType.EelLizard))),
            new Weighted<CritType>(0.5f, new (nameof(MSCCritType.FireBug))),
            new Weighted<CritType>(0.25f, new (nameof(DLCCritType.Inspector))),
            new Weighted<CritType>(0.4f, new (nameof(DLCCritType.MirosVulture))),
            new Weighted<CritType>(1f, new (nameof(DLCCritType.MotherSpider))),
            new Weighted<CritType>(0.5f, new (nameof(DLCCritType.ScavengerElite))),
            new Weighted<CritType>(1f, new (nameof(DLCCritType.SpitLizard))),
            new Weighted<CritType>(0.25f, new (nameof(DLCCritType.TerrorLongLegs))),
            new Weighted<CritType>(0.25f, new (nameof(MSCCritType.TrainLizard))),
            new Weighted<CritType>(1f, new (nameof(DLCCritType.Yeek))),
            new Weighted<CritType>(1f, new (nameof(DLCCritType.ZoopLizard))),
        };
        static CritType RandomCreatureType()
        {
            return RandomWeighted(critTypes.Where(type => type.value.Index != -1));
        }

        static CritType RandomLizardType()
        {
            return RandomWeighted(critTypes.Where(type => type.value.Index != -1 && StaticWorld.GetCreatureTemplate(type.value)?.TopAncestor().type == CritType.LizardTemplate));
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

        private static void UpdateStats()
        {
            if (Game is not RainWorldGame game) return;

            if (game?.session is ArenaGameSession arena)
            {
                var stats = arena.characterStats_Mplayer;
                for (int i = 0; i < stats.Length; i++)
                {
                    if (stats[i] != null)
                        stats[i] = new SlugcatStats(stats[i].name, stats[i].malnourished);
                }
            }

            foreach (var ply in game.Players)
            {
                if (ply?.realizedObject is Player realPly)
                    realPly.SetMalnourished(realPly.slugcatStats.malnourished);
            }
        }

        private static string[] adviceList;
        private static string GetAdvice()
        {
            if (adviceList == null)
            {
                var path = AssetManager.ResolveFilePath("text/ti_advice.txt");
                try
                {
                    adviceList = File.ReadAllLines(path)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .ToArray();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    return null;
                }
            }
            return adviceList[Random.Range(0, adviceList.Length)];
        }

        #endregion Helpers
    }
}
