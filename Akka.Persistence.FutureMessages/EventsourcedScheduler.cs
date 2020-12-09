using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.FutureMessages.Incoming;
using Akka.Persistence.FutureMessages.Internals;
using Akka.Persistence.FutureMessages.Outgoing;
using System.Collections.Generic;

namespace Akka.Persistence.FutureMessages
{
    public sealed class EventsourcedScheduler : UntypedPersistentActor, IWithUnboundedStash
    {
        private readonly EventsourcedSchedulerSettings _settings;
        private readonly ISnapshotStrategy _snapshotStrategy;
        private readonly IAcknowledgementStrategy _acknowledgementStrategy;

        private IActorRef _messageManagerRef;

        public EventsourcedScheduler(Config config = null)
        {
            this._settings = config == null ? EventsourcedSchedulerSettings.Get(Context.System) : EventsourcedSchedulerSettings.From(config);
            this._snapshotStrategy = FactoryUtils.Create<ISnapshotStrategy>(this._settings.SnapshotStrategyType, this.Self.AsFactoryParameter(), config.AsFactoryParameter(), Context.System.AsFactoryParameter(), this._settings.AsFactoryParameter());
            this._acknowledgementStrategy = FactoryUtils.Create<IAcknowledgementStrategy>(this._settings.AcknowledgementStrategyType, this.Self.AsFactoryParameter(), config.AsFactoryParameter(), Context.System.AsFactoryParameter(), this._settings.AsFactoryParameter());
        }

        public override string PersistenceId => this.Self.Path.ToStringWithoutAddress();

        protected override void PreStart()
        {
            this._messageManagerRef = Context.ActorOf(Props.Create(() => new ScheduledMessageManager(this._settings.DefaultQueueSize)));
            this._messageManagerRef.Tell(ScheduledMessageManager.Start.Instance);
        }

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case ISchedulerMessage sm:
                    var sender = this.Sender;
                    this.PersistAsync(sm, evt => {
                        this.OnSchedulerMessage(evt, sender);

                        if (this._snapshotStrategy.ShouldTakeSnapshot(evt, this.LastSequenceNr))
                        {
                            this._messageManagerRef.Tell(new ScheduledMessageManager.GetCurrentState(this.LastSequenceNr));
                            this.BecomeStacked(this.Snapshotting);
                        }
                    });
                    break;
            }
        }

        private void Snapshotting(object message)
        {
            if (message is ScheduledMessageManager.CurrentState cs)
            {
                this.SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata(this.SnapshotterId, cs.SequenceNumber, cs.AsOf.UtcDateTime), cs.State));
                this.UnbecomeStacked();
                this.Stash.UnstashAll();
            }
            else
            {
                this.Stash.Stash();
            }
        }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case SnapshotOffer so:
                    foreach (var sm in (IEnumerable<FutureMessage>)so.Snapshot)
                    {
                        this.OnSchedulerMessage(sm, ActorRefs.Nobody);
                    }
                    break;

                case ISchedulerMessage sm:
                    this.OnSchedulerMessage(sm, ActorRefs.Nobody);
                    break;
            }
        }

        protected override void PostStop()
        {
            this._messageManagerRef = null;
        }

        private void OnSchedulerMessage(ISchedulerMessage message, IActorRef sender)
        {
            var isRecovering = this.IsRecovering;
            this._messageManagerRef.Ask<Ack>(message)
                .ContinueWith(x =>
                {
                    this._acknowledgementStrategy.OnAcknowledge(x.Result, message, sender, isRecovering);
                },
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith(x =>
                {
                    this._acknowledgementStrategy.OnAcknowledgeFailed(x.Exception, message, sender, isRecovering);
                },
                System.Threading.Tasks.TaskContinuationOptions.NotOnRanToCompletion)
                .ConfigureAwait(false);
        }
    }
}
