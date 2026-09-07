using Newtonsoft.Json;
namespace TwitchBetBot.Models
{
    /// <summary>Ответ https://www.donationalerts.com/oauth/token</summary>
    public class DaTokenResponse
    {
        [JsonProperty("token_type")]
        public string TokenType { get; set; }
        [JsonProperty("expires_in")]
        public long ExpiresIn { get; set; }
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }
    }
    /// <summary>GET /api/v1/user/oauth</summary>
    public class DaUserResponse
    {
        [JsonProperty("data")]
        public DaUser Data { get; set; }
    }
    public class DaUser
    {
        [JsonProperty("id")]
        public long Id { get; set; }
        [JsonProperty("code")]
        public string Code { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("socket_connection_token")]
        public string SocketConnectionToken { get; set; }
    }
    /// <summary>POST /api/v1/centrifuge/subscribe</summary>
    public class DaSubscribeResponse
    {
        [JsonProperty("channels")]
        public DaSubscribeChannel[] Channels { get; set; }
    }
    public class DaSubscribeChannel
    {
        [JsonProperty("channel")]
        public string Channel { get; set; }
        [JsonProperty("token")]
        public string Token { get; set; }
    }
    /// <summary>Донат из канала $alerts:donation_&lt;user_id&gt;</summary>
    public class DaDonation
    {
        [JsonProperty("id")]
        public long Id { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("username")]
        public string Username { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }
        [JsonProperty("message_type")]
        public string MessageType { get; set; }
        [JsonProperty("amount")]
        public double Amount { get; set; }
        [JsonProperty("currency")]
        public string Currency { get; set; }
        /// <summary>Сумма в валюте стримера — есть в сообщениях Centrifugo</summary>
        [JsonProperty("amount_in_user_currency")]
        public double? AmountInUserCurrency { get; set; }
        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }
        public string DisplayName => string.IsNullOrWhiteSpace(Username) ? "Аноним" : Username;
    }
}
