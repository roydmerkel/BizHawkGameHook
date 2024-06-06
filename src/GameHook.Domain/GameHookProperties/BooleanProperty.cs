using GameHook.Domain.Interfaces;

namespace GameHook.Domain.GameHookProperties
{
    public class BooleanProperty(IGameHookInstance instance, PropertyAttributes variables) : GameHookProperty(instance, variables), IGameHookProperty
    {
        protected override byte[] FromValue(string value)
        {
            var booleanValue = bool.Parse(value);
            return booleanValue == true ? [0x01] : [0x00];
        }

        protected override object? ToValue(byte[] data)
        {
            return data[0] != 0x00;
        }
    }
}
