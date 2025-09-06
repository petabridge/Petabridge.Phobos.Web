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
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
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
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const string MetricsEndpointPath = "/metrics";
    
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Run();
    }

    private static WebApplication CreateHostBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddEventSourceLogger();
        
        // Log OTEL configuration for debugging
        var aspireEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var customEndpoint = builder.Configuration["CUSTOM_OTEL_COLLECTOR_ENDPOINT"];
        Console.WriteLine($"[STARTUP] OTEL_EXPORTER_OTLP_ENDPOINT = {aspireEndpoint ?? "NULL"}");
        Console.WriteLine($"[STARTUP] CUSTOM_OTEL_COLLECTOR_ENDPOINT = {customEndpoint ?? "NULL"}");
        
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });
        
        ConfigureServices(builder.Services, builder.Configuration);
        //builder.AddSeqEndpoint(connectionName: "seq");

        var app = builder.Build();
        Configure(app, app.Environment);
        MapDefaultEndpoints(app);
            
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
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Prometheus exporter won't work without this
        services.AddControllers();
        
        // add health checks
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        
        // add service discovery
        services.AddServiceDiscovery();
        
        // add background service
        services.AddHostedService<PeriodicMessageService>();

        // enables OpenTelemetry for ASP.NET / .NET Core
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddPhobosInstrumentation() // enables Phobos tracing instrumentation
                    .AddSource("Petabridge.Phobos.Web")
                    .AddAspNetCoreInstrumentation(tracing =>
                        {
                            tracing.Filter = context =>
                                // Exclude metrics scraping requests from tracing
                                !context.Request.Path.StartsWithSegments(MetricsEndpointPath)
                                // Exclude health check requests from tracing
                                && !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                                && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath);
                        }
                    )
                    .AddHttpClientInstrumentation(options =>
                    {
                        // don't trace HTTP output to Seq
                        options.FilterHttpRequestMessage = httpRequestMessage => !httpRequestMessage.RequestUri?.Host.Contains("seq") ?? false;
                    });
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddPhobosInstrumentation() // enables Phobos metrics instrumentation
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .UseOtlpExporter(OpenTelemetry.Exporter.OtlpExportProtocol.Grpc, GetOtlpEndpoint(configuration));

        // sets up Akka.NET
        ConfigureAkka(services);
    }

    private static Uri GetOtlpEndpoint(IConfiguration configuration)
    {
        var customEndpoint = configuration.GetValue<string>("CUSTOM_OTEL_COLLECTOR_ENDPOINT");
        var defaultEndpoint = configuration.GetValue<string>("OTEL_EXPORTER_OTLP_ENDPOINT");
        
        if (Uri.TryCreate(customEndpoint, UriKind.Absolute, out var uri))
        {
            Console.WriteLine($"[OTEL] Using custom collector endpoint: {customEndpoint}");
            return uri;
        }

        if (Uri.TryCreate(defaultEndpoint, UriKind.Absolute, out uri))
        {
            Console.WriteLine($"[OTEL] Using default collector endpoint: {defaultEndpoint}");
            return uri;
        }

        Console.WriteLine("[OTEL] Using default OTLP exporter configuration");
        return new Uri("http://localhost:4317"); // Default OTLP gRPC endpoint
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
        
        app.UseRouting();
        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });

        app.UseEndpoints(endpoints =>
        {
            var tracer = endpoints.ServiceProvider.GetService<TracerProvider>().GetTracer("Petabridge.Phobos.Web");
            var actors = endpoints.ServiceProvider.GetService<ActorRegistry>();
            endpoints.MapGet("/", async context =>
            {
                // fetch actor references from the registry
                var routerForwarderActor = await actors.GetAsync<RouterForwarderActor>();
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
    
    public static WebApplication MapDefaultEndpoints(WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }
}