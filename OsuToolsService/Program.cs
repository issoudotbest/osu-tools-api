using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using osu.Framework.Logging;
using osu.Game.Beatmaps.Formats;
using OsuToolsService.Services;

namespace OsuToolsService;

public class Program
{
    public static void Main(string[] args)
    {
        // Disable osu! framework logging noise
        Logger.Enabled = false;

        // Register the legacy difficulty calculator decoder
        LegacyDifficultyCalculatorBeatmapDecoder.Register();

        var socketPath = Environment.GetEnvironmentVariable("GRPC_SOCKET_PATH") ?? "/tmp/osu-tools-calc.sock";

        // Clean up any leftover socket file
        if (File.Exists(socketPath))
            File.Delete(socketPath);

        var builder = WebApplication.CreateBuilder(args);

        // Suppress noisy .NET hosting and request logging
        builder.Logging.AddFilter("Microsoft", Microsoft.Extensions.Logging.LogLevel.Warning);
        builder.Logging.AddFilter("OsuToolsService", Microsoft.Extensions.Logging.LogLevel.Warning);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenUnixSocket(socketPath, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Services.AddGrpc();

        var app = builder.Build();

        app.MapGrpcService<CalculatorService>();

        app.Run();
    }
}
