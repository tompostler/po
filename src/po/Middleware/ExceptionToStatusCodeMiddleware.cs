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
                this.logger.LogError(ex, "Caught an unhandled exception!");

                if (httpContext.Response.HasStarted)
                {
                    this.logger.LogError("Response in progress. Could not rewrite!");
                }
                else
                {
                    if (ex is BadRequestException)
                    {
                        httpContext.Response.StatusCode = 400;
                    }
                    else if (ex is UnauthorizedException)
                    {
                        httpContext.Response.StatusCode = 401;
                    }
                    else if (ex is NotFoundException)
                    {
                        httpContext.Response.StatusCode = 404;
                    }
                    else if (ex is ConflictException)
                    {
                        httpContext.Response.StatusCode = 409;
                    }
                    else
                    {
                        httpContext.Response.StatusCode = 500;
                    }
                    httpContext.Response.ContentType = "text/plain";
                    await httpContext.Response.WriteAsync(ex.ToString());
                }
            }
        }
    }
}
