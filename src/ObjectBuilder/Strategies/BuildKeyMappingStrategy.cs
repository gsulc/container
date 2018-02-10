using System;
using System.Linq;
using System.Reflection;
using Unity.Builder;
using Unity.Builder.Strategy;
using Unity.Injection;
using Unity.Lifetime;
using Unity.ObjectBuilder.Policies;
using Unity.Policy;
using Unity.Registration;
using Unity.Strategy;

namespace Unity.ObjectBuilder.Strategies
{
    /// <summary>
    /// Represents a strategy for mapping build keys in the build up operation.
    /// </summary>
    public class BuildKeyMappingStrategy : BuilderStrategy, IRegisterTypeStrategy
    {
        /// <summary>
        /// Called during the chain of responsibility for a build operation.  Looks for the <see cref="IBuildKeyMappingPolicy"/>
        /// and if found maps the build key for the current operation.
        /// </summary>
        /// <param name="context">The context for the operation.</param>
        public override object PreBuildUp(IBuilderContext context)
        {
            IBuildKeyMappingPolicy policy = context.Policies.Get<IBuildKeyMappingPolicy>(context.OriginalBuildKey.Type,
                                                                                         context.OriginalBuildKey.Name, out _)
                                          ?? (context.OriginalBuildKey.Type.GetTypeInfo().IsGenericType
                                          ? context.Policies.Get<IBuildKeyMappingPolicy>(context.OriginalBuildKey.Type.GetGenericTypeDefinition(),
                                                                                         context.OriginalBuildKey.Name, out _)
                                          : null);

            if (null == policy) return null;

            context.BuildKey = policy.Map(context.BuildKey, context);

            return null;
        }

        public void RegisterType(IContainerContext context, Type registeredType, string name, Type mappedTo, 
                                 LifetimeManager lifetimeManager, params InjectionMember[] injectionMembers)
        {
            if (null == mappedTo || registeredType == mappedTo)
            {
                context.Policies.Clear(mappedTo, name, typeof(IBuildKeyMappingPolicy));
                return;
            }

            if (registeredType.GetTypeInfo().IsGenericTypeDefinition && mappedTo.GetTypeInfo().IsGenericTypeDefinition)
            {
                context.Policies.Set<IBuildKeyMappingPolicy>(new GenericTypeBuildKeyMappingPolicy(new NamedTypeBuildKey(mappedTo, name)), 
                                                                                                  new NamedTypeBuildKey(registeredType, name));
            }
            else
            {
                context.Policies.Set(registeredType, name, typeof(IBuildKeyMappingPolicy), 
                                     new BuildKeyMappingPolicy(new NamedTypeBuildKey(mappedTo, name)));
            }

            var members = null == injectionMembers ? new InjectionMember[0] : injectionMembers;
            if (!members.Where(m => m is InjectionConstructor || m is InjectionMethod || m is InjectionProperty).Any() && !(lifetimeManager is IRequireBuildUpPolicy))
                context.Policies.Set(registeredType, name, typeof(IBuildPlanPolicy), new ResolveBuildUpPolicy());
        }
    }
}
