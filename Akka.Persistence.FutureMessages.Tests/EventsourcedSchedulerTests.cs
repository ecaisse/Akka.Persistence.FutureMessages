using Xunit;

namespace Akka.Persistence.FutureMessages.Tests
{
    public class EventsourcedSchedulerTests : EventsourcedSchedulerTestKit
    {
        public EventsourcedSchedulerTests() : base(10, 10)
        {
            this.IgnoreAck();
        }

        [Fact]
        public void WhenSchedulingFutureMessage_FiresMessageAfterElapsedTime()
        {
            this.ScheduleFutureMessage("payload", 1000);
            this.ExpectDelayedCallback("payload", 1000);
        }

        [Fact]
        public void WhenRecallingFutureMessage_DoesNotFire()
        {
            this.ScheduleFutureMessage("payload", 1000, "ToBeRecalled");
            this.ExpectNoMsg(500);
            this.RecallMessage("ToBeRecalled");
            this.ExpectNoMsg(1000);
        }

        [Fact]
        public void WhenUpdatingFireTimeAbsolutely_FiresAtNewFireTime()
        {
            var originalFireTime = this.ScheduleFutureMessage("payload", 5000, "ToBeUpdated");
            this.ExpectNoMsg(500);
            var newFireTime = originalFireTime.AddSeconds(-3);
            this.UpdateFireTimeAbsolute("ToBeUpdated", newFireTime);
            this.ExpectDelayedCallback("payload", 2000);
            this.ExpectNoMsg(3000);
        }

        [Fact]
        public void WhenUpdatingFireTimeRelatively_FiresAtNewFireTime()
        {
            this.ScheduleFutureMessage("payload", 5000, "ToBeUpdated");
            this.ExpectNoMsg(500);
            this.UpdateFireTimeRelative("ToBeUpdated", -3000);
            this.ExpectDelayedCallback("payload", 2000);
            this.ExpectNoMsg(3000);
        }
    }
}
