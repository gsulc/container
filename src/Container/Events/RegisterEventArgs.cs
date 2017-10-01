// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System;

namespace Unity
{
    /// <summary>
    /// Event argument class for the <see cref="UnityContainer.Registering"/> event.
    /// </summary>
    public class RegisterEventArgs : NamedEventArgs
    {
        /// <summary>
        /// Create a new instance of <see cref="RegisterEventArgs"/>.
        /// </summary>
        /// <param name="typeFrom">Type to map from.</param>
        /// <param name="typeTo">Type to map to.</param>
        /// <param name="name">Name for the registration.</param>
        /// <param name="lifetimeManager"><see cref="LifetimeManager"/> to manage instances.</param>
        public RegisterEventArgs(Type typeFrom, Type typeTo, string name, LifetimeManager lifetimeManager, InjectionMember[] injectionMembers = null)
            : base(name)
        {
            TypeFrom = typeFrom;
            TypeTo = typeTo;
            LifetimeManager = lifetimeManager;
            InjectionMembers = injectionMembers;
        }

        /// <summary>
        /// Type to map from.
        /// </summary>
        public Type TypeFrom { get; set; }

        /// <summary>
        /// Type to map to.
        /// </summary>
        public Type TypeTo { get; set; }

        /// <summary>
        /// <see cref="LifetimeManager"/> to manage instances.
        /// </summary>
        public LifetimeManager LifetimeManager { get; set; }

        /// <summary>
        /// Array of <see cref="InjectionMember"/>.
        /// </summary>
        public InjectionMember[] InjectionMembers { get; set; }
    }
}
