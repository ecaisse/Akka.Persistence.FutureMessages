namespace Akka.Persistence.FutureMessages
{
    public interface ISnapshotStrategy
    {
        bool ShouldTakeSnapshot(object message, long sequenceNumber);
    }
}
