using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using po.DiscordImpl;
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
            _ = services.AddControllers()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter())
                );

            _ = services.AddLogging(options => options.AddConsole());
            _ = services.AddApplicationInsightsTelemetry();

            _ = services.AddPoConfig(this.Configuration);

            _ = services.AddDbContext<DataAccess.PoContext>((provider, options) => options
                  .UseSqlServer(
                      provider.GetRequiredService<IOptions<Options.Sql>>().Value.ConnectionString,
                      sqloptions => sqloptions
                          .EnableRetryOnFailure()));
            _ = services.AddSingleton<DataAccess.PoStorage>();

            _ = services.AddSingleton<Utilities.Delays>();
            _ = services.AddSingleton<Utilities.Sentinals>();

            _ = services.AddHostedService<Services.BotService>();
            _ = services.AddHostedService<Services.MigrationService>();
            _ = services.AddHostedService<Services.RestartNotificationService>();

            _ = services.AddHostedService<Services.Background.CleanUpOldMessagesBackgroundService>();
            _ = services.AddHostedService<Services.Background.RandomMessageBackgroundService>();
            _ = services.AddHostedService<Services.Background.ScheduledBlobBackgroundService>();
            _ = services.AddHostedService<Services.Background.SyncBlobMetadataBackgroundService>();

            _ = services.AddDiscordBotSlashCommands();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            _ = app.UseDeveloperExceptionPage();
            _ = app.UseHttpsRedirection();
            _ = app.UseRouting();

            _ = app.UseMiddleware<Middleware.ExceptionToStatusCodeMiddleware>();

            string assemblyFileVersion = FileVersionInfo.GetVersionInfo(typeof(Startup).Assembly.Location).ProductVersion;
            _ = app.Use(
                async (context, next) =>
                {
                    context.Response.Headers.Add("po-tcp-wtf-server-version", assemblyFileVersion);
                    await next.Invoke();
                });

            _ = app.UseEndpoints(endpoints => _ = endpoints.MapControllers());
        }
    }
}
