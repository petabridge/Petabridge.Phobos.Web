// -----------------------------------------------------------------------
// <copyright file="SerilogBootstrapper.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Net;
using System.Reflection;
using Akka.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Petabridge.Phobos.Web
{
    /// <summary>
    ///     Used to configure and install Serilog for semantic logging for both
    ///     ASP.NET and Akka.NET
    /// </summary>
    public static class SerilogBootstrapper
    {
        private const string ServiceNameProperty = "SERVICE_NAME";
        private const string ReplicaNameProperty = "REPLICA_NAME";
        private const string EnvironmentProperty = "ENVIRONMENT";

        public static readonly Config SerilogConfig =
            @"akka.loggers =[""Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog""]";

        static SerilogBootstrapper()
        {
        }

        public static WebApplicationBuilder ConfigureSerilogLogging(this WebApplicationBuilder b)
        {
            var loggerConfiguration = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithProperty(ReplicaNameProperty, GetServiceName(b.Configuration))
                .Enrich.WithProperty(EnvironmentProperty, b.Environment.EnvironmentName)
                .Enrich.WithProperty(ServiceNameProperty, Assembly.GetEntryAssembly()?.GetName().Name)
                .WriteTo.Console(
                    outputTemplate:
                    "[{REPLICA_NAME}][{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}",
                    theme: AnsiConsoleTheme.Literate)
                .Filter.ByExcluding(HealthChecksNormalEvents); // Do not want lots of health check info logs in console
                
            var seqConnectionString = b.Configuration.GetConnectionString("seq");
                
            // enable Seq, if available
            if(!string.IsNullOrEmpty(seqConnectionString))
                loggerConfiguration = loggerConfiguration
                    .WriteTo.Seq(seqConnectionString)
                    .Filter.ByExcluding(HealthChecksNormalEvents); // Do not want lots of health check info logs in Seq

            // Configure Serilog
            Log.Logger = loggerConfiguration.CreateLogger();
            
            b.Logging.ClearProviders();
            b.Logging.AddConsole();
            b.Logging.AddSerilog();
            b.Logging.AddEventSourceLogger();

            return b;
        }

        private static string GetServiceName(IConfiguration configuration)
        {
            var serviceName = configuration.GetValue<string>("OTEL_SERVICE_NAME");

            string instanceId = null;
            
            var resourceAttributes = configuration.GetValue<string>("OTEL_RESOURCE_ATTRIBUTES");
            if (resourceAttributes is not null)
            {
                var attributes = resourceAttributes.Split('=', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var index = Array.IndexOf(attributes, "service.instance.id");
                if(index != -1 && index < attributes.Length - 2)
                    instanceId = attributes[index + 1];
            }

            if (serviceName is not null && instanceId is not null)
                return $"{serviceName}-{instanceId}";
            if (serviceName is not null)
                return serviceName;
            if (instanceId is not null)
                return instanceId;
            return Dns.GetHostName();
        }

        private static bool HealthChecksNormalEvents(LogEvent ev)
        {
            var healthCheckRequest = ev.Properties.ContainsKey("RequestPath") &&
                                     (ev.Properties["RequestPath"].ToString() == "\"/env\"" ||
                                      ev.Properties["RequestPath"].ToString() == "\"/ready\"");

            var metricsLog = ev.Properties.ContainsKey("kubernetes_annotations_prometheus.io_path") &&
                             ev.Properties["kubernetes_annotations_prometheus.io_path"].ToString() == "\"/metrics\"";

            return ev.Level < LogEventLevel.Warning && (healthCheckRequest || metricsLog);
        }

        // We have EF normal logs disabled on appsettings.json level
        /*private static bool EntityFrameworkNormalEvents(LogEvent ev)
        {
            return ev.Level < LogEventLevel.Warning && 
                   ev.Properties.ContainsKey("SourceContext") && 
                   ev.Properties["SourceContext"].ToString().Contains("Microsoft.EntityFrameworkCore");
        }*/
    }
}