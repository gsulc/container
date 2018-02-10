using System;
using System.Reflection;
using Unity.Builder;
using Unity.Builder.Strategy;
using Unity.Exceptions;
using Unity.Extension;
using Unity.Lifetime;
using Unity.Policy;
using Unity.Registration;
using Unity.Strategy;

namespace Unity.ObjectBuilder.Strategies
{
    /// <summary>
    /// An <see cref="IBuilderStrategy"/> implementation that uses
    /// a <see cref="ILifetimePolicy"/> to figure out if an object
    /// has already been created and to update or remove that
    /// object from some backing store.
    /// </summary>
    public class LifetimeStrategy : BuilderStrategy, IRegisterTypeStrategy
    {
        #region Fields

        private readonly object _genericLifetimeManagerLock = new object();

        #endregion


        #region BuilderStrategy

        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PreBuildUp method is called when the chain is being executed in the
        /// forward direction.
        /// </summary>
        /// <param name="context">Context of the build operation.</param>
        public override object PreBuildUp(IBuilderContext context)
        {
            if (context.Existing != null) return null;

            var lifetimePolicy = GetLifetimePolicy(context, out var _);
            if (lifetimePolicy is IRequiresRecovery recovery)
            {
                context.RecoveryStack.Add(recovery);
            }

            var existing = lifetimePolicy?.GetValue(context);
            if (existing != null)
            {
                context.Existing = existing;
                context.BuildComplete = true;
            }
            return null;
        }

        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PostBuildUp method is called when the chain has finished the PreBuildUp
        /// phase and executes in reverse order from the PreBuildUp calls.
        /// </summary>
        /// <param name="context">Context of the build operation.</param>
        public override void PostBuildUp(IBuilderContext context, object pre = null)
        {
            // If we got to this method, then we know the lifetime policy didn't
            // find the object. So we go ahead and store it.
            ILifetimePolicy lifetimePolicy = GetLifetimePolicy(context, out IPolicyList _);
            lifetimePolicy.SetValue(context.Existing, context);
        }

        private ILifetimePolicy GetLifetimePolicy(IBuilderContext context, out IPolicyList source)
        {
            ILifetimePolicy policy = (ILifetimePolicy)context.Policies.Get(context.OriginalBuildKey.Type, 
                                                                           context.OriginalBuildKey.Name, 
                                                                           typeof(ILifetimePolicy), out source);
            if (policy == null && context.OriginalBuildKey.Type.GetTypeInfo().IsGenericType)
            {
                policy = GetLifetimePolicyForGenericType(context, out source);
            }

            if (policy == null)
            {
                policy = TransientLifetimeManager.Instance;
                context.PersistentPolicies.Set(policy, context.OriginalBuildKey);
            }

            return policy;
        }

        private ILifetimePolicy GetLifetimePolicyForGenericType(IBuilderContext context, out IPolicyList factorySource)
        {
            var factoryPolicy = context.Policies
                                       .Get<ILifetimeFactoryPolicy>(context.OriginalBuildKey.Type.GetGenericTypeDefinition(),
                                                                    context.OriginalBuildKey.Name,
                                                                    out factorySource);
            if (factoryPolicy != null)
            {
                ILifetimePolicy newLifetime = factoryPolicy.CreateLifetimePolicy();

                lock (_genericLifetimeManagerLock)
                {
                    // check whether the policy for closed-generic has been added since first checked
                    var lifetime = factorySource.Get<ILifetimePolicy>(context.BuildKey.Type, context.BuildKey.Name);
                    if (lifetime == null)
                    {
                        factorySource.Set(newLifetime, context.BuildKey);
                        lifetime = newLifetime;
                    }

                    return lifetime;
                }
            }

            return null;
        }

        #endregion


        #region IRegisterTypeStrategy

        public void RegisterType(IContainerContext context, Type registeredType, string name, Type mappedTo, 
                                 LifetimeManager lifetimeManager, params InjectionMember[] injectionMembers)
        {
            if (null == lifetimeManager)
            {
                context.Policies.Clear(registeredType, name, typeof(ILifetimePolicy));
                return;
            }

            if (lifetimeManager.InUse)
            {
                throw new InvalidOperationException(Constants.LifetimeManagerInUse);
            }

            if (registeredType.GetTypeInfo().IsGenericTypeDefinition)
            {
                LifetimeManagerFactory factory = new LifetimeManagerFactory((ExtensionContext)context, lifetimeManager);
                context.Policies.Set<ILifetimeFactoryPolicy>(factory, new NamedTypeBuildKey(registeredType, name));
            }
            else
            {
                lifetimeManager.InUse = true;
                context.Policies.Set<ILifetimePolicy>(lifetimeManager, new NamedTypeBuildKey(registeredType, name));
                if (lifetimeManager is IDisposable)
                {
                    context.Lifetime.Add(lifetimeManager);
                }
            }
        }

        #endregion
    }
}
