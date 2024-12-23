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

From there, run the following command on the prompt:

```
PS> build.cmd Docker
```

This will create the Docker images the solution needs to run inside Kubernetes: `petabridge.phobos.web:0.2.4`.

### Deploying the K8s Cluster (with Telemetry Installed)
From there, everything you need to run the solution in Kubernetes is already defined inside the [`k8s/` folder](k8s/) - just run the following command to launch the Phobos-enabled application inside Kubernetes:

```
PS> ./k8s/deployAll.cmd
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
* [http://localhost:3000/](http://localhost:3000/) - Grafana metrics. Log in using the username **admin** and the password **admin**. It includes some ready-made dashboards you can use to explore Phobos + OpenTelemetry metrics:
	- [Akka.NET Cluster Metrics](http://localhost:3000/d/8Y4JcEfGk/akka-net-cluster-metrics?orgId=1&refresh=10s) - this is a pre-installed version of our [Akka.NET Cluster + Phobos Metrics (Prometheus Data Source) Dashboard](https://phobos.petabridge.com/articles/dashboards/prometheus-dashboard.html#phobos-2x) on Grafana Cloud, which you can install instantly into your own applications!
	- [ASP.NET Core Metrics](http://localhost:3000/d/ggsijSPZz/asp-net-core-metrics?orgId=1)
	- [Kubernetes Cluster Metrics](http://localhost:3000/d/9q974SWGz/kubernetes-pod-resources?orgId=1)
* [http://localhost:8988/](http://localhost:8988/) - Seq log aggregation.

There's many more metrics exported by Phobos that you can use to create your own dashboards or extend the existing ones - you can view all of them by going to [http://localhost:1880/metrics](http://localhost:1880/metrics)

### Tearing Down the Cluster
When you're done exploring this sample, you can tear down the cluster by running the following command:

```
PS> ./k8s/destroyAll.cmd
```

This will delete the `phobos-web` namespace and all of the resources inside it.