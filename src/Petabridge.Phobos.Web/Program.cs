// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Cluster.Routing;
using Akka.Discovery.Azure;
using Akka.Hosting;
using Akka.Management;
using Akka.Management.Cluster.Bootstrap;
using Akka.Remote.Hosting;
using Akka.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;
using Phobos.Actor;
using Phobos.Hosting;
using LogLevel = Akka.Event.LogLevel;

namespace DemoPhobos;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Run();
    }

    private static WebApplication CreateHostBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args)
            .AddServiceDefaults();
        
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddEventSourceLogger();
        
        ConfigureServices(builder.Services);
        builder.AddSeqEndpoint(connectionName: "seq");

        var app = builder.Build();
        Configure(app, app.Environment);
        app.MapDefaultEndpoints();
            
        return app;
    }
    
    // OpenTelemetry.Exporter.OpenTelemetryProtocol exporter supports several environment variables
    // that can be used to configure the exporter instance:
    //
    // * OTEL_EXPORTER_OTLP_ENDPOINT 
    // * OTEL_EXPORTER_OTLP_HEADERS
    // * OTEL_EXPORTER_OTLP_TIMEOUT
    // * OTEL_EXPORTER_OTLP_PROTOCOL
    //
    // Please read https://opentelemetry.io/docs/concepts/sdk-configuration/otlp-exporter-configuration/
    // for further information.
    //
    // Note that OTEL_EXPORTER_OTLP_PROTOCOL only supports "grpc" and "http/protobuf"

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    private static void ConfigureServices(IServiceCollection services)
    {
        // Prometheus exporter won't work without this
        services.AddControllers();

        // enables OpenTelemetry for ASP.NET / .NET Core
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddPhobosInstrumentation()
                    .AddSource("Petabridge.Phobos.Web");
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddPhobosInstrumentation()
                    .AddPrometheusExporter(_ => { });
            });

        // sets up Akka.NET
        ConfigureAkka(services);
    }

    private static void ConfigureAkka(IServiceCollection services)
    {
        services.AddAkka("ClusterSys", (builder, provider) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            
            var akkaOptions = new AkkaOptions();
            configuration.GetSection(nameof(AkkaOptions)).Bind(akkaOptions);
            
            var connectionString = configuration.GetConnectionString("azure-tables");
            akkaOptions.Discovery.ConnectionString = connectionString;
            akkaOptions.Discovery.HostName = akkaOptions.Management.HostName ?? "localhost";
            akkaOptions.Discovery.Port = akkaOptions.Management.Port ?? akkaOptions.Management.BindPort;
            
            builder
                .ConfigureLoggers(logger =>
                {
                    logger.LogLevel = LogLevel.InfoLevel;
                    logger.ClearLoggers();
                    logger.AddLoggerFactory();
                })
                .WithRemoting(akkaOptions.Remote)
                .WithClustering(akkaOptions.Cluster)
                .WithAkkaManagement(akkaOptions.Management)
                .WithAzureDiscovery(akkaOptions.Discovery)
                .WithClusterBootstrap(akkaOptions.ClusterBootstrap)
                .WithPhobos(AkkaRunMode.AkkaCluster) // enable Phobos
                .AddPetabridgeCmd(
                    akkaOptions.Pbm,
                    cmd =>
                    {
                        cmd.RegisterCommandPalette(ClusterCommands.Instance);
                        cmd.RegisterCommandPalette(new RemoteCommands());
                    })
                .StartActors((system, registry) =>
                {
                    var consoleActor = system.ActorOf(Props.Create(() => new ConsoleActor()), "console");
                    
                    var routees = new [] { "/user/console" };
                    var routerProps = Props.Empty.WithRouter(
                        new ClusterRouterGroup(
                            new RandomGroup(routees),
                            new ClusterRouterGroupSettings(
                                totalInstances: 1,
                                routeesPaths: routees,
                                allowLocalRoutees: true,
                                useRole: "console")));
                    
                    var routerActor = system.ActorOf(routerProps, "echo");
                    
                    var routerForwarderActor =
                        system.ActorOf(Props.Create(() => new RouterForwarderActor(routerActor)), "fwd");
                    registry.TryRegister<RouterForwarderActor>(routerForwarderActor);
                });
        });
    }

    public static void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment()) 
            app.UseDeveloperExceptionPage();

        // per https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.Prometheus/README.md
        app.UseRouting();
        app.UseOpenTelemetryPrometheusScrapingEndpoint();
        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });

        app.UseEndpoints(endpoints =>
        {
            var tracer = endpoints.ServiceProvider.GetService<TracerProvider>().GetTracer("Petabridge.Phobos.Web");
            var actors = endpoints.ServiceProvider.GetService<ActorRegistry>();
            endpoints.MapGet("/", async context =>
            {
                // fetch actor references from the registry
                var routerForwarderActor = actors.Get<RouterForwarderActor>();
                using (var s = tracer.StartActiveSpan("Cluster.Ask"))
                {
                    // router actor will deliver message randomly to someone in cluster
                    var resp = await routerForwarderActor.Ask<string>($"hit from {context.TraceIdentifier}",
                        TimeSpan.FromSeconds(5));
                    await context.Response.WriteAsync(resp);
                }
            });
        });
    }
}