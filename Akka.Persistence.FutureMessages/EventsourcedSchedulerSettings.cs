using Akka.Actor;
using Akka.Configuration;
using System;

namespace Akka.Persistence.FutureMessages
{
    public sealed class EventsourcedSchedulerSettings : IExtension
    {
        private EventsourcedSchedulerSettings(int defaultQueueSize, string snapshotStrategyTypeFullName, string acknowledgementStrategyTypeFullName)
        {
            this.DefaultQueueSize = defaultQueueSize;
            this.SnapshotStrategyType = Type.GetType(snapshotStrategyTypeFullName, true);
            this.AcknowledgementStrategyType = Type.GetType(acknowledgementStrategyTypeFullName, true);
        }

        public int DefaultQueueSize { get; }

        public Type SnapshotStrategyType { get; }

        public Type AcknowledgementStrategyType { get; }

        public static Config DefaultConfig => ConfigurationFactory.FromResource<EventsourcedSchedulerSettings>("Akka.Persistence.FutureMessages.default.hocon");

        public static EventsourcedSchedulerSettings Get(ActorSystem system) => system.WithExtension<EventsourcedSchedulerSettings, EventsourcedSchedulerSettingsProvider>();

        public static EventsourcedSchedulerSettings From(Config config)
        {
            return new EventsourcedSchedulerSettings(config.GetInt("default-queue-size"), config.GetString("snapshot-strategy"), config.GetString("acknowledgement-strategy"));
        }
    }

    public class EventsourcedSchedulerSettingsProvider : ExtensionIdProvider<EventsourcedSchedulerSettings>
    {
        public override EventsourcedSchedulerSettings CreateExtension(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(EventsourcedSchedulerSettings.DefaultConfig);
            return EventsourcedSchedulerSettings.From(system.Settings.Config.GetConfig("akka.persistence.eventsourced-scheduler"));
        }
    }
}
