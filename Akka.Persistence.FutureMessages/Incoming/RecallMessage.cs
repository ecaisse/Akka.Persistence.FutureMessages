namespace Akka.Persistence.FutureMessages.Incoming
{
    public sealed class RecallMessage : ISchedulerMessage
    {
        public RecallMessage(string id)
        {
            Id = id;
        }

        public string Id { get; }
    }
}
