// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

#if NET45
using Microsoft.Practices.ObjectBuilder2;
#else
using ObjectBuilder2;
#endif

namespace Microsoft.Practices.Unity.Tests.TestDoubles
{
    /// <summary>
    /// A sample policy that gets used by the SpyStrategy
    /// if present to mark execution.
    /// </summary>
    internal class SpyPolicy : IBuilderPolicy
    {
        private bool wasSpiedOn;

        public bool WasSpiedOn
        {
            get { return wasSpiedOn; }
            set { wasSpiedOn = value; }
        }
    }
}
