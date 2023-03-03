using System.Threading.Tasks;

namespace TwitchIntegration
{
    internal static class Utils
    {
        public static Task LogFailure(this Task task)
        {
            return task.ContinueWith(doneTask =>
            {
                if(doneTask.IsFaulted)
                {
                    Plugin.Logger.LogError("Task failed!");
                    foreach (var e in doneTask.Exception.InnerExceptions)
                    {
                        UnityEngine.Debug.LogException(e);
                    }
                }
            });
        }
    }
}
