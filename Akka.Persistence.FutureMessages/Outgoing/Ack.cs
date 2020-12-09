namespace Akka.Persistence.FutureMessages.Outgoing
{
    public class Ack
    {
        internal Ack(string id)
        {
            Id = id;
        }

        public string Id { get; }
    }

    public class Ack<T> : Ack
    {
        internal Ack(string id) : base(id)
        {
        }
    }
}
