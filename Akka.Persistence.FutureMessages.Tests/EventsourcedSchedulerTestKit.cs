using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Persistence.FutureMessages.Incoming;
using Akka.Persistence.FutureMessages.Outgoing;
using Akka.Persistence.TestKit;
using Akka.TestKit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Persistence.FutureMessages.Tests
{
    public abstract class EventsourcedSchedulerTestKit : PersistenceTestKit
    {
        public EventsourcedSchedulerTestKit(int defaultQueueSize, int operationCountPerSnapshot)
            : base(CreateConfig(defaultQueueSize, operationCountPerSnapshot))
        {
            var config = Sys.Settings.Config.GetConfig("akka.persistence.eventsourced-scheduler");
            this.EventsourcedSchedulerActor = this.ActorOfAsTestActorRef(() => new EventsourcedScheduler(config), "TestEventsourcedScheduler");
        }

        private static Config CreateConfig(int defaultQueueSize, int operationCountPerSnapshot)
        {
            return ConfigurationFactory.ParseString(@$"
                akka.persistence.eventsourced-scheduler.default-queue-size: {defaultQueueSize}
                akka.persistence.eventsourced-scheduler.operations-per-snapshot: {operationCountPerSnapshot}
            ").WithFallback(EventsourcedSchedulerSettings.DefaultConfig);
        }

        protected TestActorRef<EventsourcedScheduler> EventsourcedSchedulerActor { get; private set; }

        protected SupervisorStrategy SupervisorStrategy { get; set; } = new OneForOneStrategy(x => Directive.Restart);

        protected string ActorToTestPersistenceId => this.EventsourcedSchedulerActor.Path.ToStringWithoutAddress();

        protected DateTimeOffset ScheduleFutureMessage<T>(T message, int fireTimeFromNowMs, string id = null)
        {
            var fireTime = DateTimeOffset.UtcNow.AddMilliseconds(fireTimeFromNowMs);
            this.ScheduleFutureMessage(message, fireTime, id);
            return fireTime;
        }

        protected FutureMessage ScheduleFutureMessage<T>(T message, DateTimeOffset fireTime, string id = null)
        {
            var fm = new FutureMessage(id ?? Guid.NewGuid().ToString(), message, fireTime, this.TestActor);
            this.EventsourcedSchedulerActor.Tell(fm);
            return fm;
        }

        protected async Task<FutureMessage> ScheduleFutureMessageAndWait<T>(T message, DateTimeOffset fireTime, string id = null)
        {
            var fm = new FutureMessage(id ?? Guid.NewGuid().ToString(), message, fireTime, this.TestActor);
            await this.EventsourcedSchedulerActor.Ask<Ack<FutureMessage>>(fm);
            return fm;
        }

        protected void RecallMessage(string id)
        {
            this.EventsourcedSchedulerActor.Tell(new RecallMessage(id));
        }

        protected void UpdateFireTimeAbsolute(string id, int msFromNow)
        {
            this.EventsourcedSchedulerActor.Tell(new UpdateFireTimeAbsolute(id, DateTimeOffset.UtcNow.AddMilliseconds(msFromNow)));
        }

        protected void UpdateFireTimeAbsolute(string id, DateTimeOffset newFireTime)
        {
            this.EventsourcedSchedulerActor.Tell(new UpdateFireTimeAbsolute(id, newFireTime));
        }

        protected void UpdateFireTimeRelative(string id, int deltaMs)
        {
            this.EventsourcedSchedulerActor.Tell(new UpdateFireTimeRelative(id, TimeSpan.FromMilliseconds(deltaMs)));
        }

        protected void IgnoreAck()
        {
            this.IgnoreMessages<Ack>();
        }

        protected void ExpectCallback<T>(T message, int timeoutMs = -1)
        {
            var timeout = timeoutMs == -1 ? (TimeSpan?)null: TimeSpan.FromMilliseconds(timeoutMs);
            ExpectMsg<T>(msg => Assert.Equal(message, msg), timeout);
        }

        protected void ExpectDelayedCallback<T>(T message, int fireTimeMs, int timeoutMs = -1)
        {
            if (fireTimeMs > 10)
            {
                ExpectNoMsg(fireTimeMs >> 1);
            }

            this.ExpectCallback(message, timeoutMs == -1 ? fireTimeMs : timeoutMs);
        }

        protected Task<SelectedSnapshot> GetSnapshot(long? sequenceNumber = null)
        {
            var criteria = sequenceNumber == null ? SnapshotSelectionCriteria.Latest : new SnapshotSelectionCriteria(sequenceNumber.Value, DateTime.MaxValue);
            return this.GetSnapshot(criteria);
        }

        protected async Task<SelectedSnapshot> GetSnapshot(SnapshotSelectionCriteria criteria)
        {
            var result = await this.SnapshotsActorRef.Ask<LoadSnapshotResult>(new LoadSnapshot(this.ActorToTestPersistenceId, criteria, long.MaxValue));
            return result.Snapshot;
        }

        protected void SaveSnapshot(IEnumerable<FutureMessage> toSnapshot, long sequenceNumber = 0)
        {
            var msg = new SaveSnapshot(new SnapshotMetadata(this.ActorToTestPersistenceId, sequenceNumber), ImmutableList.CreateRange(toSnapshot));
            this.SnapshotsActorRef.Tell(msg, this.EventsourcedSchedulerActor);
        }

        protected void RestartActor(Exception exception = null)
        {
            this.EventsourcedSchedulerActor.SendSystemMessage(new Recreate(exception));
        }
    }
}
