using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TwitchIntegration
{
    public class RedemptionNotification : CosmeticSprite
    {
        readonly string text;
        int lifetime;

        public RedemptionNotification(Vector2 pos, string text)
        {
            this.text = text;
            this.pos = pos;
            lastPos = pos;
        }

        public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner)
        {
            base.AddToContainer(sLeaser, rCam, newContatiner);

            newContatiner.AddChild(sLeaser.containers[0]);
        }

        public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            sLeaser.containers[0].SetPosition(Vector2.Lerp(lastPos, pos, timeStacker) - camPos);
            sLeaser.containers[0].isVisible = lifetime < 40 || lifetime % 10 < 5;

            base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
        }

        public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            base.InitiateSprites(sLeaser, rCam);

            sLeaser.sprites = new FSprite[0];
            sLeaser.containers = new FContainer[1]
            {
                new FContainer()
            };

            sLeaser.containers[0].AddChild(new FLabel("font", text));

            AddToContainer(sLeaser, rCam, rCam.ReturnFContainer("Bloom"));
        }

        public override void Update(bool eu)
        {
            vel.y = (lifetime < 40) ? 2f : 0f;

            base.Update(eu);
            lifetime++;

            if (lifetime >= 80) Destroy();
        }
    }
}
