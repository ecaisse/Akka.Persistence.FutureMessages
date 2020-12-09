using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.FutureMessages.Incoming;
using Akka.Persistence.FutureMessages.Outgoing;
using Priority_Queue;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Persistence.FutureMessages
{
    public sealed class OldEventsourcedScheduler : UntypedPersistentActor
    {
        // Priority queue ordered by their next "fire" time.
        private readonly GenericPriorityQueue<FutureMessageNode, DateTimeOffset> _queue;

        // Index of messages by ID for fast lookups inside the queue.
        private readonly Dictionary<string, FutureMessageNode> _map;

        private readonly int _maxQueuedMessages;
        private readonly int _operationCountPerSnapshot;

        private ICancelable _cancellable;
        private int _currentOperationCount;

        public OldEventsourcedScheduler(Config config)
            : this(config.GetInt("maxQueuedMessages"), config.GetInt("operationCountPerSnapshot"))
        {

        }

        public OldEventsourcedScheduler(int maxQueuedMessages, int operationCountPerSnapshot)
        {
            this._maxQueuedMessages = maxQueuedMessages;
            this._operationCountPerSnapshot = operationCountPerSnapshot;
            this._queue = new GenericPriorityQueue<FutureMessageNode, DateTimeOffset>(maxQueuedMessages);
            this._map = new Dictionary<string, FutureMessageNode>(maxQueuedMessages);
        }

        public override string PersistenceId => this.Self.Path.ToString();

        protected override void OnCommand(object message)
        {
            var originalFirst = this._queue.Count > 0 ? this._queue.First : null;
            var originalFirstFireTime = originalFirst?.Priority;
            switch (message)
            {
                case ISchedulerMessage sm:
                    this.PersistAsync(sm, this.OnSchedulerMessage);
                    this.DeferAsync((originalFirst, originalFirstFireTime), x => this.OnQueueUpdated(x.originalFirst, x.originalFirstFireTime, true));
                    this.DeferAsync(sm.Id, id => this.Sender.Tell(new Ack(id), this.Self));
                    break;

                case InternalTick _:
                    var start = DateTimeOffset.UtcNow;
                    bool hasProcessed = false;
                    while (this._queue.Count > 0 && this._queue.First.Priority < start)
                    {
                        var messageToSend = this._queue.Dequeue();
                        this.PersistAsync(new Processed(messageToSend.Id, messageToSend.Message, messageToSend.ActorRef), this.OnProcessed);
                        hasProcessed = true;
                    }

                    if (hasProcessed)
                    {
                        this.DeferAsync((originalFirst, originalFirstFireTime), x => this.OnQueueUpdated(x.originalFirst, x.originalFirstFireTime, true));
                    }

                    break;
            }
        }

        protected override void PostStop()
        {
            this._cancellable.CancelIfNotNull();
        }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case SnapshotOffer so:
                    foreach (var fm in (IEnumerable<FutureMessage>)so.Snapshot)
                    {
                        this.OnFutureMessage(fm);
                    }
                    break;

                case Processed p:
                    this.OnProcessed(p);
                    break;

                case ISchedulerMessage sm:
                    this.OnSchedulerMessage(sm);
                    break;
            }
        }

        protected override void OnReplaySuccess()
        {
            var first = this._queue.Count > 0 ? this._queue.First : null;
            var firstFireTime = first?.Priority;
            this.OnQueueUpdated(first, firstFireTime, false);
        }

        private void OnSchedulerMessage(ISchedulerMessage message)
        {
            switch (message)
            {
                case FutureMessage _ when this._queue.Count == this._maxQueuedMessages:
                    if (this.IsRecovering)
                    {
                        throw new InvalidOperationException("The size of the queue is smaller than the snapshot. This can happen if you changed the config of the queue to be smaller than at the moment of snapshot.");
                    }

                    // Cannot process message when scheduler is full, reply to sender.
                    Sender.Tell(SchedulerFull.Instance, this.Self);
                    break;

                case FutureMessage fm:
                    this.OnFutureMessage(fm);
                    break;

                case RecallMessage rm:
                    this.OnRecallMessage(rm);
                    break;

                case UpdateFireTimeAbsolute ua:
                    this.OnUpdateFireTimeAbsolute(ua);
                    break;

                case UpdateFireTimeRelative ur:
                    this.OnUpdateFireTimeRelative(ur);
                    break;

                default:
                    throw new ArgumentException($"Invalid scheduler message type: '{message.GetType()}'", nameof(message));
            }
        }

        private void OnQueueUpdated(FutureMessageNode originalFirst, DateTimeOffset? originalFirstFireTime, bool takeSnapshot)
        {
            // If the first element of the queue changed, cancel the queued tick and reschedule it at the new next fire time.
            if (this._queue.Count > 0)
            {
                // If the first element of the queue changed, cancel the queued tick and reschedule it at the new next fire time.
                // Also, if the priority of the first element changed, we need to reschedule the tick.
                var actualFirst = this._queue.First;
                if (actualFirst != originalFirst || actualFirst.Priority != originalFirstFireTime)
                {
                    this._cancellable.CancelIfNotNull();
                    this._cancellable = null;

                    var delay = actualFirst.Priority - DateTimeOffset.UtcNow;
                    if (delay < TimeSpan.Zero)
                    {
                        delay = TimeSpan.Zero;
                    }

                    this._cancellable = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, this.Self, InternalTick.Instance, ActorRefs.NoSender);
                }
            }
            else
            {
                this._cancellable.CancelIfNotNull();
                this._cancellable = null;
            }

            // Take a snapshot every now and again in case this actor is shutdown and needs recovery.
            if (takeSnapshot && ++this._currentOperationCount >= this._operationCountPerSnapshot)
            {
                var messages = ImmutableList.CreateRange(this._map.Select(kv => new FutureMessage(kv.Key, kv.Value.Message, kv.Value.Priority, kv.Value.ActorRef)));
                this.SaveSnapshot(messages);
                this._currentOperationCount = 0;
            }
        }

        private void OnFutureMessage(FutureMessage evt)
        {
            var node = new FutureMessageNode(evt.Id, evt.Message, evt.ActorRef);
            if (this._map.TryAdd(evt.Id, node))
            {
                this._queue.Enqueue(node, evt.FireTime);
            }
        }

        private void OnRecallMessage(RecallMessage evt)
        {
            if (this._map.Remove(evt.Id, out var node))
            {
                this._queue.Remove(node);
            }
        }

        private void OnUpdateFireTimeAbsolute(UpdateFireTimeAbsolute evt)
        {
            this.OnUpdateFireTime(evt.Id, prev => evt.NewFireTime);
        }

        private void OnUpdateFireTimeRelative(UpdateFireTimeRelative evt)
        {
            this.OnUpdateFireTime(evt.Id, prev => prev.Add(evt.Delta));
        }

        private void OnProcessed(Processed evt)
        {
            if (this._map.Remove(evt.Id, out var node) && this.IsRecovering)
            {
                // In recovery mode, the message is expected to have already been sent.
                // However, due to processing outside a state change (Persist), the queue will be reconstructed without the proper "Dequeue"
                this._queue.Remove(node);
            }
            else
            {
                // In the normal flow, we tell send back the message to the actor.
                evt.ActorRef.Tell(evt.Message, this.Self);
            }
        }

        private void OnUpdateFireTime(string id, Func<DateTimeOffset, DateTimeOffset> update)
        {
            if (this._map.TryGetValue(id, out var node))
            {
                this._queue.UpdatePriority(node, update(node.Priority));
            }
        }

        internal sealed class InternalTick
        {
            internal static readonly InternalTick Instance = new InternalTick();
            private InternalTick()
            {
            }
        }

        internal sealed class Processed
        {
            internal Processed(string id, object message, IActorRef actorRef)
            {
                Id = id;
                Message = message;
                ActorRef = actorRef;
            }

            public string Id { get; }
            public object Message { get; }
            public IActorRef ActorRef { get; }
        }

        private sealed class FutureMessageNode : GenericPriorityQueueNode<DateTimeOffset>
        {
            public FutureMessageNode(string id, object message, IActorRef actorRef)
            {
                this.Id = id;
                this.Message = message;
                this.ActorRef = actorRef;
            }

            public string Id { get; }
            public object Message { get; }
            public IActorRef ActorRef { get; }
        }
    }
}
