using GameHook.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace GameHook.Domain.GameHookProperties
{
    public class BooleanProperty(ILogger logger, IGameHookInstance instance, PropertyAttributes variables) : GameHookProperty(logger, instance, variables), IGameHookProperty
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
