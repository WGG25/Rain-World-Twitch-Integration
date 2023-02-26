using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TwitchIntegration
{
    internal static class CacheData
    {
        private static Dictionary<string, object> _data;
        private static string _oAuthToken;
        private static List<string> _ownedRewards;

        private const string KEY_TOKEN = "oauth_token";
        private const string KEY_REWARDS = "owned_rewards";

        public static string FilePath => Path.Combine(Application.persistentDataPath, "twitchintegration.json");

        public static string OAuthToken
        {
            get { Load(); return _oAuthToken; }
            set { Load(); _oAuthToken = value; }
        }

        public static List<string> OwnedRewards
        {
            get { Load(); return _ownedRewards; }
        }

        private static void Load()
        {
            if (_data != null) return;

            try
            {
                _data = File.ReadAllText(FilePath).dictionaryFromJson();
            }
            catch
            {
                _data = new();
            }

            if (_data.TryGetValue(KEY_TOKEN, out object token) && token is string tokenString)
                _oAuthToken = tokenString;

            if (_data.TryGetValue(KEY_REWARDS, out object rewards) && rewards is List<object> rewardsList)
                _ownedRewards = rewardsList.Select(x => x as string).Where(x => x != null).ToList();
            else
                _ownedRewards = new();
        }

        public static void Save()
        {
            _data[KEY_REWARDS] = OwnedRewards;
            _data[KEY_TOKEN] = OAuthToken;

            File.WriteAllText(FilePath, _data.toJson());
        }

        public static void Reload()
        {
            _data = null;
            _oAuthToken = null;
            _ownedRewards = null;
        }
    }
}
