// -----------------------------------------------------------------------
// <copyright file="AkkaService.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Routing;
using Akka.Util;
using App.Metrics.Timer;
using Microsoft.Extensions.Hosting;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;
using Phobos.Actor;
using SerilogLogMessageFormatter = Akka.Logger.Serilog.SerilogLogMessageFormatter;

namespace Petabridge.Phobos.Web
{
    public sealed class ChildActor : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public ChildActor()
        {
            ReceiveAny(_ =>
            {
                if (ThreadLocalRandom.Current.Next(0, 3) == 1) throw new ApplicationException("I'm crashing!");

                _log.Info("Received: {0}", _);
                Sender.Tell(_);
                Self.Tell(PoisonPill.Instance);

                if (ThreadLocalRandom.Current.Next(0, 4) == 2)
                    // send a random integer to our parent in order to generate an "unhandled"
                    // message periodically
                    Context.Parent.Tell(ThreadLocalRandom.Current.Next());
            });
        }

        protected override void PreRestart(Exception reason, object message)
        {
            // re-send the message that caused us to crash so we can reprocess
            Self.Tell(message, Sender);
        }
    }

    public sealed class ConsoleActor : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger(SerilogLogMessageFormatter.Instance);

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
                        var child = Context.ActorOf(Props.Create(() => new ChildActor()));
                        _log.Info("Spawned {child}", child);

                        child.Forward(_);
                    }
                });
            });
        }
    }

    /// <summary>
    ///     To add some color to the traces
    /// </summary>
    public sealed class RouterForwarderActor : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IActorRef _routerActor;

        public RouterForwarderActor(IActorRef routerActor)
        {
            _routerActor = routerActor;
            Receive<string>(_ =>
            {
                _log.Info("Received: {0}", _);
                _routerActor.Forward(_);
            });
        }
    }
}