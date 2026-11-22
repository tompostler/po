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
        private static readonly JsonSerializerOptions JsonOptions = new()
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
        /// Deserializes a successful response, or throws with Mattermost's error message, which <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> would discard.
        /// </summary>
        private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
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

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
    }
}
