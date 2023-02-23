using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api.Auth;
using TwitchLib.Api;
using UnityEngine;

namespace TwitchIntegration
{
    internal class LoginPrompt : IDisposable
    {
        private static readonly string _redirectUri = "http://localhost:37506/";

        public bool Done => _loginTask is Task<LoginToken> login && login.IsCompleted;
        public LoginToken Validation
        {
            get
            {
                if(_loginTask is Task<LoginToken> login && login.IsCompleted)
                {
                    return login.Result;
                }
                else
                {
                    return null;
                }
            }
        }

        public string Token { get; set; }

        private readonly Task<LoginToken> _loginTask;
        private readonly CancellationTokenSource _tokenSource;
        private readonly TwitchAPI _api;
        private readonly Auth _auth;
        private HttpListener _server;

        public LoginPrompt(TwitchAPI api, string cachedToken = null)
        {
            _api = api;
            _auth = new Auth(api.Settings, null, null);

            _tokenSource = new CancellationTokenSource();
            _loginTask = Login(_tokenSource.Token, cachedToken);
        }


        public async Task<LoginToken> Login(CancellationToken ct, string cachedToken = null)
        {
            // Authorize

            // Try cached token
            string accessToken = cachedToken;
            ValidateAccessTokenResponse validation = null;
            if (cachedToken != null)
            {
                accessToken = cachedToken;
                validation = await _auth.ValidateAccessTokenAsync(accessToken);
            }

            ct.ThrowIfCancellationRequested();

            // If cached token fails, request a new one
            if (validation == null)
            {
                accessToken = await GetOAuthToken(ct);
                validation = await _auth.ValidateAccessTokenAsync(accessToken);
            }

            if (validation == null)
                throw new InvalidOperationException("Failed to validate token!");

            return new LoginToken(accessToken, validation);
        }

        private async Task<string> GetOAuthToken(CancellationToken ct)
        {
            var url = _auth.GetAuthorizationCodeUrl(_redirectUri, _api.Settings.Scopes, clientId: _api.Settings.ClientId).Replace("response_type=code", "response_type=token");
            Plugin.Logger.LogDebug(url);

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
                    Plugin.Logger.LogDebug($"New request: {client.Request.HttpMethod}, {client.Request.LocalEndPoint}");
                    if (client.Request.HttpMethod == "POST")
                    {
                        string code = new StreamReader(client.Request.InputStream).ReadToEnd();
                        Plugin.Logger.LogDebug($"Code: {code}");
                        foreach (var entry in code.Split('&'))
                        {
                            string[] args = entry.Split('=');
                            if (args.Length == 2 && args[0] == "access_token")
                            {
                                Plugin.Logger.LogDebug($"'{args[0]}' = '{args[1]}' (Token)");
                                accessToken = args[1];
                                break;
                            }
                            else
                            {
                                Plugin.Logger.LogDebug(string.Join(", ", args));
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
