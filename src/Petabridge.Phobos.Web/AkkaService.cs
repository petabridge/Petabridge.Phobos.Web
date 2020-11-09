// -----------------------------------------------------------------------
// <copyright file="AkkaService.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2020 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;
using Akka.Routing;
using App.Metrics.Timer;
using Microsoft.Extensions.Hosting;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;
using Phobos.Actor;

namespace Petabridge.Phobos.Web
{
    public sealed class ConsoleActor : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public ConsoleActor()
        {
            Receive<string>(_ =>
            {
                // use the local metrics handle to record a timer duration for how long this block of code takes to execute
                Context.GetInstrumentation().Monitor.Timer.Time(new TimerOptions {Name = "ProcessingTime"}, () =>
                {
                    // start another span programmatically inside actor
                    using (var newSpan = Context.GetInstrumentation().Tracer.BuildSpan("SecondOp").StartActive())
                    {
                        _log.Info("Received: {0}", _);
                        Sender.Tell(_);
                    }
                });
            });
        }
    }

    /// <summary>
    ///     To add some color to the traces
    /// </summary>
    public sealed class RouterForwaderActor : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IActorRef _routerActor;

        public RouterForwaderActor(IActorRef routerActor)
        {
            _routerActor = routerActor;
            Receive<string>(_ =>
            {
                _log.Info("Received: {0}", _);
                _routerActor.Forward(_);
            });
        }
    }

    public sealed class PublisherActor : ReceiveActor
    {
        public PublisherActor()
        {
            // activate the extension
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            
            ReceiveAny(m => mediator.Tell(new Publish("someTopic", m)));
        }
    }

    public sealed class SubscriberActor : ReceiveActor
    {
        public SubscriberActor()
        {
            var mediator = DistributedPubSub.Get(Context.System).Mediator;

            // subscribe to the topic named "content"
            mediator.Tell(new Subscribe("someTopic", Self));
            
            Receive<SubscribeAck>(subscribeAck =>
            {
                if (subscribeAck.Subscribe.Topic.Equals("content")
                    && subscribeAck.Subscribe.Ref.Equals(Self)
                    && subscribeAck.Subscribe.Group == null)
                {
                    Context.GetLogger().Info("subscribing");
                }
            });
            
            ReceiveAny(s =>
            {
                Context.GetLogger().Info($"Got {s}");
            });
        }
    }

    /// <summary>
    ///     Container for retaining actors
    /// </summary>
    public sealed class AkkaActors
    {
        public AkkaActors(ActorSystem sys)
        {
            Sys = sys;
            ConsoleActor = sys.ActorOf(Props.Create(() => new ConsoleActor()), "console");
            RouterActor = sys.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "echo");
            RouterForwarderActor = sys.ActorOf(Props.Create(() => new RouterForwaderActor(RouterActor)), "fwd");
            Publisher = sys.ActorOf(Props.Create<PublisherActor>(), "publisher");
            Subscriber = sys.ActorOf(Props.Create<SubscriberActor>(), "subscriber");
        }

        internal ActorSystem Sys { get; }

        public IActorRef ConsoleActor { get; }

        internal IActorRef RouterActor { get; }

        public IActorRef RouterForwarderActor { get; }
        
        public IActorRef Publisher { get; }
        public IActorRef Subscriber { get; }
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