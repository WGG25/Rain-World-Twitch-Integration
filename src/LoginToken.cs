using TwitchLib.Api.Auth;

namespace TwitchIntegration
{
    internal class LoginToken
    {
        public string AccessToken { get; }
        public string UserID => _validation.UserId;

        private ValidateAccessTokenResponse _validation;

        public LoginToken(string accessToken, ValidateAccessTokenResponse validation)
        {
            AccessToken = accessToken;
            _validation = validation;
        }
    }
}
