using System;

namespace Akka.Persistence.FutureMessages.Incoming
{
    public sealed class UpdateFireTimeAbsolute : ISchedulerMessage
    {
        public UpdateFireTimeAbsolute(string id, DateTimeOffset newFireTime)
        {
            Id = id;
            NewFireTime = newFireTime;
        }

        public string Id { get; }
        public DateTimeOffset NewFireTime { get; }
    }
}
