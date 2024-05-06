using GameHook.Domain;
using Serilog;

namespace GameHook.WebAPI
{
    public class Program
    {
        public static void Main()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            Environment.SetEnvironmentVariable("SERILOG_LOG_FILE_PATH", BuildEnvironment.LogFilePath);

            try
            {
                // TODO: 12/27/2023 - Remove this at a future date. Logs are now stored within %APPDATA%\GameHook.
                if (File.Exists("GameHook.log"))
                {
                    File.Delete("GameHook.log");
                }
                if (File.Exists("gamehook.log"))
                {
                    File.Delete("gamehook.log");
                }

                if (File.Exists(BuildEnvironment.LogFilePath))
                {
                    File.WriteAllText(BuildEnvironment.LogFilePath, string.Empty);
                }

                Log.Logger = new LoggerConfiguration()
                                    .WriteTo.Console()
                                    .WriteTo.File(BuildEnvironment.LogFilePath)
                                    .CreateBootstrapLogger();

                Log.Logger.Information("log path: " + BuildEnvironment.LogFilePath);
                Host.CreateDefaultBuilder()
                        .ConfigureWebHostDefaults(x => x.UseStartup<Startup>())
                        .ConfigureAppConfiguration(x =>
                        {
                            // Add a custom appsettings.user.json file if
                            // the user wants to override their settings.

                            Log.Logger.Information("Adding: " + EmbededResources.appsettings_json_path);
                            x.AddJsonStream(EmbededResources.appsettings_json);
                            Log.Logger.Information("Adding: " + BuildEnvironment.ConfigurationDirectoryAppsettingsFilePath);
                            x.AddJsonFile(BuildEnvironment.ConfigurationDirectoryAppsettingsFilePath, true, false);
                            Log.Logger.Information("Adding: " + BuildEnvironment.BinaryDirectoryGameHookFilePath);
                            x.AddJsonFile(BuildEnvironment.BinaryDirectoryGameHookFilePath, true, false);
                            Log.Logger.Information("Adding: Environment variables.");
                            x.AddEnvironmentVariables();
                        })
                        .UseSerilog((context, services, configuration) => configuration.ReadFrom.Configuration(context.Configuration))
                        .Build()
                        .Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "GameHook startup failed!");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}