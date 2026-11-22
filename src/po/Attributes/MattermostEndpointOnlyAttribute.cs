using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace po.Attributes
{
    /// <summary>
    /// Answers 404 unless the request arrived on the <c>Kestrel:Endpoints:Mattermost</c> port, which is reachable only on the docker network, so the internet can't reach what this guards.
    /// </summary>
    /// <remarks>
    /// This checks the port the connection actually arrived on, because the <c>Host</c> header that <c>[Host]</c> and <c>RequireHost</c> match is set by the client.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class MattermostEndpointOnlyAttribute : Attribute, IAuthorizationFilter
    {
        public const string EndpointUrlKey = "Kestrel:Endpoints:Mattermost:Url";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            IConfiguration configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            string url = configuration[EndpointUrlKey];
            if (string.IsNullOrEmpty(url))
            {
                ILogger<MattermostEndpointOnlyAttribute> logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<MattermostEndpointOnlyAttribute>>();
                logger.LogError($"{EndpointUrlKey} is not configured, so Mattermost-only endpoints refuse every request.");
                context.Result = new NotFoundResult();
                return;
            }

            if (context.HttpContext.Connection.LocalPort != BindingAddress.Parse(url).Port)
            {
                context.Result = new NotFoundResult();
            }
        }
    }
}
