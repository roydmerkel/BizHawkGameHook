using GameHook.Domain;
using GameHook.Domain.Interfaces;
using GameHook.WebAPI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAPI.GameHook;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GameHook.IntegrationTests
{
    public abstract class BaseUnitTest
    {
        protected Serilog.ILogger Logger { get; }

        public BaseUnitTest()
        {
            var testConfiguration = new ConfigurationBuilder().AddJsonFile("testsettings.json").Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(testConfiguration)
                .CreateLogger();

            Logger = Log.Logger;
        }
    }
}
