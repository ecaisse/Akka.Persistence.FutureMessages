using Akka.Actor;
using Akka.Persistence.FutureMessages.Outgoing;
using System;

namespace Akka.Persistence.FutureMessages.Internals
{
    internal sealed class NoopMessageStrategy : IAcknowledgementStrategy
    {
        public void OnAcknowledge(Ack acknowledgement, object message, IActorRef sender, bool isReplaying)
        {
        }

        public void OnAcknowledgeFailed(AggregateException exception, object message, IActorRef sender, bool isRecovering)
        {
        }
    }
}
