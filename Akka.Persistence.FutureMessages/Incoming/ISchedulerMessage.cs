namespace Akka.Persistence.FutureMessages.Incoming
{
    internal interface ISchedulerMessage
    {
        string Id { get; }
    }
}
