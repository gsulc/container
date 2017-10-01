// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System;
#if NETFX_CORE
using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
#elif __IOS__
using NUnit.Framework;
using TestClassAttribute = NUnit.Framework.TestFixtureAttribute;
using TestInitializeAttribute = NUnit.Framework.SetUpAttribute;
using TestMethodAttribute = NUnit.Framework.TestAttribute;
#else
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
#if !NET45
using ObjectBuilder2;
using Unity;
#endif

namespace Microsoft.Practices.ObjectBuilder2.Tests
{
    [TestClass]
    public class BuilderContextTest : IBuilderStrategy
    {
        private IBuilderContext parentContext, childContext, receivedContext;
        private bool throwOnBuildUp;

        [TestInitialize]
        public void SetUp()
        {
            this.throwOnBuildUp = false;
        }

        [TestMethod]
        public void NewBuildSetsChildContextWhileBuilding()
        {
#if NET45
        this.parentContext = new BuilderContext(GetNonThrowingStrategyChain(), null, null, null, null, null);
#else
        this.parentContext = new BuilderContext(null, GetNonThrowingStrategyChain(), null, null, null, null, null);
#endif

        this.parentContext.NewBuildUp(null);

            Assert.AreSame(this.childContext, this.receivedContext);
        }

        [TestMethod]
        public void NewBuildClearsTheChildContextOnSuccess()
        {
#if NET45
            this.parentContext = new BuilderContext(GetNonThrowingStrategyChain(), null, null, null, null, null);
#else
            this.parentContext = new BuilderContext(null, GetNonThrowingStrategyChain(), null, null, null, null, null);
#endif

            this.parentContext.NewBuildUp(null);

            Assert.IsNull(this.parentContext.ChildContext);
        }

        [TestMethod]
        public void NewBuildDoesNotClearTheChildContextOnFailure()
        {
#if NET45
            this.parentContext = new BuilderContext(GetNonThrowingStrategyChain(), null, null, null, null, null);
#else
            this.parentContext = new BuilderContext(null, GetNonThrowingStrategyChain(), null, null, null, null, null);
#endif

            try
            {
                this.parentContext.NewBuildUp(null);
                Assert.Fail("an exception should have been thrown here");
            }
            catch (Exception)
            {
                Assert.IsNotNull(this.parentContext.ChildContext);
                Assert.AreSame(this.parentContext.ChildContext, this.receivedContext);
            }
        }

        private StrategyChain GetNonThrowingStrategyChain()
        {
            this.throwOnBuildUp = false;
            return new StrategyChain(new[] { this });
        }

        private StrategyChain GetThrowingStrategyChain()
        {
            this.throwOnBuildUp = true;
            return new StrategyChain(new[] { this });
        }

        public void PreBuildUp(IBuilderContext context)
        {
            this.childContext = this.parentContext.ChildContext;
            this.receivedContext = context;

            if (this.throwOnBuildUp)
            {
                throw new Exception();
            }
        }

        public void PostBuildUp(IBuilderContext context)
        {
        }

        public void PreTearDown(IBuilderContext context)
        {
            throw new NotImplementedException();
        }

        public void PostTearDown(IBuilderContext context)
        {
            throw new NotImplementedException();
        }
    }
}
