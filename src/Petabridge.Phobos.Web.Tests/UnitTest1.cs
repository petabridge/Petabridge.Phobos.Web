// -----------------------------------------------------------------------
// <copyright file="UnitTest1.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2020 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using Xunit;
using Xunit.Abstractions;

namespace Petabridge.Phobos.Web.Tests
{
    public class UnitTest1 : TestKit
    {
        public UnitTest1(ITestOutputHelper helper) : base(GetTestConfig(), output: helper)
        {
        }

        public static Config GetTestConfig()
        {
            return @"
                akka{

                }
            ";
        }

        [Fact]
        public void TestMethod1()
        {
            var actor = Sys.ActorOf(act => { act.ReceiveAny((o, context) => { context.Sender.Tell(o); }); });

            actor.Tell("hit");
            ExpectMsg("hit");
        }
    }
}