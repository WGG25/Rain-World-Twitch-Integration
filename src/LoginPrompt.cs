using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api.Auth;
using TwitchLib.Api;
using UnityEngine;
using TwitchLib.Api.Core.Enums;
using System.Linq;
using TwitchLib.Api.Core.Interfaces;
using System.Net.Http;
using TwitchLib.Api.Core.Common;
using Microsoft.Extensions.Logging;
using Menu;
using Microsoft.Extensions.DependencyInjection;
using TwitchLib.Api.Core;
using TwitchLib.Communication.Clients;
using TwitchLib.EventSub.Websockets.Extensions;
using TwitchLib.EventSub.Websockets;
using System.Text.Json;

namespace TwitchIntegration
{
    internal class LoginPrompt : Dialog, IDisposable
    {
        private static readonly string redirectUri = "http://localhost:37506/";
        private readonly CancellationTokenSource cts;
        private HttpListener server;
        private Task<IntegrationSystem> loginTask;

        private SimpleButton closeButton;

        // API settings and other data
        private const string defaultClientID = "wtm2ouib4loubtj0tu2l6t69erfsgd";
        private const string mockClientID = "651437fa2cb52ffe5b8f4f76f7353d";
        private const string mockClientSecret = "94d89394af9573a5f60d1dd3b1caf0";
        private const string mockUserID = "20425321";
        private static readonly List<AuthScopes> authScopes = new()
        {
            AuthScopes.Helix_Channel_Read_Redemptions,
            AuthScopes.Helix_Channel_Manage_Redemptions,
        };

        public event Action<IntegrationSystem> Success;
        public event Action Failure;

        public LoginPrompt(ProcessManager manager, bool mock)
            : base("", new Vector2(478.1f, 115.200005f), manager)
        {
            cts = new CancellationTokenSource();

            closeButton = new SimpleButton(this, pages[0], "CANCEL", "CLOSE", pos + new Vector2((size.x - 110f) / 2f, 7f), new Vector2(110f, 30f));
            pages[0].subObjects.Add(closeButton);

            if (mock)
                loginTask = LoginMock(cts.Token);
            else
                loginTask = Login(cts.Token);
        }

        public override void Update()
        {
            base.Update();

            closeButton.menuLabel.text = loginTask == null ? "CLOSE" : "CANCEL";

            if (loginTask != null && loginTask.IsCompleted)
            {
                if (loginTask.IsFaulted)
                    descriptionLabelLong.text = loginTask.Exception.ToString();
                else
                    Close();
                loginTask = null;
            }
        }

        public override void Singal(MenuObject sender, string message)
        {
            if (message == "CLOSE")
                Close();
        }

        private void Close()
        {
            if (loginTask != null && loginTask.IsCompleted && !loginTask.IsFaulted && loginTask.Result != null)
                Success?.Invoke(loginTask.Result);
            else
                Failure?.Invoke();

            manager.StopSideProcess(this);
            Dispose();
        }

        private void SetLoadingText(string text)
        {
            Plugin.Logger.LogDebug("Login status: " + text);
            descriptionLabel.text = text;
        }

        private IServiceCollection GetDefaultServices()
        {
            return new DefaultServiceProviderFactory()
                .CreateBuilder(new ServiceCollection())
                .AddLogging(logger =>
                {
                    logger
                        .SetMinimumLevel(LogLevel.Debug)
                        .AddProvider(new BepInExLoggerProvider());
                })
                .AddSingleton<IApiSettings>(new ApiSettings()
                {
                    Scopes = authScopes
                })
                .AddSingleton<WebSocketClient>()
                .AddSingleton<TwitchAPI>()
                .AddTwitchLibEventSubWebsockets();
        }

        private async Task<IntegrationSystem> LoginMock(CancellationToken ct)
        {
            try
            {
                MockApi.Start();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e);
            }

            var services = GetDefaultServices()
                .AddSingleton<IHttpCallHandler, MockHttpClientHandler>()
                .BuildServiceProvider();

            var twitch = services.GetRequiredService<TwitchAPI>();
            var eventSub = services.GetRequiredService<EventSubWebsocketClient>();
            twitch.Settings.ClientId = mockClientID;
            twitch.Settings.Secret = mockClientSecret;

            SetLoadingText("Logging into mock API...");
            twitch.Settings.AccessToken = await GetMockAccessToken();
            ct.ThrowIfCancellationRequested();

            SetLoadingText("Connecting to Twitch events...");
            var system = new IntegrationSystem(twitch, eventSub, mockUserID);

            if (await system.Connect(new Uri("ws://127.0.0.1:8081/ws")))
            {
                SetLoadingText("Connected!");
                return system;
            }
            else
            {
                SetLoadingText("Failed to connect!");
                return null;
            }
        }

