// -----------------------------------------------------------------------
// <copyright file="AkkaService.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2021 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Util;
using Phobos.Actor;

namespace DemoPhobos;

public sealed class ChildActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public ChildActor()
    {
        ReceiveAny(msg =>
        {
            if (ThreadLocalRandom.Current.Next(0, 3) == 1) throw new ApplicationException("I'm crashing!");

            _log.Info("Received: {0}", msg);
            Sender.Tell(msg);
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
    private readonly ILoggingAdapter _log = Context.GetLogger();

    public ConsoleActor()
    {
        var processingTimer = Context.GetInstrumentation().Monitor.CreateHistogram<double>("ProcessingTime", "ms");
        Receive<string>(_ =>
        {
            // use the local metrics handle to record a timer duration for how long this block of code takes to execute
            var start = DateTime.UtcNow;

            // start another span programmatically inside actor
            using (var newSpan = Context.GetInstrumentation().Tracer.StartActiveSpan("SecondOp"))
            {
                var child = Context.ActorOf(Props.Create(() => new ChildActor()));
                _log.Info("Spawned {child}", child);

                child.Forward(_);
            }

            var duration = (DateTime.UtcNow - start).TotalMilliseconds;
            processingTimer.Record(duration);
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
        Receive<string>(str =>
        {
            _log.Info("Received: {0}", str);
            _routerActor.Forward(str);
        });
    }
}