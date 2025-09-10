# Petabridge.Phobos.Web

**📖 Full Tutorial**: [Phobos QuickStart - OpenTelemetry for Akka.NET in 5 Minutes](https://phobos.petabridge.com/articles/quickstart.html)

This repository demonstrates how to get OpenTelemetry metrics and traces from your Akka.NET application in minutes using Phobos.

> NOTE: this solution uses the [shared Phobos + Prometheus Akka.Cluster dashboard for Grafana built by Petabridge](https://phobos.petabridge.com/articles/dashboards.html), which you can install in your own application via Grafana Cloud here: https://grafana.com/grafana/dashboards/15637 and here https://grafana.com/grafana/dashboards/15638. The source for these dashboards can be found at https://github.com/petabridge/phobos-dashboards

> This sample has been updated for [Phobos 2.x and OpenTelemetry](https://phobos.petabridge.com/articles/releases/whats-new-in-phobos-2.0.0.html) - if you need access to the old 1.x version of this sample please see https://github.com/petabridge/Petabridge.Phobos.Web/tree/1.x

This project is a ready-made solution for testing [Phobos](https://phobos.petabridge.com/) in a real production environment using the following technologies:

- .NET 8.0
- ASP.NET Core 8.0
- [Akka.Cluster](https://getakka.net/)
- [Prometheus](https://prometheus.io/)
- [Grafana](https://grafana.com/)
- [Docker](https://www.docker.com/)
- [Seq](https://datalust.co/)
- [Azure Table](https://learn.microsoft.com/en-us/azure/storage/tables/)
- and finally, [.NET Aspire](https://github.com/dotnet/aspire)

## Build and Local Deployment
Start by cloning this repository to your local system.

Next - to build this solution you will need to [purchase a Phobos license key](https://phobos.petabridge.com/articles/setup/request.html). They cost $4,000 per year per organization with no node count or seat limitations and comes with a 30 day money-back guarantee.

Once you purchase a [Phobos NuGet keys for your organization](https://phobos.petabridge.com/articles/setup/index.html), you're going to want to open [`NuGet.config`](NuGet.config) and add your key:

```
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <solution>
    <add key="disableSourceControlIntegration" value="true" />
  </solution>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="phobos" value="{your key here}" />
  </packageSources>
</configuration>
```

## Running the Sample

Run the following command from the repository root:

```bash
dotnet run --project src/Petabridge.Phobos.Web.Aspire/Petabridge.Phobos.Web.Aspire.csproj
```

The Aspire dashboard will automatically open in your browser. If it doesn't, check the console output for the dashboard URL.

## Aspire Dashboard

Once the cluster is fully up and running you can explore the application and its associated telemetry via the Aspire dashboard or by opening these URLs:

* [Akka sample node](http://localhost:1880) - generates traffic across the Akka.NET cluster inside the `phobos-web` service.
* [Prometheus](http://localhost:9090) - Prometheus query UI.
* [Grafana](http://localhost:3000) - Grafana metrics. Log in using the username **admin** and the password **admin**. It includes some ready-made dashboards you can use to explore Phobos + OpenTelemetry metrics:
	- [Akka.NET Cluster Metrics](http://localhost:3000/d/8Y4JcEfGk/akka-net-cluster-metrics?orgId=1&refresh=10s) - this is a pre-installed version of our [Akka.NET Cluster + Phobos Metrics (Prometheus Data Source) Dashboard](https://phobos.petabridge.com/articles/dashboards/prometheus-dashboard.html#phobos-2x) on Grafana Cloud, which you can install instantly into your own applications!
	- [ASP.NET Core Metrics](http://localhost:3000/d/ggsijSPZz/asp-net-core-metrics?orgId=1)
* [Seq](http://localhost:8988) - Seq log aggregation.

There's many more metrics exported by Phobos that you can use to create your own dashboards or extend the existing ones - you can view all of them by going to [http://localhost:1880/metrics](http://localhost:1880/metrics)

### Stopping the Cluster

When you're done exploring this sample, you can stop the cluster by pressing Ctrl-C if you're running the sample in console, or stopping all running processes from inside the IDE.

---

© 2025 Petabridge®

All rights reserved

Built with ♥ by [Petabridge](https://petabridge.com/)
