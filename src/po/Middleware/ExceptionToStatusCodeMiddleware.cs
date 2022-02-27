using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using po.Exceptions;
using System;
using System.Threading.Tasks;

namespace po.Middleware
{
    public sealed class ExceptionToStatusCodeMiddleware
    {
        private readonly RequestDelegate next;
        private readonly ILogger<ExceptionToStatusCodeMiddleware> logger;

        public ExceptionToStatusCodeMiddleware(RequestDelegate next, ILogger<ExceptionToStatusCodeMiddleware> logger)
        {
            this.next = next;
            this.logger = logger;
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await this.next(httpContext);
            }
            catch (Exception ex)
            {
                if (httpContext.Response.HasStarted)
                {
                    this.logger.LogError(ex, "Response in progress. Could not rewrite!");
                }
                else
                {
                    if (ex is BadRequestException)
                    {
                        this.logger.LogWarning(ex, "Caught an unhandled exception!");
                        httpContext.Response.StatusCode = 400;
                    }
                    else if (ex is UnauthorizedException)
                    {
                        this.logger.LogWarning(ex, "Caught an unhandled exception!");
                        httpContext.Response.StatusCode = 401;
                    }
                    else if (ex is NotFoundException)
                    {
                        this.logger.LogWarning(ex, "Caught an unhandled exception!");
                        httpContext.Response.StatusCode = 404;
                    }
                    else if (ex is ConflictException)
                    {
                        this.logger.LogWarning(ex, "Caught an unhandled exception!");
                        httpContext.Response.StatusCode = 409;
                    }
                    else
                    {
                        this.logger.LogError(ex, "Caught an unhandled exception!");
                        httpContext.Response.StatusCode = 500;
                    }
                    httpContext.Response.ContentType = "text/plain";
                    await httpContext.Response.WriteAsync(ex.ToString());
                }
            }
        }
    }
}
