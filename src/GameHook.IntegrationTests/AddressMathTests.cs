using GameHook.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameHook.IntegrationTests
{
    [TestClass]
    public class AddressMathTests : BaseUnitTest
    {
        // Address Math tests
        [TestMethod]
        public Task AddressMath_TrySolve_OK()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            var res = AddressMath.TrySolve("1234", variables, out uint testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == 1234);

            return Task.CompletedTask;
        }
        [TestMethod]
        public Task AddressMath_TrySolve_Arithmatic()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            var res = AddressMath.TrySolve("1234+4567", variables, out uint testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == (1234+4567));

            return Task.CompletedTask;
        }
        [TestMethod]
        public Task AddressMath_TrySolve_Variable()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            variables["abc"] = 1234;
            var res = AddressMath.TrySolve("abc", variables, out uint testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == (1234));

            return Task.CompletedTask;
        }
        [TestMethod]
        public Task AddressMath_TrySolve_Variable_Arithmatic()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            variables["abc"] = 1234;
            var res = AddressMath.TrySolve("abc+4567", variables, out uint testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == (1234+4567));

            return Task.CompletedTask;
        }
        [TestMethod]
        public Task AddressMath_TrySolve_Variable_Arithmatic2()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            variables["abc"] = 1234;
            variables["def"] = 4567;
            var res = AddressMath.TrySolve("abc+def", variables, out uint testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == (1234 + 4567));

            return Task.CompletedTask;
        }
    }
}
