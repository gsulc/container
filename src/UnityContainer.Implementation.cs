﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Unity.Builder;
using Unity.Builder.Selection;
using Unity.Builder.Strategy;
using Unity.Container;
using Unity.Container.Lifetime;
using Unity.Container.Registration;
using Unity.Events;
using Unity.Extension;
using Unity.Lifetime;
using Unity.ObjectBuilder.BuildPlan;
using Unity.ObjectBuilder.BuildPlan.DynamicMethod;
using Unity.ObjectBuilder.BuildPlan.Selection;
using Unity.ObjectBuilder.Policies;
using Unity.ObjectBuilder.Strategies;
using Unity.Policy;
using Unity.Strategy;

namespace Unity
{
    public partial class UnityContainer
    {
        #region Fields

        private LifetimeContainer _lifetimeContainer;
        private readonly PolicyList _policies;
        private readonly UnityContainer _parent;
        private readonly NamedTypesRegistry _registeredNames;
        private readonly List<UnityContainerExtension> _extensions;
        private readonly StagedStrategyChain<UnityBuildStage> _strategies;
        private readonly StagedStrategyChain<BuilderStage> _buildPlanStrategies;
        private readonly ContainerContext _context;

        private event EventHandler<RegisterEventArgs> Registering;
        private event EventHandler<RegisterInstanceEventArgs> RegisteringInstance;
        private event EventHandler<ChildContainerCreatedEventArgs> ChildContainerCreated;

        // Caches
        private IRegisterTypeStrategy[] _registerTypeStrategies;

        #endregion


        #region Constructors

        /// <summary>
        /// Create a <see cref="Unity.UnityContainer"/> with the given parent container.
        /// </summary>
        /// <param name="parent">The parent <see cref="Unity.UnityContainer"/>. The current object
        /// will apply its own settings first, and then check the parent for additional ones.</param>
        private UnityContainer(UnityContainer parent)
        {
            _extensions = new List<UnityContainerExtension>();

            _parent = parent;
            _parent?._lifetimeContainer.Add(this);
            _context = new ContainerContext(this);
            _strategies = new StagedStrategyChain<UnityBuildStage>(_parent?._strategies);
            _buildPlanStrategies = new StagedStrategyChain<BuilderStage>(_parent?._buildPlanStrategies);
            _registeredNames = new NamedTypesRegistry(_parent?._registeredNames);
            _lifetimeContainer = new LifetimeContainer { _strategies, _buildPlanStrategies };
            _policies = new PolicyList(_parent?._policies);
            _policies.Set<IRegisteredNamesPolicy>(new RegisteredNamesPolicy(_registeredNames), null);

            if (null == _parent) InitializeStrategies();

            // Caches
            OnStrategiesChanged(this, null);
            _strategies.Invalidated += OnStrategiesChanged;
        }

        #endregion


        #region Default Strategies

        protected void InitializeStrategies()
        {
            // Main strategy chain
            _strategies.AddNew<BuildKeyMappingStrategy>(UnityBuildStage.TypeMapping);
            _strategies.AddNew<LifetimeStrategy>(UnityBuildStage.Lifetime);

            _strategies.AddNew<ArrayResolutionStrategy>(UnityBuildStage.Creation);
            _strategies.AddNew<BuildPlanStrategy>(UnityBuildStage.Creation);

            // Build plan strategy chain
            _buildPlanStrategies.AddNew<DynamicMethodConstructorStrategy>(BuilderStage.Creation);
            _buildPlanStrategies.AddNew<DynamicMethodPropertySetterStrategy>(BuilderStage.Initialization);
            _buildPlanStrategies.AddNew<DynamicMethodCallStrategy>(BuilderStage.Initialization);

            // Policies - mostly used by the build plan strategies
            _policies.SetDefault<IConstructorSelectorPolicy>(new DefaultUnityConstructorSelectorPolicy());
            _policies.SetDefault<IPropertySelectorPolicy>(new DefaultUnityPropertySelectorPolicy());
            _policies.SetDefault<IMethodSelectorPolicy>(new DefaultUnityMethodSelectorPolicy());
            _policies.SetDefault<IBuildPlanCreatorPolicy>(new DynamicMethodBuildPlanCreatorPolicy(_buildPlanStrategies));
            _policies.Set<IBuildPlanPolicy>(new DeferredResolveBuildPlanPolicy(), typeof(Func<>));
            _policies.Set<ILifetimePolicy>(new PerResolveLifetimeManager(), typeof(Func<>));
            _policies.Set<IBuildPlanCreatorPolicy>(new LazyDynamicMethodBuildPlanCreatorPolicy(), typeof(Lazy<>));
            _policies.Set<IBuildPlanCreatorPolicy>(new EnumerableDynamicMethodBuildPlanCreatorPolicy(), typeof(IEnumerable<>));

            RegisterInstance(typeof(IUnityContainer), null, this, new ContainerLifetimeManager());
        }


