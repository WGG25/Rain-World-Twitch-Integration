using System.Collections.Generic;
using TwitchLib.Api.Core.Enums;
using System;
using System.Threading.Tasks;
using TwitchLib.Api.Core.Interfaces;
using Microsoft.Extensions.Logging;
using TwitchLib.Api.Core.HttpCallHandlers;
using System.Diagnostics;
using UnityEngine;

namespace TwitchIntegration
{
    internal class MockApi : IDisposable
    {
        private Process mockApi;
        private Process eventSub;
        public static MockApi Instance { get; private set; }

        private MockApi()
        {
            mockApi = new Process();
            mockApi.StartInfo = new ProcessStartInfo("twitch", $"mock-api start --port {Plugin.SetupFile.MockApiPort}")
            {
                UseShellExecute = Plugin.SetupFile.MockShowConsole,
                CreateNoWindow = !Plugin.SetupFile.MockShowConsole,
            };
            mockApi.Start();

            eventSub = new Process();
            eventSub.StartInfo = new ProcessStartInfo("twitch", $"event websocket start-server --port {Plugin.SetupFile.MockEventSubPort}")
            {
                UseShellExecute = Plugin.SetupFile.MockShowConsole,
                CreateNoWindow = !Plugin.SetupFile.MockShowConsole,
            };
            eventSub.Start();

            Instance = this;

            Application.quitting += () =>
            {
                mockApi.Kill();
                eventSub.Kill();
            };
        }

        public static void Start()
        {
            Instance = new MockApi();
        }

        public static void Stop()
        {
            Instance?.Dispose();
            Instance = null;
        }

        public void TriggerRedeem(RewardInfo reward)
        {
            var process = new Process();
            process.StartInfo = new ProcessStartInfo("twitch", $"event trigger channel.channel_points_custom_reward_redemption.add --transport=websocket --item-id=\"{reward.Id}\" --item-name=\"{reward.Title}\"")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            process.Start();
        }

        public void Dispose()
        {
            mockApi.Kill();
            eventSub.Kill();
        }
    }

    internal class MockHttpClientHandler : IHttpCallHandler
    {
        private readonly string mockHelixUrl = $"http://localhost:{Plugin.SetupFile.MockApiPort}/mock";
        private readonly string mockEventSubUrl = $"http://localhost:{Plugin.SetupFile.MockEventSubPort}/";
        private const string helixUrl = "https://api.twitch.tv/helix";
        private const string eventSubUrl = "https://api.twitch.tv/helix/eventsub";
        private readonly TwitchHttpClient client;

        public MockHttpClientHandler(ILogger<TwitchHttpClient> logger = null)
        {
            client = new TwitchHttpClient(logger);
        }

        public Task<KeyValuePair<int, string>> GeneralRequestAsync(string url, string method, string payload = null, ApiVersion api = ApiVersion.Helix, string clientId = null, string accessToken = null)
        {
            url = Redirect(url);
            return client.GeneralRequestAsync(url, method, payload, api, clientId, accessToken);
        }

        public Task PutBytesAsync(string url, byte[] payload)
        {
            url = Redirect(url);
            return client.PutBytesAsync(url, payload);
        }

        public Task<int> RequestReturnResponseCodeAsync(string url, string method, List<KeyValuePair<string, string>> getParams = null)
        {
            url = Redirect(url);
            return client.RequestReturnResponseCodeAsync(url, method, getParams);
        }

        private string Redirect(string url)
        {
            if (url.StartsWith(eventSubUrl)) return url.Replace(eventSubUrl, mockEventSubUrl);
            else if (url.StartsWith(helixUrl)) return url.Replace(helixUrl, mockHelixUrl);
            else throw new ArgumentException("Could not redirect url!");
        }
    }
}

