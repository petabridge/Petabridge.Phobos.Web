# Petabridge.Phobos.Web
_This repository contains the source code for the [Phobos Quickstart Tutorial, which you can read here](https://phobos.petabridge.com/articles/quickstart.html)_.

> NOTE: this solution uses the [shared Phobos + Prometheus Akka.Cluster dashboard for Grafana built by Petabridge](https://phobos.petabridge.com/articles/dashboards/prometheus-dashboard.html#phobos-2x), which you can install in your own application via Grafana Cloud here: https://grafana.com/grafana/dashboards/15637 and here https://grafana.com/grafana/dashboards/15638

> This sample has been updated for [Phobos 2.x and OpenTelemetry](https://phobos.petabridge.com/articles/releases/whats-new-in-phobos-2.0.0.html) - if you need access to the old 1.x version of this sample please see https://github.com/petabridge/Petabridge.Phobos.Web/tree/1.x

This project is a ready-made solution for testing [Phobos](https://phobos.petabridge.com/) in a real production environment using the following technologies:

- .NET 8.0
- ASP.NET Core 8.0
- [Akka.Cluster](https://getakka.net/)
- [Prometheus](https://prometheus.io/)
- [Jaeger Tracing](https://www.jaegertracing.io/)
- [Grafana](https://grafana.com/)
- [Docker](https://www.docker.com/)
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

## Running From Your IDE

To run the sample from your IDE (Visual Studio 2022, Visual Studio Code, or JetBrains Rider), simply run the `Petabridge.Phobos.Web.Aspire: https` launch profile. The Aspire dashboard should automatically launch in a browser window. If it is blocked, you can manually open the Aspire dashboard [here](http://localhost:15266).

## Running From Console

Run the following command on the prompt inside the solution folder:

```
PS> .\run.cmd
```

Once the project is running, you can manually open the Aspire dashboard [here](http://localhost:15266).

## Aspire Dashboard

Once the cluster is fully up and running you can explore the application and its associated telemetry via the Aspire dashboard:

* __phobos-web-*__ - Akka cluster nodes, generates traffic across the Akka.NET cluster inside the `phobos-web` service.
* __prometheus__ - Prometheus query UI.
* __grafana__ - Grafana metrics. Log in using the username **admin** and the password **admin**. It includes some ready-made dashboards you can use to explore Phobos + OpenTelemetry metrics:
	- [Akka.NET Cluster Metrics](http://localhost:3000/d/8Y4JcEfGk/akka-net-cluster-metrics?orgId=1&refresh=10s) - this is a pre-installed version of our [Akka.NET Cluster + Phobos Metrics (Prometheus Data Source) Dashboard](https://phobos.petabridge.com/articles/dashboards/prometheus-dashboard.html#phobos-2x) on Grafana Cloud, which you can install instantly into your own applications!
	- [ASP.NET Core Metrics](http://localhost:3000/d/ggsijSPZz/asp-net-core-metrics?orgId=1)
* __seq__ - Seq log aggregation.

There's many more metrics exported by Phobos that you can use to create your own dashboards or extend the existing ones - you can view all of them by going to [http://localhost:1880/metrics](http://localhost:1880/metrics)

### Stopping the Cluster

When you're done exploring this sample, you can stop the cluster by pressing Ctrl-C if you're running the sample in console, or stopping all running processes from inside the IDE. 

#### Other Build Script Options
This project supports a wide variety of commands, all of which can be listed via:

**Windows**
```
c:\> build.cmd help
```

**Linux / OS X**
```
c:\> build.sh help
```

However, please see this readme for full details.

### Summary

* `build.[cmd|sh] all` - runs the entire build system minus documentation: `NBench`, `Tests`, and `Nuget`.
* `build.[cmd|sh] buildrelease` - compiles the solution in `Release` mode.
* `build.[cmd|sh] tests` - compiles the solution in `Release` mode and runs the unit test suite (all projects that end with the `.Tests.csproj` suffix). All of the output will be published to the `./TestResults` folder.
* `build.[cmd|sh] nbench` - compiles the solution in `Release` mode and runs the [NBench](https://nbench.io/) performance test suite (all projects that end with the `.Tests.Performance.csproj` suffix). All of the output will be published to the `./PerfResults` folder.
* `build.[cmd|sh] nuget` - compiles the solution in `Release` mode and creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set and uses the version number from `RELEASE_NOTES.md`.
* `build.[cmd|sh] nuget nugetprerelease=dev` - compiles the solution in `Release` mode and creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set - but in this instance all projects will have a `VersionSuffix` of `-beta{DateTime.UtcNow.Ticks}`. It's typically used for publishing nightly releases.
* `build.[cmd|sh] nuget SignClientUser=$(signingUsername) SignClientSecret=$(signingPassword)` - compiles the solution in `Release` modem creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set using the version number from `RELEASE_NOTES.md`, and then signs those packages using the SignClient data below.
* `build.[cmd|sh] nuget SignClientUser=$(signingUsername) SignClientSecret=$(signingPassword) nugetpublishurl=$(nugetUrl) nugetkey=$(nugetKey)` - compiles the solution in `Release` modem creates Nuget packages from any project that does not have `<IsPackable>false</IsPackable>` set using the version number from `RELEASE_NOTES.md`, signs those packages using the SignClient data below, and then publishes those packages to the `$(nugetUrl)` using NuGet key `$(nugetKey)`.
* `build.[cmd|sh] DocFx` - compiles the solution in `Release` mode and then uses [DocFx](http://dotnet.github.io/docfx/) to generate website documentation inside the `./docs/_site` folder. Use the `./serve-docs.cmd` on Windows to preview the documentation.

This build script is powered by [FAKE](https://fake.build/); please see their API documentation should you need to make any changes to the [`build.fsx`](build.fsx) file.

### Release Notes, Version Numbers, Etc
This project will automatically populate its release notes in all of its modules via the entries written inside [`RELEASE_NOTES.md`](RELEASE_NOTES.md) and will automatically update the versions of all assemblies and NuGet packages via the metadata included inside [`common.props`](src/common.props).

**RELEASE_NOTES.md**
```
#### 0.1.0 October 05 2019 ####
First release
```

In this instance, the NuGet and assembly version will be `0.1.0` based on what's available at the top of the `RELEASE_NOTES.md` file.

**RELEASE_NOTES.md**
```
#### 0.1.0-beta1 October 05 2019 ####
First release
```

But in this case the NuGet and assembly version will be `0.1.0-beta1`.

If you add any new projects to the solution created with this template, be sure to add the following line to each one of them in order to ensure that you can take advantage of `common.props` for standardization purposes:

```
<Import Project="..\common.props" />
```
