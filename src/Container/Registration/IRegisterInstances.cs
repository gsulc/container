using ObjectBuilder2;
using System;
using System.Collections.Generic;

namespace Unity.Container.Registration
{
    /// <summary>
    /// This interface allows strategies in BuildUp chain
    /// to participate in type registration process.
    /// </summary>
    public interface IRegisterInstances
    {
        IEnumerable<IBuilderPolicy> OnRegisterInstance(Type type, string name, object instance, LifetimeManager lifetimeManager);
    }
}
