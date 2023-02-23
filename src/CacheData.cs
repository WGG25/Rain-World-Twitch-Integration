using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TwitchIntegration
{
    internal class CacheData
    {
        private readonly Dictionary<string, object> _data;
        private const string KEY_TOKEN = "oauth_token";
        private const string KEY_REWARDS = "owned_rewards";

        public string FilePath => Path.Combine(Application.persistentDataPath, "twitchintegration.json");

        public string OAuthToken { get; set; }
        public List<string> OwnedRewards { get; } = new();

        public CacheData()
        {
            try
            {
                _data = File.ReadAllText(FilePath).dictionaryFromJson();
            }
            catch
            {
                _data = new();
            }

            if (_data.TryGetValue(KEY_TOKEN, out object token) && token is string tokenString)
                OAuthToken = tokenString;

            if (_data.TryGetValue(KEY_REWARDS, out object rewards) && rewards is List<object> rewardsList)
                OwnedRewards = rewardsList.Select(x => x as string).Where(x => x != null).ToList();
        }

        public void Save()
        {
            _data[KEY_REWARDS] = OwnedRewards;
            _data[KEY_TOKEN] = OAuthToken;

            File.WriteAllText(FilePath, _data.toJson());
        }
    }
}
