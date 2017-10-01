// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ObjectBuilder2;

namespace Unity
{
    /// <summary>
    /// Class that returns information about the types registered in a container.
    /// </summary>
    public class ContainerRegistration : IPolicyList
    {
        #region Fields

        private readonly object _lock = new object();
        private readonly bool _isUserType;  // TODO: DO we need it at all
        private readonly NamedTypeBuildKey _buildKey;
        private readonly IPolicyList _defaults;
        private IBuilderPolicy[] _policies;

        #endregion


        #region Constructors

        public ContainerRegistration(NamedTypeBuildKey key, IPolicyList defaultPolicies, IEnumerable<IBuilderPolicy> policies = null)
        {
            _buildKey = key;
            _defaults = defaultPolicies;
            _isUserType = null != policies;
            _policies = policies?.ToArray() ?? new IBuilderPolicy[] { TransientLifetimeManager.Instance,
                                                                  new OverriddenBuildPlanMarkerPolicy() };
        }

        public ContainerRegistration(Type registeredType, string name, IPolicyList policies)
        {
            _buildKey = new NamedTypeBuildKey(registeredType, name);
            MappedToType = GetMappedType(policies);
            LifetimeManagerType = GetLifetimeManagerType(policies);
            LifetimeManager = GetLifetimeManager(policies);
        }

        #endregion


        #region ContainerRegistration

        /// <summary>
        /// The type that was passed to the <see cref="IUnityContainer.RegisterType"/> method
        /// as the "from" type, or the only type if type mapping wasn't done.
        /// </summary>
        public Type RegisteredType => _buildKey.Type;

        /// <summary>
        /// The type that this registration is mapped to. If no type mapping was done, the
        /// <see cref="RegisteredType"/> property and this one will have the same value.
        /// </summary>
        public Type MappedToType { get; }

        /// <summary>
        /// Name the type was registered under. Null for default registration.
        /// </summary>
        public string Name => _buildKey.Name;

        /// <summary>
        /// The registered lifetime manager instance.
        /// </summary>
        public Type LifetimeManagerType { get; }

        /// <summary>
        /// The lifetime manager for this registration.
        /// </summary>
        /// <remarks>
        /// This property will be null if this registration is for an open generic.</remarks>
        public LifetimeManager LifetimeManager { get; }

        private Type GetMappedType(IPolicyList policies)
        {
            var mappingPolicy = policies.Get<IBuildKeyMappingPolicy>(_buildKey);
            if (mappingPolicy != null)
            {
                return mappingPolicy.Map(_buildKey, null).Type;
            }
            return _buildKey.Type;
        }

        private Type GetLifetimeManagerType(IPolicyList policies)
        {
            var key = new NamedTypeBuildKey(RegisteredType, Name);
            var lifetime = policies.Get<ILifetimePolicy>(key);

            if (lifetime != null)
            {
                return lifetime.GetType();
            }

            if (RegisteredType.GetTypeInfo().IsGenericType)
            {
                var genericKey = new NamedTypeBuildKey(RegisteredType.GetGenericTypeDefinition(), Name);
                var lifetimeFactory = policies.Get<ILifetimeFactoryPolicy>(genericKey);
                if (lifetimeFactory != null)
                {
                    return lifetimeFactory.LifetimeType;
                }
            }

            return typeof(TransientLifetimeManager);
        }

        private LifetimeManager GetLifetimeManager(IPolicyList policies)
        {
            var key = new NamedTypeBuildKey(RegisteredType, Name);
            return (LifetimeManager)policies.Get<ILifetimePolicy>(key);
        }

        #endregion


        #region IPolicyList

        public void Clear(Type policyInterface, object buildKey)
        {
        }

        public void ClearAll()
        {
        }

        public void ClearDefault(Type policyInterface)
        {
        }

        public IBuilderPolicy Get(Type policyInterface, object buildKey, bool localOnly, out IPolicyList containingPolicyList)
        {
            IBuilderPolicy result;

            if (!_buildKey.Equals(buildKey))
                return _defaults.Get(policyInterface, buildKey, localOnly, out containingPolicyList);

            var info = policyInterface.GetTypeInfo();

            lock (_lock)
            {
                result = _policies.FirstOrDefault(p => info.IsAssignableFrom(p.GetType().GetTypeInfo()));
            }

            containingPolicyList = null != result ? this : null; 
            return result;
        }

        public IBuilderPolicy GetNoDefault(Type policyInterface, object buildKey, bool localOnly, out IPolicyList containingPolicyList)
        {
            IBuilderPolicy result;

            if (!_buildKey.Equals(buildKey))
                return _defaults.Get(policyInterface, buildKey, localOnly, out containingPolicyList);

            var info = policyInterface.GetTypeInfo();

            lock (_lock)
            {
                result = _policies.FirstOrDefault(p => info.IsAssignableFrom(p.GetType().GetTypeInfo()));
            }

            containingPolicyList = null != result ? this : null;
            return result;
        }

        public void Set(Type policyInterface, IBuilderPolicy policy, object buildKey)
        {
            var info = policyInterface.GetTypeInfo();

            lock (_lock)
            {
                _policies = Enumerable.Repeat(policy, 1)
                                      .Concat(_policies.Where(p => !info.IsAssignableFrom(p.GetType().GetTypeInfo())))
                                      .ToArray();
            }
        }

        public void SetDefault(Type policyInterface, IBuilderPolicy policy)
        {
            _defaults.Set(policyInterface, policy, _buildKey);
        }

        #endregion
    }
}
