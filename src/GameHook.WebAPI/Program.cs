using GameHook.Domain;
using GameHook.Domain.Drivers;
using GameHook.Domain.Infrastructure;
using GameHook.Domain.Interfaces;
using GameHook.WebAPI;
using GameHook.WebAPI.Hubs;
using Hellang.Middleware.ProblemDetails;
using Hellang.Middleware.ProblemDetails.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text.Json.Serialization;

static class EmbededResources
{
    public static Stream appsettings_json => ApiHelper.GetEmbeddedResourceStream("GameHook.WebAPI.appsettings.json");
    public static Stream index_html => ApiHelper.GetEmbeddedResourceStream("GameHook.WebAPI.wwwroot.index.html");
    public static Stream favicon_ico => ApiHelper.GetEmbeddedResourceStream("GameHook.WebAPI.wwwroot.favicon.ico");
    public static Stream site_css => ApiHelper.GetEmbeddedResourceStream("GameHook.WebAPI.wwwroot.site.css");
    public static Stream dist_gameHookMapperClient_js => ApiHelper.GetEmbeddedResourceStream("GameHook.WebAPI.wwwroot.dist.gameHookMapperClient.js");
} 

public class Program
{
    static void Main()
    {
        Start().Wait();
    }

    public static async Task Start()
    {
        if (BuildEnvironment.IsReleaseBuild)
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        }
        else
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        }

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());

            // Remove default logging providers
            builder.Logging.ClearProviders();

            // Register Serilog
            builder.Logging.AddSerilog();

            // Add a custom appsettings.user.json file if
            // the user wants to override their settings.
            builder.Configuration.AddJsonStream(EmbededResources.appsettings_json);
            builder.Configuration.AddJsonFile("appsettings.user.json", true, true);

            builder.Services.AddHttpClient();

            // Add CORS
            builder.Services.AddCors();

            // Add Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(x =>
            {
                x.DocumentFilter<DefaultSwashbuckleFilter>();

                x.EnableAnnotations();

                x.SwaggerDoc("v1", new OpenApiInfo
                {
                    Version = "v1",
                    Title = "GameHook API",
                    Contact = new OpenApiContact
                    {
                        Name = "GameHook Team",
                        Url = new Uri("https://github.com/gamehook-io/gamehook")
                    },
                    License = new OpenApiLicense
                    {
                        Name = "GNU Affero General Public License v3.0",
                        Url = new Uri("https://github.com/gamehook-io/gamehook/blob/main/LICENSE.txt")
                    }
                });
            });

            // Add Web API
            builder.Services
                .AddControllers()
                .AddApplicationPart(typeof(Program).Assembly)
                .AddControllersAsServices()
                .AddProblemDetailsConventions()
                .AddJsonOptions(x => x.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

            // Add SignalR
            builder.Services.AddSignalR();

            // Add ProblemDetails
            builder.Services.AddProblemDetails((options) =>
            {
                options.ShouldLogUnhandledException = (ctx, e, d) => true;

                options.IncludeExceptionDetails = (ctx, ex) =>
                {
                    var env = ctx.RequestServices.GetRequiredService<IHostEnvironment>();
                    return env.IsDevelopment();
                };
            });

            // Register application classes.
            builder.Services.AddSingleton<DriverOptions>();
            builder.Services.AddSingleton<IMapperFilesystemProvider, MapperFilesystemProvider>();
            builder.Services.AddSingleton<IMapperUpdateManager, MapperUpdateManager>();
            builder.Services.AddSingleton<IGameHookDriver, RetroArchUdpPollingDriver>();
            builder.Services.AddSingleton<IGameHookContainerFactory, GameHookContainerFactory>();
            builder.Services.AddSingleton<IClientNotifier, ClientNotifier>();

            // Build and run.
            var app = builder.Build();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            app.UseCors(x =>
            {
                x.SetIsOriginAllowed(x => true);
                x.AllowAnyMethod();
                x.AllowAnyHeader();
                x.AllowCredentials();
            });

            // Use Swagger
            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseSerilogRequestLogging();
            app.UseProblemDetails();

            app.MapGet("/", () =>
            {
                return Results.File(EmbededResources.index_html, contentType: "text/html");
            });

            app.MapGet("/favicon.ico", () =>
            {
                return Results.File(EmbededResources.favicon_ico, contentType: "image/x-icon");
            });

            app.MapGet("/site.css", () =>
            {
                return Results.File(EmbededResources.site_css, contentType: "text/css");
            });

            app.MapGet("/dist/gameHookMapperClient.js", () =>
            {
                return Results.File(EmbededResources.dist_gameHookMapperClient_js, contentType: "application/javascript");
            });

            app.MapControllers();
            app.MapHub<UpdateHub>("/updates");

            if (BuildEnvironment.IsReleaseBuild == false)
            {
                logger.LogWarning($"Running build in release mode: {BuildEnvironment.ReleaseMode}");
            }

            logger.LogInformation($"Starting GameHook version {BuildEnvironment.AssemblyProductVersion}.");

            var mapperUpdateManager = app.Services.GetRequiredService<IMapperUpdateManager>();
            await mapperUpdateManager.CheckForUpdates();

            await app.StartAsync();

            logger.LogInformation($"GameHook is now online. Accessible via: {string.Join(", ", app.Urls)}");
            logger.LogInformation("Navigate to the above URL in order to access the Web UI.");

            await app.WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception");
        }
        finally
        {
            Log.Information("Terminating application.");
            Log.CloseAndFlush();

            Console.WriteLine("Application has exited. Press any key to continue.");
            Console.ReadLine();
        }
    }
}