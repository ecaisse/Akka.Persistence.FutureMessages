using Akka.Configuration;

namespace Akka.Persistence.FutureMessages.Internals
{
    internal sealed class DefaultSnapshotStrategy : ISnapshotStrategy
    {
        private readonly int _operationsPerSnapshot;

        public DefaultSnapshotStrategy(Config config)
        {
            this._operationsPerSnapshot = config.GetInt("operations-per-snapshot");
        }

        public bool ShouldTakeSnapshot(object message, long sequenceNumber)
        {
            return sequenceNumber % this._operationsPerSnapshot == 0;
        }
    }
}
