using System;

namespace Akka.Persistence.FutureMessages.Incoming
{
    public sealed class UpdateFireTimeRelative : ISchedulerMessage
    {
        public UpdateFireTimeRelative(string id, TimeSpan delta)
        {
            Id = id;
            Delta = delta;
        }

        public string Id { get; }
        public TimeSpan Delta { get; }
    }
}
