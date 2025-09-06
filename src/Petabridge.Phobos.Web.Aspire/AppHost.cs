using Microsoft.Extensions.Hosting;
using Petabridge.Phobos.Web.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

#region Logging Setup

var seq = builder.AddSeq("seq", 8988);
    // Uncomment to have Seq persisted between sessions
    // .ExcludeFromManifest()
    // .WithDataVolume()
    // .WithLifetime(ContainerLifetime.Persistent);

#endregion

#region Telemetry Setup

var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.2.1")
    .WithBindMount("./prometheus", "/etc/prometheus", isReadOnly: true)
    .WithArgs("--web.enable-otlp-receiver", "--config.file=/etc/prometheus/prometheus.yaml")
    .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "http");

var grafana = builder.AddContainer("grafana", "grafana/grafana")
    .WithBindMount("./grafana/config", "/etc/grafana", isReadOnly: true)
    .WithBindMount("./grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
    .WithEnvironment("PROMETHEUS_ENDPOINT", prometheus.GetEndpoint("http"))
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http")
    .WithLifetime(ContainerLifetime.Session); // Force fresh container each run

var otelCollector = builder.AddOpenTelemetryCollector("otelcollector", "./otel_collector/config.yaml")
    .WithEnvironment("SEQ_ENDPOINT", $"{seq.GetEndpoint("http")}/ingest/otlp")
    .WithEnvironment("PROMETHEUS_ENDPOINT", $"{prometheus.GetEndpoint("http")}/api/v1/otlp");

#endregion

#region Azure Discovery Setup

var azure = builder.AddAzureStorage("azure");

if (builder.Environment.IsDevelopment())
    azure.RunAsEmulator();

var azureTables = azure.AddTables("azure-tables");

#endregion

builder.AddProject<Projects.Petabridge_Phobos_Web>("phobos-web")
    .WithHttpsEndpoint(port: 1881)
    .WithHttpEndpoint(port: 1880)
    .WithEndpoint(name: "remoting", env: "AkkaOptions__Remote__Port")
    .WithEndpoint(name:"management", env: "AkkaOptions__Management__Port")
    .WithEndpoint(name: "pbm", env: "AkkaOptions__Pbm__Port")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithReference(azureTables)
    .WithReference(seq)
    .WaitFor(prometheus)
    .WaitFor(grafana)
    .WaitFor(seq)
    .WaitFor(azureTables)
    .WithReplicas(3);

builder.Build().Run();