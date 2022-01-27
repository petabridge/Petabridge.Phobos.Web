// -----------------------------------------------------------------------
// <copyright file="Startup.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Phobos.Actor;
using Phobos.Actor.Configuration;

namespace Petabridge.Phobos.Web
{
    public sealed class FilterProcessor : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            // filter out unwanted Activities here
            if (data.OperationName.Contains("/ready"))
                return;
            if (data.OperationName.Contains("HTTP POST"))
                return;
            base.OnEnd(data);
        }
    }

    public class Startup
    {
        /// <summary>
        ///     Name of the <see cref="Environment" /> variable used to direct Phobos' Jaeger
        ///     output.
        ///     See https://github.com/jaegertracing/jaeger-client-csharp for details.
        /// </summary>
        public const string JaegerAgentHostEnvironmentVar = "JAEGER_AGENT_HOST";

        public const string JaegerEndpointEnvironmentVar = "JAEGER_ENDPOINT";

        public const string JaegerAgentPortEnvironmentVar = "JAEGER_AGENT_PORT";

        public const int DefaultJaegerAgentPort = 6832;
        
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
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    //.AddProcessor(new FilterProcessor())
                    .AddJaegerExporter(opt =>
                    {
                        opt.AgentHost = Environment.GetEnvironmentVariable(JaegerAgentHostEnvironmentVar);
                    });
            });

            services.AddOpenTelemetryMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(resource)
                    .AddPhobosInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation()
                    .AddPrometheusExporter(opt =>
                    {
                    });
            });

            // sets up Akka.NET
            ConfigureAkka(services);
        }

        public static void ConfigureAkka(IServiceCollection services)
        {
            services.AddSingleton(sp =>
            {
                var metrics = sp.GetRequiredService<MeterProvider>();
                var tracer = sp.GetRequiredService<TracerProvider>();

                var config = ConfigurationFactory.ParseString(File.ReadAllText("app.conf"))
                    .BootstrapFromDocker()
                    .UseSerilog();

                var phobosSetup = PhobosSetup.Create(new PhobosConfigBuilder()
                        .WithMetrics(m =>
                            m.SetMetricsRoot(metrics)) // binds Phobos to same IMetricsRoot as ASP.NET Core
                        .WithTracing(t => t.SetTracer(tracer))) // binds Phobos to same tracer as ASP.NET Core
                    .WithSetup(BootstrapSetup.Create()
                        .WithConfig(config) // passes in the HOCON for Akka.NET to the ActorSystem
                        .WithActorRefProvider(PhobosProviderSelection
                            .Cluster)); // last line activates Phobos inside Akka.NET

                var sys = ActorSystem.Create("ClusterSys", phobosSetup);

                // create actor "container" and bind it to DI, so it can be used by ASP.NET Core
                return new AkkaActors(sys);
            });

            // this will manage Akka.NET lifecycle
            services.AddHostedService<AkkaService>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment()) app.UseDeveloperExceptionPage();

            // per https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.Prometheus/README.md
            app.UseRouting();
            app.UseOpenTelemetryPrometheusScrapingEndpoint();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });

            app.UseEndpoints(endpoints =>
            {
                var tracer = endpoints.ServiceProvider.GetService<TracerProvider>().GetTracer("Petabridge.Phobos.Web");
                var actors = endpoints.ServiceProvider.GetService<AkkaActors>();
                endpoints.MapGet("/", async context =>
                {
                    using (var s = tracer.StartActiveSpan("Cluster.Ask"))
                    {
                        // router actor will deliver message randomly to someone in cluster
                        var resp = await actors.RouterForwarderActor.Ask<string>($"hit from {context.TraceIdentifier}",
                            TimeSpan.FromSeconds(5));
                        await context.Response.WriteAsync(resp);
                    }
                });
            });
        }
    }
}