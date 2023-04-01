using System;
using System.Threading.Tasks;

namespace TwitchIntegration
{
    internal static class Utils
    {
        public static Task LogFailure(this Task task)
        {
            return task.ContinueWith(ValidateSuccess);
        }

        public static bool ValidateSuccess(Task task)
        {
            if (task.Exception != null)
            {
                foreach (var inner in task.Exception.InnerExceptions)
                {
                    Plugin.Logger.LogError("Task failed!\n" + Environment.StackTrace);
                    Plugin.Logger.LogError(inner);
                    UnityEngine.Debug.LogException(inner);
                }
            }

            return !task.IsFaulted;
        }
    }
}