        private async Task<string> GetMockAccessToken()
        {
            using var client = new HttpClient();

            var uri = new UriBuilder("http", "localhost", 8080, "auth/authorize");
            uri.Query = "client_id=" + mockClientID
                + "&client_secret=" + mockClientSecret
                + "&grant_type=user_token"
                + "&user_id=" + mockUserID
                + "&scope=" + string.Join("%20", authScopes.Select(Helpers.AuthScopesToString));

            var res = await client.PostAsync(uri.Uri, null);
            var stream = await res.Content.ReadAsStreamAsync();
            var json = await JsonDocument.ParseAsync(stream);
            return json.RootElement.GetProperty("access_token").GetString();
        }

        private async Task<IntegrationSystem> Login(CancellationToken ct)
        {
            var services = GetDefaultServices().BuildServiceProvider();
            var api = services.GetRequiredService<TwitchAPI>();
            var eventSub = services.GetRequiredService<EventSubWebsocketClient>();
            api.Settings.ClientId = defaultClientID;

            ValidateAccessTokenResponse validation = null;

            // Try cached token
            if (CacheData.OAuthToken != null)
            {
                api.Settings.AccessToken = CacheData.OAuthToken;
                SetLoadingText("Trying saved login info...");
                validation = await api.Auth.ValidateAccessTokenAsync();
                ct.ThrowIfCancellationRequested();
            }

            // If cached token fails, request a new one
            if (validation == null)
            {
                SetLoadingText("Log into Twitch with your web browser to continue.");
                api.Settings.AccessToken = await GetOAuthToken(api, ct);
                ct.ThrowIfCancellationRequested();
                SetLoadingText("Validating login info...");
                validation = await api.Auth.ValidateAccessTokenAsync();
                ct.ThrowIfCancellationRequested();
            }

            if (validation == null)
            {
                SetLoadingText("Failed to log in!");
                return null;
            }

            Plugin.Logger.LogDebug($"Logged in! UserId={validation.UserId}, Login={validation.Login}");

            SetLoadingText("Connecting to Twitch events...");
            var system = new IntegrationSystem(api, eventSub, validation.UserId);

            if (await system.Connect(null))
            {
                SetLoadingText("Connected!");
                return system;
            }
            else
            {
                SetLoadingText("Failed to connect!");
                return null;
            }
        }

        private async Task<string> GetOAuthToken(TwitchAPI api, CancellationToken ct)
        {
            var url = api.Auth.GetAuthorizationCodeUrl(redirectUri, api.Settings.Scopes).Replace("response_type=code", "response_type=token");

            // Set up a server to receive the result
            var server = new HttpListener();
            server.Prefixes.Add(redirectUri);
            this.server = server;
            try
            {
                server.Start();

                ct.ThrowIfCancellationRequested();

                string accessToken = null;

                // Direct the user to the OAuth page
                Application.OpenURL(url);

                byte[] response = File.ReadAllBytes(AssetManager.ResolveFilePath("twitchauth/code.html"));
                while (accessToken == null)
                {
                    ct.ThrowIfCancellationRequested();

                    // Receive post with code, returning a status message
                    var client = await server.GetContextAsync();
                    if (client.Request.HttpMethod == "POST")
                    {
                        string code = new StreamReader(client.Request.InputStream).ReadToEnd();
                        foreach (var entry in code.Split('&'))
                        {
                            string[] args = entry.Split('=');
                            if (args.Length == 2 && args[0] == "access_token")
                            {
                                accessToken = args[1];
                                break;
                            }
                        }

                        client.Response.ContentType = "text/plain";
                        int status;
                        string text;
                        if (accessToken != null)
                        {
                            var validation = await api.Auth.ValidateAccessTokenAsync(accessToken);
                            if (validation == null)
                            {
                                status = 400;
                                text = "Error: Couldn't validate login token!";
                            }
                            else
                            {
                                var info = await api.Helix.Users.GetUsersAsync(new() { validation.UserId }, accessToken: accessToken);
                                if (string.IsNullOrEmpty(info.Users[0].BroadcasterType))
                                {
                                    status = 400;
                                    text = $"Error: {validation.Login} must be a partner or affiliate to use this mod!";
                                }
                                else
                                {
                                    status = 200;
                                    text = $"Success: Logged in as {validation.Login}!";
                                }
                            }
                        }
                        else
                        {
                            status = 400;
                            text = "Error: Couldn't read login token!";
                        }
                        client.Response.StatusCode = status;
                        client.Response.Close(System.Text.Encoding.ASCII.GetBytes(text), false);
                    }
                    else
                    {
                        // Send response
                        string localPath = client.Request.Url.LocalPath;
                        if (localPath is "/" or "/code")
                        {
                            client.Response.ContentType = "text/html";
                            client.Response.StatusCode = 200;
                            client.Response.Close(response, false);
                        }
                        else
                        {
                            client.Response.StatusCode = 404;
                            client.Response.Close();
                        }
                    }
                }

                ct.ThrowIfCancellationRequested();

                return accessToken;
            }
            finally
            {
                server.Close();
            }
        }

        public void Dispose()
        {
            server?.Close();
            cts.Cancel();
            cts.Dispose();
        }
    }
}
