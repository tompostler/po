using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace po.Platforms.Mattermost
{
    /// <summary>
    /// Calls the Mattermost REST API v4 as the <c>po</c> bot account.
    /// </summary>
    public sealed class MattermostApiClient
    {
        /// <summary>
        /// Mattermost's snake_case JSON, also used by the command endpoint for its responses.
        /// </summary>
        internal static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly HttpClient httpClient;

        public MattermostApiClient(HttpClient httpClient, IOptions<Options.Mattermost> options)
        {
            Options.Mattermost mattermost = options.Value;
            if (mattermost.ApiBaseUri == null || string.IsNullOrEmpty(mattermost.BotToken))
            {
                throw new InvalidOperationException($"{nameof(Options.Mattermost)}:{nameof(mattermost.ApiBaseUri)} and {nameof(Options.Mattermost)}:{nameof(mattermost.BotToken)} must both be configured.");
            }

            httpClient.BaseAddress = new Uri(mattermost.ApiBaseUri, "api/v4/");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mattermost.BotToken);
            this.httpClient = httpClient;
        }

        public async Task<MattermostUser> GetMeAsync(CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await this.httpClient.GetAsync("users/me", cancellationToken);
            return await ReadAsync<MattermostUser>(response, cancellationToken);
        }

        public async Task<MattermostPost> CreatePostAsync(MattermostPost post, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await this.httpClient.PostAsJsonAsync("posts", post, JsonOptions, cancellationToken);
            return await ReadAsync<MattermostPost>(response, cancellationToken);
        }

        public async Task<MattermostTeam> GetTeamByNameAsync(string name, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await this.httpClient.GetAsync($"teams/name/{Uri.EscapeDataString(name)}", cancellationToken);
            return await ReadAsync<MattermostTeam>(response, cancellationToken);
        }

        /// <summary>
        /// Lists the team's custom slash commands, leaving out built-in ones such as <c>/join</c>.
        /// </summary>
        public async Task<IReadOnlyList<MattermostCommand>> ListCommandsAsync(string teamId, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await this.httpClient.GetAsync($"commands?team_id={Uri.EscapeDataString(teamId)}&custom_only=true", cancellationToken);
            return await ReadAsync<List<MattermostCommand>>(response, cancellationToken);
        }

        public async Task<MattermostCommand> CreateCommandAsync(MattermostCommand command, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await this.httpClient.PostAsJsonAsync("commands", command, JsonOptions, cancellationToken);
            return await ReadAsync<MattermostCommand>(response, cancellationToken);
        }

        /// <summary>
        /// Replaces the command whose <see cref="MattermostCommand.Id"/> matches, which can't move it to another team.
        /// </summary>
        public async Task<MattermostCommand> UpdateCommandAsync(MattermostCommand command, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await this.httpClient.PutAsJsonAsync($"commands/{Uri.EscapeDataString(command.Id)}", command, JsonOptions, cancellationToken);
            return await ReadAsync<MattermostCommand>(response, cancellationToken);
        }

        public async Task DeleteCommandAsync(string commandId, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await this.httpClient.DeleteAsync($"commands/{Uri.EscapeDataString(commandId)}", cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }

        /// <summary>
        /// Replaces the command's token, for when a listing doesn't include it.
        /// </summary>
        public async Task<string> RegenerateCommandTokenAsync(string commandId, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await this.httpClient.PutAsync($"commands/{Uri.EscapeDataString(commandId)}/regen_token", content: null, cancellationToken);
            MattermostTokenResponse token = await ReadAsync<MattermostTokenResponse>(response, cancellationToken);
            return token.Token;
        }

        /// <summary>
        /// Posts a delayed reply to a slash command's <paramref name="responseUrl"/>, which Mattermost accepts 5 times within 30 minutes of the command.
        /// </summary>
        /// <remarks>
        /// The URL is built from Mattermost's public site URL, so only its path is kept and sent to <see cref="Options.Mattermost.ApiBaseUri"/>, like every other call.
        /// </remarks>
        public async Task PostCommandResponseAsync(Uri responseUrl, MattermostCommandResponse reply, CancellationToken cancellationToken)
        {
            var internalUrl = new Uri(this.httpClient.BaseAddress, responseUrl.PathAndQuery);
            using HttpResponseMessage response = await this.httpClient.PostAsJsonAsync(internalUrl, reply, JsonOptions, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }

        /// <summary>
        /// Uploads <paramref name="content"/> to <paramref name="channelId"/> without posting it.
        /// Pass the returned file id in <see cref="MattermostPost.FileIds"/> to post it.
        /// </summary>
        public async Task<string> UploadFileAsync(string channelId, string fileName, Stream content, CancellationToken cancellationToken)
        {
            // Sending channel_id before the file lets Mattermost stream the upload instead of buffering the whole form.
            using var form = new MultipartFormDataContent
            {
                { new StringContent(channelId), "channel_id" },
                { new StreamContent(content), "files", fileName },
            };

            using HttpResponseMessage response = await this.httpClient.PostAsync("files", form, cancellationToken);
            MattermostFileUploadResponse upload = await ReadAsync<MattermostFileUploadResponse>(response, cancellationToken);
            return upload.FileInfos.Single().Id;
        }

        /// <summary>
        /// Deserializes a successful response, or throws like <see cref="EnsureSuccessAsync"/>.
        /// </summary>
        private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            await EnsureSuccessAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }

        /// <summary>
        /// Throws with Mattermost's error message for an unsuccessful response, which <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> would discard.
        /// </summary>
        private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                string detail = body;
                try
                {
                    MattermostError error = JsonSerializer.Deserialize<MattermostError>(body, JsonOptions);
                    if (error?.Message != null)
                    {
                        detail = $"{error.Id}: {error.Message}";
                    }
                }
                catch (JsonException)
                {
                    // Not a Mattermost error body, such as a proxy's HTML page, so keep the raw text.
                }

                throw new HttpRequestException(
                    $"Mattermost {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath} returned {(int)response.StatusCode}: {detail}",
                    inner: null,
                    response.StatusCode);
            }
        }
    }
}
