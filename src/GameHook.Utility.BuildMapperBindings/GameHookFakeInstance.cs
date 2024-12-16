using GameHook.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NCalc;

namespace GameHook.Utility.BuildMapperBindings
{
    internal class Logger : ILogger<IGameHookInstance>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    };

    internal class GameHookFakeInstance : IGameHookInstance
    {
        public ILogger<IGameHookInstance> Logger => new Logger();

        public bool Initalized => throw new NotImplementedException();

        public Dictionary<string, object?> State => throw new NotImplementedException();

        public List<IClientNotifier> ClientNotifiers => throw new NotImplementedException();

        public IGameHookDriver? Driver => throw new NotImplementedException();

        public IGameHookMapper? Mapper => throw new NotImplementedException();

        public IPlatformOptions? PlatformOptions => throw new NotImplementedException();

        public Dictionary<string, object?> Variables => throw new NotImplementedException();

        public Task Load(IGameHookDriver driver, string mapperId)
        {
            throw new NotImplementedException();
        }

        public object? ExecuteExpression(string? expression, object x)
        {
            throw new NotImplementedException();
        }

        public object? ExecuteModuleFunction(string? function, IGameHookProperty property)
        {
            throw new NotImplementedException();
        }

        public bool? GetModuleFunctionResult(string? function, IGameHookProperty property)
        {
            throw new NotImplementedException();
        }
    }
}
