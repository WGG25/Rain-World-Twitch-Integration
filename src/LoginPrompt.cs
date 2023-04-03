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
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using Newtonsoft.Json;
using TwitchLib.Api.Core.Common;
using Microsoft.Extensions.Logging;

namespace TwitchIntegration
{
    internal class LoginPrompt : IDisposable
    {
        private static readonly string _redirectUri = "http://localhost:37506/";

        public bool Done => _loginTask is Task<KeyValuePair<string, TwitchAPI>> login && login.IsCompleted;
        public KeyValuePair<string, TwitchAPI> Result
        {
            get
            {
                if(_loginTask is Task<KeyValuePair<string, TwitchAPI>> login && login.IsCompleted)
                {
                    return login.Result;
                }
                else
                {
                    return default;
                }
            }
        }

        public readonly MockData MockApi;
        public string Token { get; set; }

        private readonly Task<KeyValuePair<string, TwitchAPI>> _loginTask;
        private readonly CancellationTokenSource _tokenSource;
        private HttpListener _server;

        public LoginPrompt(string clientID, IEnumerable<AuthScopes> scopes, MockData mockApi = null, ILoggerFactory logger = null, string cachedToken = null)
        {
            _tokenSource = new CancellationTokenSource();
            MockApi = mockApi;
            _loginTask = Login(clientID, scopes.ToList(), _tokenSource.Token, logger, cachedToken);
        }

        async Task<KeyValuePair<string, TwitchAPI>> Login(string clientID, List<AuthScopes> scopes, CancellationToken ct, ILoggerFactory logger, string cachedToken = null)
        {
            // Authorize
            TwitchAPI api;

            if (MockApi == null)
            {
                ValidateAccessTokenResponse validation = null;

                // Use real data
                api = new TwitchAPI(logger);
                api.Settings.ClientId = clientID;
                api.Settings.Scopes = scopes;

                // Try cached token
                if (cachedToken != null)
                {
                    api.Settings.AccessToken = cachedToken;
                    validation = await api.Auth.ValidateAccessTokenAsync();
                }

                ct.ThrowIfCancellationRequested();

                // If cached token fails, request a new one
                if (validation == null)
                {
                    api.Settings.AccessToken = await GetOAuthToken(api, ct);
                    validation = await api.Auth.ValidateAccessTokenAsync();
                }

                if (validation == null)
                    throw new InvalidOperationException("Failed to validate token!");

                Plugin.Logger.LogDebug($"Logged in! UserId={validation.UserId}, Login={validation.Login}");

                return new(validation.UserId, api);
            }
            else
            {
                // Use mock API
                api = new TwitchAPI(logger, http: MockApi.HttpCallHandler);
                api.Settings.ClientId = MockApi.ClientID;
                api.Settings.Scopes = scopes;

                var client = new HttpClient();

                var uri = new UriBuilder("http", "localhost", 8080, "auth/authorize");
                uri.Query = "client_id=" + MockApi.ClientID
                    + "&client_secret=" + MockApi.ClientSecret
                    + "&grant_type=user_token"
                    + "&grant_type=user_token"
                    + "&user_id=" + MockApi.UserID
                    + "&scope=" + string.Join("%20", scopes.Select(Helpers.AuthScopesToString));

                var res = await client.PostAsync(uri.Uri, new StringContent(""));
                api.Settings.AccessToken = (string)(await res.Content.ReadAsStringAsync()).dictionaryFromJson()["access_token"];

                var user = await api.Helix.Users.GetUsersAsync();

                return new(user.Users[0].Id, api);
            }
        }

        private async Task<string> GetOAuthToken(TwitchAPI api, CancellationToken ct)
        {
            var url = api.Auth.GetAuthorizationCodeUrl(_redirectUri, api.Settings.Scopes, clientId: api.Settings.ClientId).Replace("response_type=code", "response_type=token");

            // Set up a server to receive the result
            var server = new HttpListener();
            server.Prefixes.Add(_redirectUri);
            _server = server;
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
                        if(accessToken != null)
                        {
                            var validation = await api.Auth.ValidateAccessTokenAsync(accessToken);
                            if(validation == null)
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
            _server?.Close();
        }
    }
}
