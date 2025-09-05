// -----------------------------------------------------------------------
// <copyright file="PeriodicMessageService.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DemoPhobos;

public sealed class PeriodicMessageService : BackgroundService
{
    private readonly ILogger<PeriodicMessageService> _logger;
    private readonly ActorRegistry _actorRegistry;
    private readonly ActivitySource _activitySource;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    public PeriodicMessageService(ILogger<PeriodicMessageService> logger, ActorRegistry actorRegistry)
    {
        _logger = logger;
        _actorRegistry = actorRegistry;
        _activitySource = new ActivitySource("Petabridge.Phobos.Web");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PeriodicMessageService starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = _activitySource.StartActivity("PeriodicMessage");
                var routerForwarderActor = await _actorRegistry.GetAsync<RouterForwarderActor>(stoppingToken);
                var message = $"Periodic message at {DateTimeOffset.UtcNow:HH:mm:ss}";

                _logger.LogInformation("Sending periodic message: {Message}", message);
                routerForwarderActor.Tell(message, ActorRefs.NoSender);

                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PeriodicMessageService");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("PeriodicMessageService stopping");
    }

    public override void Dispose()
    {
        _activitySource?.Dispose();
        base.Dispose();
    }
}