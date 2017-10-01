// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity;
using Unity.Container.Registration;
using Unity.Properties;

namespace ObjectBuilder2
{
    /// <summary>
    /// An <see cref="IBuilderStrategy"/> implementation that uses
    /// a <see cref="ILifetimePolicy"/> to figure out if an object
    /// has already been created and to update or remove that
    /// object from some backing store.
    /// </summary>
    public class LifetimeStrategy : BuilderUpStrategy, IRegisterTypes, IRegisterInstances
    {
        private readonly object _genericLifetimeManagerLock = new object();


        #region Registerations

        public IEnumerable<IBuilderPolicy> OnRegisterType(Type from, Type to, string name, LifetimeManager lifetimeManager, InjectionMember[] injectionMembers)
        {
            if (null == lifetimeManager) return Enumerable.Empty<IBuilderPolicy>();

            if (lifetimeManager.InUse)
                throw new InvalidOperationException(Resources.LifetimeManagerInUse);

            lifetimeManager.InUse = true;
            if (lifetimeManager is IDisposable)
                ContainerContext.Lifetime.Add(lifetimeManager);

            return new[] { lifetimeManager };
        }

        public IEnumerable<IBuilderPolicy> OnRegisterInstance(Type type, string name, object instance, LifetimeManager lifetimeManager)
        {
            if (null == lifetimeManager) return Enumerable.Empty<IBuilderPolicy>();

            if (lifetimeManager.InUse)
                throw new InvalidOperationException(Resources.LifetimeManagerInUse);

            lifetimeManager.InUse = true;
            lifetimeManager.SetValue(instance);

            if (lifetimeManager is IDisposable)
                ContainerContext.Lifetime.Add(lifetimeManager);

            return new[] { lifetimeManager };
        }

        #endregion


        #region BuilderStrategy

        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PreBuildUp method is called when the chain is being executed in the
        /// forward direction.
        /// </summary>
        /// <param name="builderContext">Context of the build operation.</param>
        // FxCop suppression: Validation is done by Guard class
        public override void PreBuildUp(IBuilderContext builderContext)
        {
            var context = builderContext ?? throw new ArgumentNullException(nameof(builderContext));

            if (context.Existing != null) return;

            var lifetimePolicy = GetLifetimePolicy(context, out var containingPolicyList);

            if (null == lifetimePolicy) return;

            if (lifetimePolicy is IScopeLifetimePolicy scope &&  
                !ReferenceEquals(containingPolicyList, context.PersistentPolicies))
            {
                lifetimePolicy = scope.CreateScope() as ILifetimePolicy;
                context.PersistentPolicies.Set(lifetimePolicy, context.BuildKey);
                context.Lifetime.Add(lifetimePolicy);
            }

            if (lifetimePolicy is IRequiresRecovery recovery)
            {
                context.RecoveryStack.Add(recovery);
            }

            var existing = lifetimePolicy?.GetValue();
            if (existing != null)
            {
                context.Existing = existing;
                context.BuildComplete = true;
            }
        }

        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PostBuildUp method is called when the chain has finished the PreBuildUp
        /// phase and executes in reverse order from the PreBuildUp calls.
        /// </summary>
        /// <param name="builderContext">Context of the build operation.</param>
        public override void PostBuildUp(IBuilderContext builderContext)
        {
            var context = builderContext ?? throw new ArgumentNullException(nameof(builderContext));
            var lifetimePolicy = context.Policies.Get<ILifetimePolicy>(context.OriginalBuildKey);
            lifetimePolicy?.SetValue(context.Existing);
        }

        private ILifetimePolicy GetLifetimePolicy(IBuilderContext context, out IPolicyList containingPolicyList)
        {
            var policy = context.Policies.Get<ILifetimePolicy>(context.BuildKey, out containingPolicyList);
            if (policy == null && context.BuildKey.Type.GetTypeInfo().IsGenericType)
            {
                policy = GetLifetimePolicyForGenericType(context, out containingPolicyList);
            }

            return policy;
        }

        private ILifetimePolicy GetLifetimePolicyForGenericType(IBuilderContext context, out IPolicyList containingPolicyList)
        {
            Type typeToBuild = context.BuildKey.Type;
            object openGenericBuildKey = new NamedTypeBuildKey(typeToBuild.GetGenericTypeDefinition(),
                                                               context.BuildKey.Name);

            ILifetimeFactoryPolicy factoryPolicy =
                context.Policies.Get<ILifetimeFactoryPolicy>(openGenericBuildKey, out containingPolicyList);

            if (factoryPolicy != null)
            {
                // creating the lifetime policy can result in arbitrary code execution
                // in particular it will likely result in a Resolve call, which could result in locking
                // to avoid deadlocks the new lifetime policy is created outside the lock
                // multiple instances might be created, but only one instance will be used
                ILifetimePolicy newLifetime = factoryPolicy.CreateLifetimePolicy();

                lock (this._genericLifetimeManagerLock)
                {
                    // check whether the policy for closed-generic has been added since first checked
                    ILifetimePolicy lifetime = containingPolicyList.GetNoDefault<ILifetimePolicy>(context.BuildKey, false);
                    if (lifetime == null)
                    {
                        containingPolicyList.Set(newLifetime, context.BuildKey);
                        lifetime = newLifetime;
                    }

                    return lifetime;
                }
            }

            return null;
        }

        #endregion
    }
}
