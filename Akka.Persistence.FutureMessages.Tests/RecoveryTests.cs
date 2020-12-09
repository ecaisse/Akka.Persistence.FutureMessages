using Akka.Persistence.FutureMessages.Incoming;
using Akka.Persistence.FutureMessages.Outgoing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Persistence.FutureMessages.Tests
{
    public class RecoveryTests : EventsourcedSchedulerTestKit
    {
        private const int OperationCountPerSnapshot = 20;

        public RecoveryTests() : base(100, OperationCountPerSnapshot)
        {
        }

        [Fact]
        public async Task WhenLimitOfOperationReached_TakesSnapshot()
        {
            const int TotalOperations = OperationCountPerSnapshot + 10;

            for (int i = 0; i < TotalOperations; i++)
            {
                this.ScheduleFutureMessage(i, DateTimeOffset.UtcNow.AddHours(1));
            }

            this.ReceiveN(TotalOperations);

            var result = await this.GetSnapshot();
            var snapshottedEvents = Assert.IsAssignableFrom<IEnumerable<FutureMessage>>(result.Snapshot);
            Assert.Equal(OperationCountPerSnapshot, snapshottedEvents.Count());
            Assert.Equal(Enumerable.Range(0, OperationCountPerSnapshot), snapshottedEvents.Select(x => (int)x.Message));
        }

        [Fact]
        public async Task WhenLimitOfOperationReachedMultipleTimes_TakesSnapshot()
        {
            await this.WithJournalWrite(x => x.Pass(),
            async () =>
            {
                const int SnapshotCount = 3;
                const int TotalOperations = (SnapshotCount * OperationCountPerSnapshot) + 10;

                for (int i = 0; i < TotalOperations; i++)
                {
                    this.ScheduleFutureMessage(i, DateTimeOffset.UtcNow.AddHours(1));
                }

                this.ReceiveN(TotalOperations);

                var results = await Task.WhenAll(Enumerable.Range(1, SnapshotCount).Select(x => this.GetSnapshot(x * OperationCountPerSnapshot)));

                for (int i = 0; i < SnapshotCount; i++)
                {
                    var result = results[i];
                    var snapshottedEvents = Assert.IsAssignableFrom<IEnumerable<FutureMessage>>(result.Snapshot);
                    var expectedSnapshottedEvents = (i + 1) * OperationCountPerSnapshot;
                    Assert.Equal(expectedSnapshottedEvents, snapshottedEvents.Count());
                    Assert.Equal(Enumerable.Range(0, expectedSnapshottedEvents), snapshottedEvents.Select(x => (int)x.Message));
                }
            });
        }

        [Fact]
        public void WhenRecoveringFromSnapshot_LoadsFutureMessagesFromSnapshot()
        {
            this.IgnoreAck();

            var now = DateTimeOffset.UtcNow;
            var events = Enumerable.Range(0, 5).Select(x => new FutureMessage(x.ToString(), x, now.AddSeconds(1), this.TestActor));
            this.SaveSnapshot(events);
            this.RestartActor();

            this.EventsourcedSchedulerActor.Tell(new RecallMessage("1"));

            this.ExpectNoMsg(500);
            this.ExpectCallback(0, 1000);
            this.ExpectCallback(2, 1000);
            this.ExpectCallback(3, 1000);
            this.ExpectCallback(4, 1000);
        }

        [Fact]
        public void WhenRecoveringWithoutSnapshot_LoadsFutureMessagesFromHistory()
        {
            var now = DateTimeOffset.UtcNow;
            for (int i = 0; i < 5; i++)
            {
                this.ScheduleFutureMessage(i, now.AddSeconds(1), i.ToString());
            }

            this.ReceiveN(5);

            this.RestartActor();

            this.IgnoreMessages<Ack<RecallMessage>>(msg => msg.Id == "1");
            this.EventsourcedSchedulerActor.Tell(new RecallMessage("1"));

            this.ExpectNoMsg(500);
            this.ExpectCallback(0, 1000);
            this.ExpectCallback(2, 1000);
            this.ExpectCallback(3, 1000);
            this.ExpectCallback(4, 1000);
        }

        [Fact]
        public void WhenRecoveringFromSnapshotAndHadExtraMessages_LoadsFutureMessagesFromSnapshotAndHistory()
        {
            this.IgnoreAck();

            var now = DateTimeOffset.UtcNow;
            var events = Enumerable.Range(0, 5).Select(x => new FutureMessage(x.ToString(), x, now.AddSeconds(1), this.TestActor));
            this.SaveSnapshot(events);
            this.ScheduleFutureMessage(6, now.AddSeconds(2), "6");
            this.ReceiveOne();

            this.RestartActor();

            this.EventsourcedSchedulerActor.Tell(new RecallMessage("1"));

            this.ExpectNoMsg(500);
            this.ExpectCallback(0, 1000);
            this.ExpectCallback(2, 1000);
            this.ExpectCallback(3, 1000);
            this.ExpectCallback(4, 1000);
            this.ExpectCallback(6, 2000);
        }
    }
}
