using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace po.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ApiKeyOrSignedUrlAuthorizationAttribute : Attribute, IAuthorizationFilter
    {
        private const string ApiKeyHeaderName = "X-Api-Key";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            ILogger<ApiKeyOrSignedUrlAuthorizationAttribute> logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<ApiKeyOrSignedUrlAuthorizationAttribute>>();
            Options.Api options = context.HttpContext.RequestServices.GetRequiredService<IOptions<Options.Api>>().Value;
            string configuredApiKey = options.ApiKey;

            if (string.IsNullOrEmpty(configuredApiKey))
            {
                logger.LogError("API key not configured on server");
                context.Result = new ObjectResult("API key not configured on server")
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
                return;
            }

            // Check for API key header
            bool hasApiKeyHeader = context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out StringValues providedApiKey);
            if (hasApiKeyHeader)
            {
                if (string.Equals(configuredApiKey, providedApiKey, StringComparison.Ordinal))
                {
                    return;
                }
                else
                {
                    logger.LogWarning("Invalid API key [{providedKey}] provided for {path}", providedApiKey, context.HttpContext.Request.Path);
                    context.Result = new ObjectResult("Invalid API key") { StatusCode = StatusCodes.Status403Forbidden };
                    return;
                }
            }

            // Check for signed URL parameters
            if (context.HttpContext.Request.Query.TryGetValue("sig", out StringValues sig))
            {
                string containerName = context.RouteData.Values["containerName"]?.ToString();
                string blobName = context.RouteData.Values["blobName"]?.ToString();

                if (!context.HttpContext.Request.Query.TryGetValue("expires", out StringValues expiresValue)
                    || !long.TryParse(expiresValue, out long expiresUnixSeconds))
                {
                    logger.LogWarning("Signature present [{sig}] but missing or invalid expires parameter [{expires}] for {path}", sig, expiresValue, context.HttpContext.Request.Path);
                    context.Result = new ObjectResult("Missing or invalid expires parameter") { StatusCode = StatusCodes.Status403Forbidden };
                    return;
                }

                if (string.IsNullOrEmpty(containerName) || string.IsNullOrEmpty(blobName))
                {
                    logger.LogWarning("Signature present [{sig}] but missing route parameters for {path}", sig, context.HttpContext.Request.Path);
                    context.Result = new ObjectResult("Invalid request path") { StatusCode = StatusCodes.Status403Forbidden };
                    return;
                }

                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresUnixSeconds)
                {
                    logger.LogWarning("Expired expiration [{expires}] for {path}", expiresValue, context.HttpContext.Request.Path);
                    context.Result = new ObjectResult("Signature expired") { StatusCode = StatusCodes.Status403Forbidden };
                    return;
                }

                if (!Utilities.SignedUrls.ValidateSignature(configuredApiKey, containerName, blobName, expiresUnixSeconds, sig, logger))
                {
                    // Validation method logs
                    context.Result = new ObjectResult("Invalid signature") { StatusCode = StatusCodes.Status403Forbidden };
                    return;
                }

                return;
            }

            // No authentication provided
            logger.LogWarning("No authentication provided for {path}", context.HttpContext.Request.Path);
            context.Result = new ObjectResult("Authentication required") { StatusCode = StatusCodes.Status401Unauthorized };
        }
    }
}