        private void SetLifetimeManager(Type lifetimeType, string name, LifetimeManager lifetimeManager)
        {
            if (lifetimeManager.InUse)
            {
                throw new InvalidOperationException(Constants.LifetimeManagerInUse);
            }

            if (lifetimeType.GetTypeInfo().IsGenericTypeDefinition)
            {
                LifetimeManagerFactory factory =
                    new LifetimeManagerFactory(new ContainerContext(this), lifetimeManager.GetType());
                _policies.Set<ILifetimeFactoryPolicy>(factory,
                    new NamedTypeBuildKey(lifetimeType, name));
            }
            else
            {
                lifetimeManager.InUse = true;
                _policies.Set<ILifetimePolicy>(lifetimeManager,
                    new NamedTypeBuildKey(lifetimeType, name));
                if (lifetimeManager is IDisposable)
                {
                    _lifetimeContainer.Add(lifetimeManager);
                }
            }
        }

        #endregion


        #region Implementation

        private UnityContainer GetRootContainer()
        {
            UnityContainer container;

            for (container = this; container._parent != null; container = container._parent) ;

            return container;
        }

        private void OnStrategiesChanged(object sender, EventArgs e)
        {
            _registerTypeStrategies = _strategies.OfType<IRegisterTypeStrategy>().ToArray();
        }

        /// <summary>
        /// Verifies that an argument instance is assignable from the provided type (meaning
        /// interfaces are implemented, or classes exist in the base class hierarchy, or instance can be 
        /// assigned through a runtime wrapper, as is the case for COM Objects).
        /// </summary>
        /// <param name="assignmentTargetType">The argument type that will be assigned to.</param>
        /// <param name="assignmentInstance">The instance that will be assigned.</param>
        /// <param name="argumentName">Argument name.</param>
        private static void InstanceIsAssignable(Type assignmentTargetType, object assignmentInstance, string argumentName)
        {
            if (!(assignmentTargetType ?? throw new ArgumentNullException(nameof(assignmentTargetType)))
                .GetTypeInfo().IsAssignableFrom((assignmentInstance ?? throw new ArgumentNullException(nameof(assignmentInstance))).GetType().GetTypeInfo()))
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Constants.TypesAreNotAssignable,
                        assignmentTargetType, GetTypeName(assignmentInstance)),
                    argumentName);
            }
        }

        private static string GetTypeName(object assignmentInstance)
        {
            string assignmentInstanceType;
            try
            {
                assignmentInstanceType = assignmentInstance.GetType().FullName;
            }
            catch (Exception)
            {
                assignmentInstanceType = Constants.UnknownType;
            }

            return assignmentInstanceType;
        }

        #endregion


        #region Nested Types

        /// <summary>
        /// Implementation of the ExtensionContext that is actually used
        /// by the UnityContainer implementation.
        /// </summary>
        /// <remarks>
        /// This is a nested class so that it can access state in the
        /// container that would otherwise be inaccessible.
        /// </remarks>
        private class ContainerContext : ExtensionContext, IContainerContext
        {
            private readonly UnityContainer _container;

            public ContainerContext(UnityContainer container)
            {
                _container = container ?? throw new ArgumentNullException(nameof(container));
            }

            public override IUnityContainer Container => _container;

            public override IStagedStrategyChain<IBuilderStrategy, UnityBuildStage> Strategies => _container._strategies;

            public override IStagedStrategyChain<IBuilderStrategy, BuilderStage> BuildPlanStrategies => _container._buildPlanStrategies;

            public override IPolicyList Policies => _container._policies;

            public override ILifetimeContainer Lifetime => _container._lifetimeContainer;

            public override IStagedStrategyChain<ISelectConstructor, SelectorStage> ConstructorSelectStrategies => throw new NotImplementedException();

            public override IStagedStrategyChain<ISelectProperties, SelectorStage> PropertySelectStrategies => throw new NotImplementedException();

            public override IStagedStrategyChain<ISelectMethods, SelectorStage> MethodSelectStrategies => throw new NotImplementedException();

            public override event EventHandler<RegisterEventArgs> Registering
            {
                add => _container.Registering += value;
                remove => _container.Registering -= value;
            }

            /// <summary>
            /// This event is raised when the <see cref="Unity.UnityContainer.RegisterInstance(Type,string,object,LifetimeManager)"/> method,
            /// or one of its overloads, is called.
            /// </summary>
            public override event EventHandler<RegisterInstanceEventArgs> RegisteringInstance
            {
                add => _container.RegisteringInstance += value;
                remove => _container.RegisteringInstance -= value;
            }

            public override event EventHandler<ChildContainerCreatedEventArgs> ChildContainerCreated
            {
                add => _container.ChildContainerCreated += value;
                remove => _container.ChildContainerCreated -= value;
            }
        }


        // Works like the ExternallyControlledLifetimeManager, but uses regular instead of weak references
        private class ContainerLifetimeManager : LifetimeManager
        {
            public override object GetValue(IBuilderContext context = null)
            {
                return context.Container;
            }

            public override void SetValue(object newValue, IBuilderContext context = null)
            {
            }

            public override void RemoveValue(IBuilderContext context = null)
            {
            }

            protected override LifetimeManager OnCreateLifetimeManager()
            {
                return new ContainerLifetimeManager();
            }
        }


        #endregion
    }
}
