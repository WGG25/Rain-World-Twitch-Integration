using Newtonsoft.Json.Linq;
using System.IO;
using UnityEngine;

namespace TwitchIntegration
{
    internal static class CacheData
    {
        private static JObject _data;
        private static string _oAuthToken;
        private static readonly object _lock = new();

        private const string KEY_TOKEN = "oauth_token";

        public static string FilePath => Path.Combine(Application.persistentDataPath, "twitchintegration.json");

        public static string OAuthToken
        {
            get { lock (_lock) { Load(); return _oAuthToken; } }
            set { lock (_lock) { Load(); _oAuthToken = value; } }
        }

        private static void Load()
        {
            if (_data != null) return;

            try
            {
                _data = JObject.Parse(File.ReadAllText(FilePath));
            }
            catch
            {
                _data = new();
            }

            if (_data.TryGetValue(KEY_TOKEN, out JToken token) && token.Type == JTokenType.String)
                _oAuthToken = (string)token;
        }

        public static void Save()
        {
            lock (_lock)
            {
                _data[KEY_TOKEN] = OAuthToken;

                File.WriteAllText(FilePath, _data.ToString());
            }
        }

        public static void Reload()
        {
            lock (_lock)
            {
                _data = null;
                _oAuthToken = null;
            }
        }
    }
}
