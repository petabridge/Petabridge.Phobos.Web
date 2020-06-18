using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Configuration;
using Akka.Event;
using Akka.Routing;
using App.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTracing;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;
using Phobos.Actor;
using Phobos.Actor.Configuration;

namespace Petabridge.Phobos.Web
{
    public sealed class ConsoleActor : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        public ConsoleActor()
        {
            Receive<string>(_ =>
            {
                _log.Info("Received: {0}", _);
                Sender.Tell(_);
            });
        }
    }

    /// <summary>
    /// Container for retaining actors
    /// </summary>
    public sealed class AkkaActors
    {
        public AkkaActors(ActorSystem sys)
        {
            Sys = sys;
            ConsoleActor = sys.ActorOf(Props.Create(() => new ConsoleActor()), "console");
            RouterActor = sys.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "echo");
        }

        internal ActorSystem Sys { get; }

        public IActorRef ConsoleActor { get; }

        public IActorRef RouterActor { get; }
    }

    public class AkkaService : IHostedService
    {
        private readonly AkkaActors _actors;

        public AkkaService(AkkaActors actors, IServiceProvider services)
        {
            _actors = actors;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // start https://cmd.petabridge.com/ for diagnostics and profit
            var pbm = PetabridgeCmd.Get(_actors.Sys); // start Pbm
            pbm.RegisterCommandPalette(ClusterCommands.Instance);
            pbm.RegisterCommandPalette(RemoteCommands.Instance);
            pbm.Start(); // begin listening for PBM management commands

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return CoordinatedShutdown.Get(_actors.Sys).Run(CoordinatedShutdown.ClrExitReason.Instance);
        }
    }
}
