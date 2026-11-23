using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using po;
using System.Text.Json.Serialization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
IServiceCollection services = builder.Services;

_ = services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter())
    );

_ = services.AddLogging(options => options.AddConsole());
_ = services.AddApplicationInsightsTelemetry();

_ = services.AddPoConfig(builder.Configuration);

_ = services.AddDbContext<po.DataAccess.PoContext>((provider, options) => options
      .UseSqlServer(
          provider.GetRequiredService<IOptions<po.Options.Sql>>().Value.ConnectionString,
          sqloptions => sqloptions
              .EnableRetryOnFailure()));
_ = services.AddSingleton<po.DataAccess.IPoStorage, po.DataAccess.PoLocalStorage>();
_ = services.AddSingleton<po.Services.Po.PoBlobSync>();
_ = services.AddSingleton<po.Services.Po.PoImageService>();
_ = services.AddSingleton<po.Services.Po.PoScheduler>();
_ = services.AddSingleton<po.Services.Po.PoStatusReport>();

_ = services.AddSingleton<po.Utilities.Delays>();
_ = services.AddSingleton<po.Utilities.Sentinels>();

_ = services.AddSingleton<po.Chat.ChatRouter>();
_ = services.AddSingleton<po.Commands.CommandDispatcher>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.CommandList>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.ScheduleMessage>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.PoConfigure>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.PoShow>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.PoRandom>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.PoTimer>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.PoClear>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.PoReset>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.PoStatus>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.NukeMessages>();
_ = services.AddSingleton<po.Commands.ICommand, po.Commands.NukeBotMessages>();

_ = services.AddHttpClient<po.Platforms.Mattermost.MattermostApiClient>();
_ = services.AddSingleton<po.Platforms.Mattermost.MattermostPlatform>();
_ = services.AddSingleton<po.Chat.IChatPlatform>(provider => provider.GetRequiredService<po.Platforms.Mattermost.MattermostPlatform>());
_ = services.AddHostedService(provider => provider.GetRequiredService<po.Platforms.Mattermost.MattermostPlatform>());
_ = services.AddSingleton<po.Platforms.Mattermost.MattermostCommandTokens>();
_ = services.AddHostedService<po.Platforms.Mattermost.MattermostCommandRegistrar>();
_ = services.AddHostedService<po.Platforms.Mattermost.MattermostChannelCleanup>();

_ = services.AddHostedService<po.Services.MigrationService>();
_ = services.AddHostedService<po.Services.RestartNotificationService>();

_ = services.AddHostedService<po.Services.Background.RandomMessageBackgroundService>();
_ = services.AddHostedService<po.Services.Background.ScheduledBlobBackgroundService>();
_ = services.AddHostedService<po.Services.Background.ScheduledMessageBackgroundService>();
_ = services.AddHostedService<po.Services.Background.SyncBlobMetadataBackgroundService>();

WebApplication app = builder.Build();

_ = app.UseDeveloperExceptionPage();
_ = app.UseRouting();

_ = app.UseMiddleware<po.Middleware.ExceptionToStatusCodeMiddleware>();

string assemblyFileVersion = ThisAssembly.AssemblyInformationalVersion;
_ = app.Use(
    async (context, next) =>
    {
        context.Response.Headers["po-tcp-wtf-server-version"] = assemblyFileVersion;
        await next.Invoke();
    });

_ = app.MapControllers();

app.Run();
