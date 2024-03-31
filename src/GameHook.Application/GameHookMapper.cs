using GameHook.Domain;
using GameHook.Domain.Interfaces;

namespace GameHook.Application
{
    public class GameHookMapper : IGameHookMapper
    {
        public GameHookMapper(
            MetadataSection metadata,
            MemorySection memory,
            IEnumerable<IGameHookProperty> properties,
            IEnumerable<ReferenceItems> references,
            IEnumerable<IGameHookEvent> events)
        {
            Metadata = metadata;
            Memory = memory;
            Properties = properties.ToDictionary(x => x.Path, x => x);
            References = references.ToDictionary(x => x.Name, x => x);
            Events = events.ToDictionary(x => x.Name, x => x);
        }

        public MetadataSection Metadata { get; }
        public MemorySection Memory { get; }
        public Dictionary<string, IGameHookProperty> Properties { get; }
        public Dictionary<string, ReferenceItems> References { get; }
        public Dictionary<string, IGameHookEvent> Events { get; }

        public IGameHookProperty[] GetAllProperties() => Properties.Values.ToArray();
        public IGameHookEvent[] GetAllEvents() => Events.Values.ToArray();
    }
}