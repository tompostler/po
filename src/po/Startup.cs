﻿using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace po
{
    public sealed class Startup
    {
        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMemoryCache(options =>
            {
                options.CompactionPercentage = .25;
                options.SizeLimit = 1024;
            });
            services.AddControllers()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter())
                );

            services.AddLogging(options => options.AddConsole());
            services.AddApplicationInsightsTelemetry();

            services.Configure<Options.Discord>(this.Configuration.GetSection(nameof(Options.Discord)));
            services.Configure<Options.Sql>(this.Configuration.GetSection(nameof(Options.Sql)));

            services.AddDbContext<DataAccess.PoContext>((provider, options) => options
                .UseSqlServer(
                    provider.GetRequiredService<IOptions<Options.Sql>>().Value.ConnectionString,
                    sqloptions => sqloptions
                        .EnableRetryOnFailure()));

            services.AddHostedService<Services.MigrationService>();
            services.AddHostedService<Services.BotService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseDeveloperExceptionPage();
            app.UseHttpsRedirection();
            app.UseRouting();

            app.UseMiddleware<Middleware.ExceptionToStatusCodeMiddleware>();

            var assemblyFileVersion = FileVersionInfo.GetVersionInfo(typeof(Startup).Assembly.Location).ProductVersion;
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Add("po-tcp-wtf-server-version", assemblyFileVersion);
                await next.Invoke();
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
