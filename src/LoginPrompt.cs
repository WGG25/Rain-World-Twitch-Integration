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
                if(_loginTask is var login && login.IsCompleted)
                {
                    return login.Result;
                }
                else
                {
                    return default;
                }
            }
        }

        public string Token { get; set; }

        private readonly Task<KeyValuePair<string, TwitchAPI>> _loginTask;
        private readonly CancellationTokenSource _tokenSource;
        private HttpListener _server;

        public LoginPrompt(string clientID, IEnumerable<AuthScopes> scopes, string cachedToken = null)
        {
            _tokenSource = new CancellationTokenSource();
            _loginTask = Login(clientID, scopes.ToList(), _tokenSource.Token, cachedToken);
        }


        public async Task<KeyValuePair<string, TwitchAPI>> Login(string clientID, List<AuthScopes> scopes, CancellationToken ct, string cachedToken = null)
        {
            // Authorize
            var api = new TwitchAPI();
            api.Settings.ClientId = clientID;
            api.Settings.Scopes = scopes;

            // Try cached token
            ValidateAccessTokenResponse validation = null;
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

                    // Receive post with code
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
                    }

                    // Send response
                    string localPath = client.Request.Url.LocalPath;
                    if (localPath is "/" or "/code")
                    {
                        client.Response.ContentType = "text/html";
                        client.Response.StatusCode = 200;
                        client.Response.Close(response, true);
                    }
                    else
                    {
                        client.Response.StatusCode = 404;
                        client.Response.Close();
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
