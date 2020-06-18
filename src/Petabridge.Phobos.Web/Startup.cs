using System;
using System.IO;
using System.Linq;
using System.Net;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Configuration;
using Akka.Event;
using App.Metrics;
using App.Metrics.Formatters.Prometheus;
using Jaeger;
using Jaeger.Reporters;
using Jaeger.Samplers;
using Jaeger.Senders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTracing;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;
using Phobos.Actor;
using Phobos.Actor.Configuration;
using Phobos.Tracing.Scopes;

namespace Petabridge.Phobos.Web
{
    public class Startup
    {
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

            // sets up Prometheus + ASP.NET Core metrics
            ConfigureAppMetrics(services);

            // sets up Jaeger tracing
            ConfigureJaegerTracing(services);

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
                    .Report.ToConsole().Build();

                services.AddMetricsEndpoints(ep =>
                {
                    ep.MetricsTextEndpointOutputFormatter = metrics.OutputMetricsFormatters.OfType<MetricsPrometheusTextOutputFormatter>().First();
                    ep.MetricsEndpointOutputFormatter = metrics.OutputMetricsFormatters.OfType<MetricsPrometheusTextOutputFormatter>().First();
                });
            });
            services.AddMetricsReportingHostedService();
        }

        /// <summary>
        ///     Name of the <see cref="Environment" /> variable used to direct Phobos' Jaeger
        ///     output.
        ///
        ///     See https://github.com/jaegertracing/jaeger-client-csharp for details.
        /// </summary>
        public const string JaegerAgentHostEnvironmentVar = "JAEGER_AGENT_HOST";

        public const string JaegerEndpointEnvironmentVar = "JAEGER_ENDPOINT";

        public const string JaegerAgentPortEnvironmentVar = "JAEGER_AGENT_PORT";
        public const int DefaultJaegerAgentPort = 6832;

        public static void ConfigureJaegerTracing(IServiceCollection services)
        {
            static ISender BuildSender()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(JaegerEndpointEnvironmentVar)))
                {
                    if (!int.TryParse(Environment.GetEnvironmentVariable(JaegerAgentPortEnvironmentVar),
                        out var udpPort))
                    {
                        udpPort = DefaultJaegerAgentPort;
                    }
                    return new UdpSender(Environment.GetEnvironmentVariable(JaegerAgentHostEnvironmentVar) ?? "localhost",
                        udpPort, 0);
                }

                return new HttpSender(Environment.GetEnvironmentVariable(JaegerEndpointEnvironmentVar));
            }

            services.AddSingleton<ITracer>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                var remoteReporter = new RemoteReporter.Builder()
                    .WithLoggerFactory(loggerFactory) // optional, defaults to no logging
                    .WithMaxQueueSize(100)            // optional, defaults to 100
                    .WithFlushInterval(TimeSpan.FromSeconds(1)) // optional, defaults to TimeSpan.FromSeconds(1)
                    .WithSender(BuildSender())   // optional, defaults to UdpSender("localhost", 6831, 0)
                    .Build();

                var sampler = new ConstSampler(false); // keep sampling disabled

                // name the service after the executing assembly
                var tracer = new Tracer.Builder(typeof(Startup).Assembly.GetName().Name)
                    .WithReporter(remoteReporter)
                    .WithSampler(sampler)
                    .WithScopeManager(new ActorScopeManager()); // IMPORTANT: ActorScopeManager needed to properly correlate trace inside Akka.NET

                return tracer.Build();
            });
        }

        public static void ConfigureAkka(IServiceCollection services)
        {
            services.AddSingleton<AkkaActors>(sp =>
            {
                var metrics = sp.GetRequiredService<IMetricsRoot>();
                var tracer = sp.GetRequiredService<ITracer>();

                var config = ConfigurationFactory.ParseString(File.ReadAllText("app.conf")).BootstrapFromDocker();

                var phobosSetup = PhobosSetup.Create(new PhobosConfigBuilder()
                        .WithMetrics(m => m.SetMetricsRoot(metrics)) // binds Phobos to same IMetricsRoot as ASP.NET Core
                        .WithTracing(t => t.SetTracer(tracer))) // binds Phobos to same tracer as ASP.NET Core
                    .WithSetup(BootstrapSetup.Create()
                        .WithConfig(config) // passes in the HOCON for Akka.NET to the ActorSystem
                        .WithActorRefProvider(PhobosProviderSelection.Cluster)); // last line activates Phobos inside Akka.NET

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
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                var actors = endpoints.ServiceProvider.GetService<AkkaActors>();
                endpoints.MapGet("/", async context =>
                {
                    // router actor will deliver message randomly to someone in cluster
                    actors.RouterActor.Tell($"hit from {context.TraceIdentifier}");
                    await context.Response.WriteAsync($"Hello World! Request: {context.TraceIdentifier}");
                });
            });
        }
    }
}
