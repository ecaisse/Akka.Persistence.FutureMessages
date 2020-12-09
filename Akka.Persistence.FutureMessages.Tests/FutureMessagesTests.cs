using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Persistence.FutureMessages.Incoming;
using Akka.Persistence.FutureMessages.Outgoing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Persistence.FutureMessages.Tests
{
    public class FutureMessagesTests : EventsourcedSchedulerTestKit
    {
        public FutureMessagesTests() : base(30, 100)
        {
        }

        [Fact]
        public void WhenFireTimeIsInPast_Fires()
        {
            this.IgnoreAck();

            var fireTime = DateTime.UtcNow.AddSeconds(-2);
            this.ScheduleFutureMessage("payload", fireTime);
            this.ExpectCallback("payload", 500);
        }

        [Fact]
        public void WhenMultipleMessagesWithSameFireTime_FiresInSequence()
        {
            const int NumberOfMessages = 10;

            this.IgnoreAck();

            var fireTime = DateTime.UtcNow.AddSeconds(1);
            for (int i = 0; i < NumberOfMessages; i++)
            {
                // Fire a message every 100 ms
                this.ScheduleFutureMessage(i, fireTime);
            }

            this.ExpectNoMsg(500);

            for (int i = 0; i < NumberOfMessages; i++)
            {
                this.ExpectCallback(i, 2000);
            }
        }

        [Fact]
        public void WhenMultipleMessagesWithDifferentFireTime_FiresInSequence()
        {
            const int NumberOfMessages = 10;

            this.IgnoreAck();

            var fireTime = DateTime.UtcNow.AddSeconds(1);
            for (int i = 0; i < NumberOfMessages; i++)
            {
                // Fire a message every 100 ms
                this.ScheduleFutureMessage(i, fireTime.AddMilliseconds(100 * i));
            }

            this.ExpectNoMsg(500);

            for (int i = 0; i < NumberOfMessages; i++)
            {
                this.ExpectCallback(i, 2000);
            }
        }

        [Fact]
        public void WhenQueuedInReversedFireTimeOrder_FiresInSequence()
        {
            const int NumberOfMessages = 10;

            this.IgnoreAck();

            var fireTime = DateTime.UtcNow.AddSeconds(2);
            for (int i = 0; i < NumberOfMessages; i++)
            {
                // Fire a message every 100 ms
                this.ScheduleFutureMessage(i, fireTime.AddMilliseconds(-100 * i));
            }

            this.ExpectNoMsg(500);

            for (int i = NumberOfMessages - 1; i >= 0; i--)
            {
                // Assert messages in reverse order.
                this.ExpectCallback(i, 2000);
            }
        }

        [Fact]
        public void WhenQueuedInRandomFireTimeOrder_FiresInSequence()
        {
            const int NumberOfMessages = 10;

            this.IgnoreAck();

            var random = new Random();
            var fireTime = DateTime.UtcNow.AddSeconds(1);
            foreach (var item in Enumerable.Range(0, NumberOfMessages).OrderBy(x => random.Next()))
            {
                // Fire a message every 100 ms
                this.ScheduleFutureMessage(item, fireTime.AddMilliseconds(100 * item));
            }

            this.ExpectNoMsg(500);

            for (int i = 0; i < NumberOfMessages; i++)
            {
                this.ExpectCallback(i, 2000);
            }
        }

        [Fact]
        public void WhenQueued_ReceivesAck()
        {
            const int NumberOfMessages = 10;

            var fireTime = DateTime.UtcNow.AddSeconds(1);
            for (int i = 0; i < NumberOfMessages; i++)
            {
                // Fire a message every 100 ms
                this.ScheduleFutureMessage(i, fireTime.AddMilliseconds(100 * i), $"msg-{i}");
            }

            for (int i = 0; i < NumberOfMessages; i++)
            {
                this.ExpectMsg<Ack>(msg => Assert.Equal(msg.Id, $"msg-{i}"), TimeSpan.FromMilliseconds(100));
            }

            for (int i = 0; i < NumberOfMessages; i++)
            {
                this.ExpectCallback(i, 2000);
            }
        }

        [Fact]
        public async Task WhenQueuedAndWaitingForAck_ReceivesAck()
        {
            var id = Guid.NewGuid().ToString();
            var ack = await this.EventsourcedSchedulerActor.Ask<Ack>(new FutureMessage(id, "payload", DateTimeOffset.UtcNow.AddSeconds(500), this.TestActor), TimeSpan.FromMilliseconds(100));
            Assert.Equal(id, ack.Id);
        }

        [Fact]
        public void WhenExceedingSchedulerLimit_WillResizeAndFireAllFutureMessages()
        {
            this.IgnoreAck();

            const int NumberOfMessages = 40;

            var fireTime = DateTime.UtcNow.AddSeconds(1);
            for (int i = 0; i < NumberOfMessages; i++)
            {
                this.ScheduleFutureMessage(i, fireTime.AddMilliseconds(-10 * i));
            }

            for (int i = NumberOfMessages - 1; i >= 0; i--)
            {
                this.ExpectCallback(i, 1000);
            }
        }
    }
}
