# Petabridge.Phobos.Web

This project is a ready-made solution for testing [Phobos](https://phobos.petabridge.com/) in a real production environment using the following technologies:

- .NET Core 3.1
- ASP.NET Core 3.1
- [Akka.Cluster](https://getakka.net/)
- [Prometheus](https://prometheus.io/)
- [Jaeger Tracing](https://www.jaegertracing.io/)
- [Grafana](https://grafana.com/)
- [Docker](https://www.docker.com/)
- and finally, [Kubernetes](https://kubernetes.io/)

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

From there, run the following commad on the prompt:

```
PS> build.cmd Docker
```

This will create the Docker images the solution needs to run inside Kubernetes: `petabridge.phobos.web:0.1.0`.

## Working with DataDog
This sample is designed to work with [DataDog Application Performance Monitoring](https://datadoghq.com).

To run this sample, you will need a copy of your DataDog API Key - which will be passed into the local Kubernetes cluster created by this tutorial.

### Deploying the K8s Cluster (with Telemetry Installed)
From there, everything you need to run the solution in Kubernetes is already defined inside the [`k8s/` folder](k8s/) - just run the following command to launch the Phobos-enabled application inside Kubernetes:

```
PS> ./k8s/deployAll.cmd [DataDog API Key]
```

This will spin up a separate Kubernetes namespace, `phobos-web`, and you can view which services are deployed by running the following command:

```
PS> kubectl get all -n phobos-web
```

You should see the following or similar output:

```
NAME                                        READY   STATUS    RESTARTS   AGE
pod/grafana-5f54fd5bf4-wvdgw                1/1     Running   0          11m
pod/jaeger-578558d6f9-2xzdv                 1/1     Running   0          11m
pod/phobos-web-0                            1/1     Running   3          11m
pod/phobos-web-1                            1/1     Running   2          10m
pod/phobos-web-2                            1/1     Running   0          9m54s
pod/prometheus-deployment-c6d99b8b9-28tmq   1/1     Running   0          11m

NAME                            TYPE           CLUSTER-IP       EXTERNAL-IP   PORT(S)                               AGE
service/grafana-ip-service      LoadBalancer   10.105.46.6      localhost     3000:31641/TCP                        11m
service/jaeger-agent            ClusterIP      None             <none>        5775/UDP,6831/UDP,6832/UDP,5778/TCP   11m
service/jaeger-collector        ClusterIP      10.109.248.20    <none>        14267/TCP,14268/TCP,9411/TCP          11m
service/jaeger-query            LoadBalancer   10.109.204.203   localhost     16686:30911/TCP                       11m
service/phobos-web              ClusterIP      None             <none>        4055/TCP                              11m
service/phobos-webapi           LoadBalancer   10.103.247.68    localhost     1880:30424/TCP                        11m
service/prometheus-ip-service   LoadBalancer   10.101.119.120   localhost     9090:31698/TCP                        11m
service/zipkin                  ClusterIP      None             <none>        9411/TCP                              11m

NAME                                    READY   UP-TO-DATE   AVAILABLE   AGE
deployment.apps/grafana                 1/1     1            1           11m
deployment.apps/jaeger                  1/1     1            1           11m
deployment.apps/prometheus-deployment   1/1     1            1           11m

NAME                                              DESIRED   CURRENT   READY   AGE
replicaset.apps/grafana-5f54fd5bf4                1         1         1       11m
replicaset.apps/jaeger-578558d6f9                 1         1         1       11m
replicaset.apps/prometheus-deployment-c6d99b8b9   1         1         1       11m
```

> NOTE: the restarts from the `phobos-web-*` pods come from calling `Dns.GetHostName()` prior to the local K8s service allocating its hostnames. Nothing to worry about - it'll resolve itself in a few moments.

Once the cluster is fully up and running you can explore the application and its associated telemetry via the following Urls:

* [http://localhost:1880](http://localhost:1880) - generates traffic across the Akka.NET cluster inside the `phobos-web` service.
* [http://localhost:16686/](http://localhost:16686/) - Jaeger tracing UI. Allows to explore the traces that are distributed across the different nodes in the cluster.
* [http://localhost:9090/](http://localhost:9090/) - Prometheus query UI.
* [http://localhost:3000/](http://localhost:3000/) - Grafana metrics. Log in using the username **admin** and the password **admin**. It includes some ready-made dashboards you can use to explore Phobos + App.Metrics metrics:
	- [Akka.NET Metrics](http://localhost:3000/d/I84lyfiMk/akka-net-metrics?orgId=1)
	- [ASP.NET Core Metrics](http://localhost:3000/d/ggsijSPZz/asp-net-core-metrics?orgId=1)
	- [Kubernetes Cluster Metrics](http://localhost:3000/d/9q974SWGz/kubernetes-pod-resources?orgId=1)

There's many more metrics exported by Phobos that you can use to create your own dashboards or extend the existing ones - you can view all of them by going to [http://localhost:1880/metrics](http://localhost:1880/metrics)

### Tearing Down the Cluster
When you're done exploring this sample, you can tear down the cluster by running the following command:

```
PS> ./k8s/destroyAll.cmd
```

This will delete the `phobos-web` namespace and all of the resources inside it.

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

### Deployment
Petabridge.App uses Docker for deployment - to create Docker images for this project, please run the following command:

```
build.cmd Docker
```

By default `build.fsx` will look for every `.csproj` file that has a `Dockerfile` in the same directory - from there the name of the `.csproj` will be converted into [the supported Docker image name format](https://docs.docker.com/engine/reference/commandline/tag/#extended-description), so "Petabridge.App.csproj" will be converted to an image called `petabridge.app:latest` and `petabridge.app:{VERSION}`, where version is determined using the rules defined in the section below.

#### Pushing to a Remote Docker Registry
You can also specify a remote Docker registry URL and that will cause a copy of this Docker image to be published there as well:

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

### Conventions
The attached build script will automatically do the following based on the conventions of the project names added to this project:

* Any project name ending with `.Tests` will automatically be treated as a [XUnit2](https://xunit.github.io/) project and will be included during the test stages of this build script;
* Any project name ending with `.Tests.Performance` will automatically be treated as a [NBench](https://github.com/petabridge/NBench) project and will be included during the test stages of this build script; and
* Any project meeting neither of these conventions will be treated as a NuGet packaging target and its `.nupkg` file will automatically be placed in the `bin\nuget` folder upon running the `build.[cmd|sh] all` command.

### DocFx for Documentation
This solution also supports [DocFx](http://dotnet.github.io/docfx/) for generating both API documentation and articles to describe the behavior, output, and usages of your project. 

All of the relevant articles you wish to write should be added to the `/docs/articles/` folder and any API documentation you might need will also appear there.

All of the documentation will be statically generated and the output will be placed in the `/docs/_site/` folder. 

#### Previewing Documentation
To preview the documentation for this project, execute the following command at the root of this folder:

```
C:\> serve-docs.cmd
```

This will use the built-in `docfx.console` binary that is installed as part of the NuGet restore process from executing any of the usual `build.cmd` or `build.sh` steps to preview the fully-rendered documentation. For best results, do this immediately after calling `build.cmd buildRelease`.

### Code Signing via SignService
This project uses [SignService](https://github.com/onovotny/SignService) to code-sign NuGet packages prior to publication. The `build.cmd` and `build.sh` scripts will automatically download the `SignClient` needed to execute code signing locally on the build agent, but it's still your responsibility to set up the SignService server per the instructions at the linked repository.

Once you've gone through the ropes of setting up a code-signing server, you'll need to set a few configuration options in your project in order to use the `SignClient`:

* Add your Active Directory settings to [`appsettings.json`](appsettings.json) and
* Pass in your signature information to the `signingName`, `signingDescription`, and `signingUrl` values inside `build.fsx`.

Whenever you're ready to run code-signing on the NuGet packages published by `build.fsx`, execute the following command:

```
C:\> build.cmd nuget SignClientSecret={your secret} SignClientUser={your username}
```

This will invoke the `SignClient` and actually execute code signing against your `.nupkg` files prior to NuGet publication.

If one of these two values isn't provided, the code signing stage will skip itself and simply produce unsigned NuGet code packages.
