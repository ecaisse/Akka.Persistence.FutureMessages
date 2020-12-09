using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.FutureMessages.Incoming;
using Akka.Persistence.FutureMessages.Outgoing;
using Priority_Queue;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Persistence.FutureMessages.Internals
{
    internal sealed class ScheduledMessageManager : UntypedActor
    {
        // TODO: this should be a config.
        private static readonly TimeSpan FireEventEpsilon = TimeSpan.FromMilliseconds(50);

        private readonly int _defaultQueueSize;

        // Priority queue ordered by their next "fire" time.
        private GenericPriorityQueue<FutureMessageNode, DateTimeOffset> _queue;

        // Index of messages by ID for fast lookups inside the queue.
        private Dictionary<string, FutureMessageNode> _map;

        // Current scheduled task
        private ICancelable _cancellable;

        public ScheduledMessageManager(int defaultQueueSize)
        {
            this._defaultQueueSize = defaultQueueSize;
        }

        protected override void PreStart()
        {
            this._queue = new GenericPriorityQueue<FutureMessageNode, DateTimeOffset>(this._defaultQueueSize);
            this._map = new Dictionary<string, FutureMessageNode>(this._defaultQueueSize);
        }

        protected override void OnReceive(object message)
        {
            if (message is Start)
            {
                this.Become(this.OnStarted);
                Self.Tell(InternalTick.Instance);
            }
        }

        private void OnStarted(object message)
        {
            var originalFirst = this._queue.Count > 0 ? this._queue.First : null;
            var originalFirstFireTime = originalFirst?.Priority;
            bool updateQueue = false;

            switch (message)
            {
                case InternalTick _:
                    var start = DateTimeOffset.UtcNow;
                    while (this._queue.Count > 0 && LessThanDelta(this._queue.First.Priority, start, FireEventEpsilon))
                    {
                        var messageToSend = this._queue.Dequeue();
                        messageToSend.ActorRef.Tell(messageToSend.Message, ActorRefs.NoSender);
                        updateQueue = true;
                    }
                    break;

                case FutureMessage fm:
                    if(this._queue.Count == this._queue.MaxSize)
                    {
                        var newSize = this._queue.MaxSize + this._defaultQueueSize;
                        this._queue.Resize(this._queue.MaxSize + this._defaultQueueSize);
                        this._map.EnsureCapacity(newSize);
                    }

                    this.OnFutureMessage(fm);
                    Sender.Tell(new Ack<FutureMessage>(fm.Id));
                    updateQueue = true;
                    break;

                case RecallMessage rm:
                    this.OnRecallMessage(rm);
                    Sender.Tell(new Ack<RecallMessage>(rm.Id));
                    updateQueue = true;
                    break;

                case UpdateFireTimeAbsolute ua:
                    this.OnUpdateFireTimeAbsolute(ua);
                    Sender.Tell(new Ack<UpdateFireTimeAbsolute>(ua.Id));
                    updateQueue = true;
                    break;

                case UpdateFireTimeRelative ur:
                    this.OnUpdateFireTimeRelative(ur);
                    Sender.Tell(new Ack<UpdateFireTimeRelative>(ur.Id));
                    updateQueue = true;
                    break;

                case GetCurrentState gcs:
                    var state = ImmutableList.CreateRange(this._map.Select(kv => new FutureMessage(kv.Key, kv.Value.Message, kv.Value.Priority, kv.Value.ActorRef)));
                    Sender.Tell(new CurrentState(gcs.SequenceNumber, state));
                    break;

                case ISchedulerMessage _:
                    throw new ArgumentException($"Invalid scheduler message type: '{message.GetType()}'", nameof(message));
            }

            if (updateQueue)
            {
                this.OnQueueUpdated(originalFirst, originalFirstFireTime);
            }
        }

        protected override void PostStop()
        {
            this._cancellable.CancelIfNotNull();
            this._cancellable = null;
            this._queue = null;
            this._map = null;
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

        private void OnUpdateFireTime(string id, Func<DateTimeOffset, DateTimeOffset> update)
        {
            if (this._map.TryGetValue(id, out var node))
            {
                this._queue.UpdatePriority(node, update(node.Priority));
            }
        }

        private void OnQueueUpdated(FutureMessageNode originalFirst, DateTimeOffset? originalFirstFireTime)
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
        }

        private static bool LessThanDelta(DateTimeOffset first, DateTimeOffset second, TimeSpan epsilon)
        {
            var delta = first - second;
            return delta <= epsilon;
        }

        internal sealed class Start
        {
            public static readonly Start Instance = new Start();

            private Start()
            {
            }
        }

        internal sealed class GetCurrentState
        {
            public GetCurrentState(long sequenceNumber)
            {
                this.SequenceNumber = sequenceNumber;
            }

            public long SequenceNumber { get; }
        }

        internal sealed class CurrentState
        {
            public CurrentState(long sequenceNumber, ImmutableList<FutureMessage> state)
            {
                this.SequenceNumber = sequenceNumber;
                this.State = state;
                this.AsOf = DateTimeOffset.UtcNow;
            }

            public long SequenceNumber { get; }
            public ImmutableList<FutureMessage> State { get; }
            public DateTimeOffset AsOf { get; }
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

        private sealed class InternalTick
        {
            public static readonly InternalTick Instance = new InternalTick();
            private InternalTick()
            {
            }
        }
    }
}
