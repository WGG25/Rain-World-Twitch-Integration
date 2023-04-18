using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TwitchIntegration
{
    public class NameLabel : CosmeticSprite
    {
        private static readonly ConditionalWeakTable<AbstractCreature, string> _names = new();

        private readonly Creature _owner;
        private readonly string _name;

        private Vector2 CenterPos => _owner.mainBodyChunk.pos + Vector2.up * (_owner.mainBodyChunk.rad + 20f);

        public NameLabel(Creature owner, string name)
        {
            _owner = owner;
            _name = name;

            pos = CenterPos;
            lastPos = pos;
        }

        public override void Update(bool eu)
        {
            if (_owner.slatedForDeletetion || _owner.room != room)
            {
                Destroy();
            }

            base.Update(eu);

            pos = CenterPos;
        }

        public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
        {
            sLeaser.sprites = new FSprite[0];
            sLeaser.containers = new FContainer[] { new FContainer() };

            var label = new FLabel(RWCustom.Custom.GetFont(), _name)
            {
                anchorX = 0.5f,
                anchorY = 0.5f
            };

            var back = new FSprite("Futile_White")
            {
                anchorX = 0.5f,
                anchorY = 0.5f,
                width = label.textRect.width + 2f,
                height = label.textRect.height + 2f,
                color = new Color(0f, 0f, 0f, 0.3f)
            };

            sLeaser.containers[0].AddChild(back);
            sLeaser.containers[0].AddChild(label);

            AddToContainer(sLeaser, rCam, null);
        }

        public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner)
        {
            newContatiner ??= rCam.ReturnFContainer("HUD");

            newContatiner.AddChild(sLeaser.containers[0]);
        }

        public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
        {
            base.DrawSprites(sLeaser, rCam, timeStacker, camPos);

            var drawPos = Vector2.Lerp(lastPos, pos, timeStacker) - camPos;
            sLeaser.containers[0].SetPosition(drawPos);
        }

        public static void AddHooks()
        {
            On.CreatureState.BaseSaveString += CreatureState_BaseSaveString;
            On.CreatureState.LoadFromString += CreatureState_LoadFromString;
            On.Room.AddObject += Room_AddObject;
        }

        private static string CreatureState_BaseSaveString(On.CreatureState.orig_BaseSaveString orig, CreatureState self)
        {
            string text = orig(self);

            if (_names.TryGetValue(self.creature, out string name))
            {
                text += "TWITCHOWNER<cC>" + name + "<cB>";
            }

            return text;
        }

        private static void CreatureState_LoadFromString(On.CreatureState.orig_LoadFromString orig, CreatureState self, string[] s)
        {
            orig(self, s);

            if(self.unrecognizedSaveStrings.TryGetValue("TWITCHOWNER", out var name)
                && !_names.TryGetValue(self.creature, out _))
            {
                self.unrecognizedSaveStrings.Remove("TWITCHOWNER");
                _names.Add(self.creature, name);
            }
        }

        public static void AddNameLabel(AbstractCreature creature, string name)
        {
            _names.Add(creature, name);
        }

        private static void Room_AddObject(On.Room.orig_AddObject orig, Room self, UpdatableAndDeletable obj)
        {
            orig(self, obj);

            if(obj is Creature crit
                && _names.TryGetValue(crit.abstractCreature, out var name))
            {
                self.AddObject(new NameLabel(crit, name));
            }
        }
    }
}
