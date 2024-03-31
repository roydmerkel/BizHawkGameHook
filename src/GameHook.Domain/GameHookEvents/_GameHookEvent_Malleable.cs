using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookEvents
{
    public abstract partial class GameHookEvent : IGameHookEvent
    {
        private string? _memoryContainer { get; set; }
        private uint? _address { get; set; }
        private uint? _oldAddress { get; set; }
        private string? _addressString { get; set; }
        private string? _description { get; set; }

        public string? MemoryContainer
        {
            get { return _memoryContainer; }
            set
            {
                if (value == _memoryContainer) { return; }

                FieldsChanged.Add("memoryContainer");
                _memoryContainer = value;
            }
        }

        public uint? Address
        {
            get { return _address; }
            set
            {
                if (value == _address) { return; }

                _address = value;
                _addressString = value.ToString();

                IsMemoryAddressSolved = true;
                if (_address == null && _oldAddress != null)
                {
                    ClearEvent(_oldAddress.Value);
                    _oldAddress = _address;
                }
                else if (_address != null && _oldAddress == null)
                {
                    _oldAddress = _address;
                    SetEvent(_address.Value);
                }
                else if (_address != null && _oldAddress != _address)
                {
                    ClearEvent(_oldAddress.Value);
                    _oldAddress = _address;
                    SetEvent(_address.Value);
                }

                FieldsChanged.Add("address");
            }
        }

        public string? AddressString
        {
            get { return _addressString; }
            set
            {
                if (value == _addressString) { return; }

                _addressString = value;

                IsMemoryAddressSolved = AddressMath.TrySolve(value, [], out var solvedAddress);

                if (IsMemoryAddressSolved == false)
                {
                    _address = null;
                }
                else
                {
                    _address = solvedAddress;
                }

                if (_address == null && _oldAddress != null)
                {
                    ClearEvent(_oldAddress.Value);
                    _oldAddress = _address;
                }
                else if (_address != null && _oldAddress == null)
                {
                    _oldAddress = _address;
                    SetEvent(_address.Value);
                }
                else if (_address != null && _oldAddress != _address)
                {
                    ClearEvent(_oldAddress.Value);
                    _oldAddress = _address;
                    SetEvent(_address.Value);
                }

                FieldsChanged.Add("address");
            }
        }

        public string? Description
        {
            get => _description;
            set
            {
                if (_description == value) return;

                FieldsChanged.Add("description");
                _description = value;
            }
        }
    }
}
