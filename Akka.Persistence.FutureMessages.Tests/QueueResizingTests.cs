using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Persistence.FutureMessages.Tests
{
    public class QueueResizingTests : EventsourcedSchedulerTestKit
    {
        public QueueResizingTests(int maxQueuedMessages, int operationCountPerSnapshot) 
            : base(maxQueuedMessages, operationCountPerSnapshot)
        {
        }
    }
}
