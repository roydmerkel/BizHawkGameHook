using GameHook.Domain.Interfaces;
using System.Collections;

namespace GameHook.Domain.GameHookEvents
{
    public abstract partial class GameHookEvent : IGameHookEvent
    {
        public GameHookEvent(IGameHookInstance instance, EventAttributes attributes)
        {
            Instance = instance;
            Name = attributes.Name;
            EventType = attributes.EventType;

            MemoryContainer = attributes.MemoryContainer;
            AddressString = attributes.Address;
            Description = attributes.Description;
            EventRegisterOverrides = attributes?.EventRegisterOverrides.ToArray() ?? [];
            Property = attributes.Property;
        }

        protected IGameHookInstance Instance { get; }
        public string Name { get; }
        public EventType EventType { get; }
        public EventRegisterOverride[] EventRegisterOverrides { get; }

        private bool IsMemoryAddressSolved { get; set; }

        public HashSet<string> FieldsChanged { get; } = [];
        public IGameHookProperty? Property { get; }

        public void ProcessLoop(IMemoryManager memoryManager)
        {
            if (Instance == null) { throw new Exception("Instance is NULL."); }
            if (Instance.Mapper == null) { throw new Exception("Instance.Mapper is NULL."); }
            if (Instance.Driver == null) { throw new Exception("Instance.Driver is NULL."); }

            MemoryAddress? address = Address;

            if (string.IsNullOrEmpty(_addressString) == false && IsMemoryAddressSolved == false)
            {
                if (AddressMath.TrySolve(_addressString, Instance.Variables, out var solvedAddress))
                {
                    address = solvedAddress;
                }
                else
                {
                    // TODO: Write a log entry here.
                }
            }

            if (address == null)
            {
                // There is nothing to do for this property, as it does not have an address or bytes.
                // Hopefully a postprocessor will pick it up and set it's value!

                return;
            }

            Address = address;
        }

        public abstract void ClearEvent(MemoryAddress address);
        public abstract void SetEvent(MemoryAddress address);

        public void UpdateAddressFromProperty()
        {
            if(Property != null)
            {
                if(Property.Address == null && _oldAddress != null)
                {
                    ClearEvent(_oldAddress.Value);
                    _oldAddress = Property.Address;
                } 
                else if(Property.Address != null && _oldAddress == null)
                {
                    _oldAddress = Property.Address;
                    SetEvent(Property.Address.Value);
                }
                else if(Property.Address != null && Property.Address.Value != _oldAddress.Value)
                {
                    ClearEvent(_oldAddress.Value);
                    _oldAddress = Property.Address;
                    SetEvent(Property.Address.Value);
                }
            }
        }
    }
}