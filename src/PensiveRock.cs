using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace TwitchIntegration
{
    internal static class PensiveRock
    {
        private static readonly HashSet<EntityID> rocks = new HashSet<EntityID>();
        private static bool initialized = false;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;

            Futile.atlasManager.LoadImage("atlases/ti_emoji");

            On.Rock.InitiateSprites += (orig, rock, sLeaser, rCam) =>
            {
                orig(rock, sLeaser, rCam);

                if(rocks.Contains(rock.abstractPhysicalObject.ID))
                {
                    sLeaser.sprites[0].SetElementByName("atlases/ti_emoji");
                }
            };

            On.Rock.DrawSprites += (orig, rock, sLeaser, rCam, timeStacker, camPos) =>
            {
                orig(rock, sLeaser, rCam, timeStacker, camPos);

                if(rocks.Contains(rock.abstractPhysicalObject.ID))
                {
                    sLeaser.sprites[0].color = Color.white;
                    sLeaser.sprites[1].color = new Color(0.997f, 0.906f, 0.722f);
                }
            };

            On.Rock.ctor += (orig, rock, apo, world) =>
            {
                orig(rock, apo, world);

                if (rocks.Contains(rock.abstractPhysicalObject.ID))
                    rock.firstChunk.rad = 8f;
            };

            On.RainWorldGame.ShutDownProcess += (orig, self) =>
            {
                orig(self);

                rocks.Clear();
            };
        }

        public static void Mark(AbstractPhysicalObject rock)
        {
            Init();
            rocks.Add(rock.ID);
        }
    }
}
