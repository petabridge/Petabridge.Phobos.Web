// -----------------------------------------------------------------------
// <copyright file="Startup.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Reflection;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Cluster.Hosting;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;
using Phobos.Actor;
using Phobos.Hosting;

namespace Petabridge.Phobos.Web
{
    public class Startup
    {
        public const string OtlpEndpointEnv = "OTEL_EXPORTER_OTLP_ENDPOINT";

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // Prometheus exporter won't work without this
            services.AddControllers();

            var resource = ResourceBuilder.CreateDefault()
                .AddService(Assembly.GetEntryAssembly().GetName().Name, serviceInstanceId: $"{Dns.GetHostName()}");

            // enables OpenTelemetry for ASP.NET / .NET Core
            services.AddOpenTelemetryTracing(builder =>
            {
                builder
                    .SetResourceBuilder(resource)
                    .AddPhobosInstrumentation()
                    .AddSource("Petabridge.Phobos.Web")
                    .AddHttpClientInstrumentation(options =>
                    {
                        // don't trace HTTP output to Seq
                        options.Filter = httpRequestMessage => !httpRequestMessage.RequestUri.Host.Contains("seq");
                    })
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = context => !context.Request.Path.StartsWithSegments("/metrics");
                    })
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(Environment.GetEnvironmentVariable(OtlpEndpointEnv));
                        options.Protocol = OtlpExportProtocol.Grpc;
                    });
            });

            services.AddOpenTelemetryMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(resource)
                    .AddPhobosInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(Environment.GetEnvironmentVariable(OtlpEndpointEnv));
                        options.Protocol = OtlpExportProtocol.Grpc;
                    });
            });

            // sets up Akka.NET
            ConfigureAkka(services);
        }

        public static void ConfigureAkka(IServiceCollection services)
        {
            services.AddAkka("ClusterSys", (builder, provider) =>
            {
                // use our legacy app.conf file
                var config = ConfigurationFactory.ParseString(File.ReadAllText("app.conf"))
                    .BootstrapFromDocker()
                    .UseSerilog();

                builder.AddHocon(config)
                    .WithClustering(new ClusterOptions { Roles = new[] { "console" } })
                    .WithPhobos(AkkaRunMode.AkkaCluster) // enable Phobos
                    .StartActors((system, registry) =>
                    {
                        var consoleActor = system.ActorOf(Props.Create(() => new ConsoleActor()), "console");
                        var routerActor = system.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "echo");
                        var routerForwarderActor =
                            system.ActorOf(Props.Create(() => new RouterForwarderActor(routerActor)), "fwd");
                        registry.TryRegister<RouterForwarderActor>(routerForwarderActor);
                    })
                    .StartActors((system, registry) =>
                    {
                        // start https://cmd.petabridge.com/ for diagnostics and profit
                        var pbm = PetabridgeCmd.Get(system); // start Pbm
                        pbm.RegisterCommandPalette(ClusterCommands.Instance);
                        pbm.RegisterCommandPalette(new RemoteCommands());
                        pbm.Start(); // begin listening for PBM management commands
                    });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment()) app.UseDeveloperExceptionPage();

            app.UseRouting();
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
}