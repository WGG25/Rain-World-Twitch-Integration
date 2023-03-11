using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TwitchIntegration
{
    public class Timer : MonoBehaviour
    {
        public static bool FastForwarding { get; private set; }

        private static float time = 0f;

        static Timer instance;
        static Timer Instance
        {
            get
            {
                if(instance == null)
                    instance = new GameObject("Twitch Integration Timer").AddComponent<Timer>();
                return instance;
            }
        }

        readonly List<QueuedAction> queue = new List<QueuedAction>();

        public static void Set(Action action, float delay, string name = null)
        {
            if (action == null) return;
            Instance.QueueAction(action, delay, name);
        }

        public static void FastForward(string name)
        {
            bool wasFF = FastForwarding;
            FastForwarding = true;

            if (name == null) return;
            var queue = Instance.queue;
            int i;
            while ((i = queue.FindIndex(x => x.name == name)) != -1)
            {
                if (i != -1)
                {
                    try
                    {
                        queue[i].action();
                    }
                    catch (Exception e)
                    {
                        Plugin.Logger.LogError("Failed to fast forward timer! Check exception log for more info.");
                        Debug.LogException(e);
                    }
                    queue.RemoveAt(i);
                }
            }

            FastForwarding = wasFF;
        }

        public static void FastForwardAll()
        {
            bool wasFF = FastForwarding;
            FastForwarding = true;

            for (int i = 0; i < Instance.queue.Count; i++)
                Instance.queue[i] = new QueuedAction(Instance.queue[i].action, 0f, Instance.queue[i].name);

            FastForwarding = wasFF;
        }

        void QueueAction(Action action, float delay, string name)
        {
            queue.Add(new QueuedAction(action, time + delay, name));
        }

        public void Update()
        {
            time += Time.unscaledDeltaTime;

            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].executeAt <= time)
                {
                    try
                    {
                        queue[i].action();
                    }
                    catch(Exception e)
                    {
                        Plugin.Logger.LogError("Failed to execute timer! Check exception log for more info.");
                        Debug.LogException(e);
                    }
                    queue.RemoveAt(i);
                }
            }
        }

        private struct QueuedAction
        {
            public readonly Action action;
            public readonly float executeAt;
            public readonly string name;

            public QueuedAction(Action action, float executeAt, string name = null)
            {
                this.action = action;
                this.executeAt = executeAt;
                this.name = name;
            }
        }
    }
}
