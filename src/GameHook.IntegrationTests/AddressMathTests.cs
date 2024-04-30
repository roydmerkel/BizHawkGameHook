﻿using GameHook.Domain;
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
        public async Task AddressMath_TrySolve_OK()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            uint testAddress = 0;
            var res = AddressMath.TrySolve("1234", variables, out testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == 1234);
        }
        [TestMethod]
        public async Task AddressMath_TrySolve_Arithmatic()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            uint testAddress = 0;
            var res = AddressMath.TrySolve("1234+4567", variables, out testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == (1234+4567));
        }
        [TestMethod]
        public async Task AddressMath_TrySolve_Variable()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            variables["abc"] = 1234;
            uint testAddress = 0;
            var res = AddressMath.TrySolve("abc", variables, out testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == (1234));
        }
        [TestMethod]
        public async Task AddressMath_TrySolve_Variable_Arithmatic()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            variables["abc"] = 1234;
            uint testAddress = 0;
            var res = AddressMath.TrySolve("abc+4567", variables, out testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == (1234+4567));
        }
        [TestMethod]
        public async Task AddressMath_TrySolve_Variable_Arithmatic2()
        {
            Dictionary<string, object?> variables = new Dictionary<string, object?>();
            variables["abc"] = 1234;
            variables["def"] = 4567;
            uint testAddress = 0;
            var res = AddressMath.TrySolve("abc+def", variables, out testAddress);
            Assert.IsTrue(res);
            Assert.IsTrue(testAddress == (1234 + 4567));
        }
    }
}