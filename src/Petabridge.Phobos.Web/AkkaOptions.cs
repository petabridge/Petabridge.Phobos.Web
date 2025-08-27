using Akka.Cluster.Hosting;
using Akka.Discovery.Azure;
using Akka.Management;
using Akka.Management.Cluster.Bootstrap;
using Akka.Remote.Hosting;
using Petabridge.Cmd.Host;

namespace Petabridge.Phobos.Web;

public class AkkaOptions
{
     public RemoteOptions Remote { get; set; }
     public ClusterOptions Cluster { get; set; }
     public AkkaManagementOptions Management { get; set; }
     public ClusterBootstrapOptions ClusterBootstrap { get; set; }
     public AzureDiscoveryOptions Discovery { get; set; }
     public PetabridgeCmdOptions Pbm { get; set; }
}