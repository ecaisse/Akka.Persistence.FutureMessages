using Akka.Actor;
using Akka.Persistence.FutureMessages.Outgoing;
using System;

namespace Akka.Persistence.FutureMessages.Internals
{
    internal sealed class PassthroughAcknowledgementStrategy : IAcknowledgementStrategy
    {
        private readonly IActorRef _schedulerRef;

        public PassthroughAcknowledgementStrategy(IActorRef schedulerRef)
        {
            this._schedulerRef = schedulerRef;
        }

        public void OnAcknowledge(Ack acknowledgement, object message, IActorRef sender, bool isReplaying)
        {
            sender.Tell(message, this._schedulerRef);
        }

        public void OnAcknowledgeFailed(AggregateException exception, object message, IActorRef sender, bool isRecovering)
        {
        }
    }
}
