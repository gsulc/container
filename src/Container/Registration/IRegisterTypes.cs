using ObjectBuilder2;
using System;
using System.Collections.Generic;

namespace Unity.Container.Registration
{
    /// <summary>
    /// This interface allows strategies in BuildUp chain
    /// to participate in type registration process.
    /// </summary>
    public interface IRegisterTypes
    {
        /// <summary>
        /// This method is called to allow strategy to prepare and return corresponding registration policy 
        /// </summary>
        /// <param name="typeFrom"><see cref="Type"/> that will be requested.</param>
        /// <param name="typeTo"><see cref="Type"/> that will actually be returned.</param>
        /// <param name="name">Name to use for registration, null if a default registration.</param>
        /// <param name="lifetimeManager">The <see cref="LifetimeManager"/> that controls the lifetime
        /// of the returned instance.</param>
        /// <param name="injectionMembers">Injection configuration objects.</param>
        /// <returns>The <see cref="UnityContainer"/> object that this method was called on (this in C#, Me in Visual Basic).</returns>
        IEnumerable<IBuilderPolicy> OnRegisterType(Type typeFrom, Type typeTo, string name, LifetimeManager lifetimeManager, InjectionMember[] injectionMembers);
    }
}
