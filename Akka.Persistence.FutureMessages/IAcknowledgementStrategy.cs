using Akka.Actor;
using Akka.Persistence.FutureMessages.Outgoing;
using System;

namespace Akka.Persistence.FutureMessages
{
    public interface IAcknowledgementStrategy
    {
        void OnAcknowledge(Ack acknowledgement, object message, IActorRef sender, bool isReplaying);
        void OnAcknowledgeFailed(AggregateException exception, object message, IActorRef sender, bool isRecovering);
    }
}
