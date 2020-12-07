// -----------------------------------------------------------------------
// <copyright file="Startup.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2020 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Configuration;
using App.Metrics;
using App.Metrics.Formatters.Prometheus;
using Datadog.Trace.OpenTracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTracing;
using Phobos.Actor;
using Phobos.Actor.Configuration;
using Phobos.Tracing;
using Phobos.Tracing.Scopes;

namespace Petabridge.Phobos.Web
{
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
            // enables OpenTracing for ASP.NET Core
            services.AddOpenTracing(o =>
            {
                o.ConfigureAspNetCore(a =>
                    a.Hosting.OperationNameResolver = context => $"{context.Request.Method} {context.Request.Path}");
                o.AddCoreFx();
            });

            // sets up Prometheus + DataDog + ASP.NET Core metrics
            ConfigureAppMetrics(services);

            // sets up DataDog tracing
            ConfigureDataDogTracing(services);

            // sets up Akka.NET
            ConfigureAkka(services);
        }

        public static void ConfigureAppMetrics(IServiceCollection services)
        {
            services.AddMetricsTrackingMiddleware();
            services.AddMetrics(b =>
            {
                var metrics = b.Configuration.Configure(o =>
                    {
                        o.GlobalTags.Add("host", Dns.GetHostName());
                        o.DefaultContextLabel = "akka.net";
                        o.Enabled = true;
                        o.ReportingEnabled = true;
                    })
                    .OutputMetrics.AsPrometheusPlainText()
                    .Report.ToDatadogHttp(options => {
                        options.Datadog.BaseUri = new Uri($"http://{Environment.GetEnvironmentVariable("DD_AGENT_HOST")}");
                        options.HttpPolicy.BackoffPeriod = TimeSpan.FromSeconds(30);
                        options.HttpPolicy.FailuresBeforeBackoff = 5;
                        options.HttpPolicy.Timeout = TimeSpan.FromSeconds(10);
                        options.FlushInterval = TimeSpan.FromSeconds(20);
                    })
                    .Build();

                services.AddMetricsEndpoints(ep =>
                {
                    ep.MetricsTextEndpointOutputFormatter = metrics.OutputMetricsFormatters
                        .OfType<MetricsPrometheusTextOutputFormatter>().First();
                    ep.MetricsEndpointOutputFormatter = metrics.OutputMetricsFormatters
                        .OfType<MetricsPrometheusTextOutputFormatter>().First();
                });
            });
            services.AddMetricsReportingHostedService();
        }

        public static void ConfigureDataDogTracing(IServiceCollection services)
        {
            // Add DataDog Tracing
            services.AddSingleton<ITracer>(sp =>
            {
                return OpenTracingTracerFactory.CreateTracer().WithScopeManager(new ActorScopeManager());
            });
        }

        public static void ConfigureAkka(IServiceCollection services)
        {
            services.AddSingleton(sp =>
            {
                var metrics = sp.GetRequiredService<IMetricsRoot>();
                var tracer = sp.GetRequiredService<ITracer>();

                var config = ConfigurationFactory.ParseString(File.ReadAllText("app.conf")).BootstrapFromDocker();

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

            app.UseRouting();

            // enable App.Metrics routes
            app.UseMetricsAllMiddleware();
            app.UseMetricsAllEndpoints();

            app.UseEndpoints(endpoints =>
            {
                var actors = endpoints.ServiceProvider.GetService<AkkaActors>();
                var tracer = endpoints.ServiceProvider.GetService<ITracer>();
                endpoints.MapGet("/", async context =>
                {
                    using (var s = tracer.BuildSpan("Cluster.Ask").StartActive())
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