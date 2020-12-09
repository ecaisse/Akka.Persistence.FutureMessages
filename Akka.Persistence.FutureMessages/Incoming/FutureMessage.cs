using Akka.Actor;
using System;

namespace Akka.Persistence.FutureMessages.Incoming
{
    public sealed class FutureMessage : ISchedulerMessage
    {
        public FutureMessage(string id, object message, DateTimeOffset fireTime, IActorRef actorRef)
        {
            Id = id;
            Message = message;
            FireTime = fireTime;
            ActorRef = actorRef;
        }

        public string Id { get; }
        public object Message { get; }
        public DateTimeOffset FireTime { get; }
        public IActorRef ActorRef { get; }
    }
}
