using Newtonsoft.Json;
using System.IO;

namespace TwitchIntegration
{
    internal class SetupFile
    {
        [JsonProperty("client_id")]
        public string ClientID { get; private set; }

        [JsonProperty("redirect_uri")]
        public string RedirectUri { get; private set; }

        [JsonProperty("mock_api_port")]
        public int MockApiPort { get; private set; } = 8080;

        [JsonProperty("mock_eventsub_port")]
        public int MockEventSubPort { get; private set; } = 8081;

        [JsonProperty("mock_show_console")]
        public bool MockShowConsole { get; private set; } = false;

        public static SetupFile Load(string path)
        {
            return JsonConvert.DeserializeObject<SetupFile>(File.ReadAllText(path));
        }

        private SetupFile() { }
    }
}
