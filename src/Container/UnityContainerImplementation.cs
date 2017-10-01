using System;
using System.Reflection;
using ObjectBuilder2;
using Unity.Properties;
using System.Linq;
using System.Collections.Generic;
using Unity.ObjectBuilder;
using System.Globalization;
using Unity.Container.Registration;

namespace Unity
{
    // This part contains implementation details of the container
    public partial class UnityContainer
    {
        #region Fields 

        private readonly UnityContainer _parent;
        private readonly IDictionary<IBuildKey, ContainerRegistration> _registrations = new RegistrationCollection();
        private readonly StagedStrategyChain<UnityBuildStage> _strategies;

        private LifetimeContainer _lifetimeContainer;
        private readonly StagedStrategyChain<UnityBuildStage> _buildPlanStrategies;
        private readonly PolicyList _policies;
        private readonly NamedTypesRegistry _registeredNames;
        private readonly List<UnityContainerExtension> _extensions;
        private readonly ExtensionContext _extensionsContext;

        private IStrategyChain _cachedStrategies;
        private readonly object _cachedStrategiesLock;

        private event EventHandler<RegisterEventArgs> Registering;
        private event EventHandler<RegisterInstanceEventArgs> RegisteringInstance;
        private event EventHandler<ChildContainerCreatedEventArgs> ChildContainerCreated = delegate { };

        #endregion


        #region Constructors

        /// <summary>
        /// Create a default <see cref="UnityContainer"/>.
        /// </summary>
        public UnityContainer()
            : this(null)
        {
        }

        /// <summary>
        /// Create a <see cref="UnityContainer"/> with the given parent container.
        /// </summary>
        /// <param name="parent">The parent <see cref="UnityContainer"/>. The current object
        /// will apply its own settings first, and then check the parent for additional ones.</param>
        private UnityContainer(UnityContainer parent)
        {
            _parent = parent;
            _parent?._lifetimeContainer.Add(this);

            Registering = OnTypeRegistration;
            Registering += OnRegister;  // TODO: Obsolete

            RegisteringInstance = OnInstanceRegistration;
            RegisteringInstance += OnRegisterInstance;  // TODO: Obsolete


            _registeredNames = new NamedTypesRegistry(ParentNameRegistry);
            _extensions = new List<UnityContainerExtension>();
            _extensionsContext = new ExtensionContextImpl(this);

            _lifetimeContainer = new LifetimeContainer();
            _strategies = new StagedStrategyChain<UnityBuildStage>(ParentStrategies);
            _buildPlanStrategies = new StagedStrategyChain<UnityBuildStage>(ParentBuildPlanStrategies);
            _policies = new PolicyList(ParentPolicies);
            _policies.Set<IRegisteredNamesPolicy>(new RegisteredNamesPolicy(_registeredNames), null);

            _cachedStrategies = null;
            _cachedStrategiesLock = new object();

            if (null == parent)
            {
                InitializeDefaultPolicies();
            }

            RegisterInstance(typeof(IUnityContainer), null, this, new ContainerLifetimeManager());
        }

        #endregion


        #region Registration

        private void InitializeDefaultPolicies()
        {
            // Main strategy chain
            _strategies.AddNew<LifetimeStrategy>(UnityBuildStage.Lifetime)
                       .ContainerContext = _extensionsContext;  // TODO: Requires optimization
            _strategies.AddNew<BuildKeyMappingStrategy>(UnityBuildStage.TypeMapping);
            _strategies.AddNew<ArrayResolutionStrategy>(UnityBuildStage.Creation);
            _strategies.AddNew<BuildPlanStrategy>(UnityBuildStage.Creation);

            // Build plan strategy chain
            _buildPlanStrategies.AddNew<DynamicMethodConstructorStrategy>(UnityBuildStage.Creation);
            _buildPlanStrategies.AddNew<DynamicMethodPropertySetterStrategy>(UnityBuildStage.Initialization);
            _buildPlanStrategies.AddNew<DynamicMethodCallStrategy>(UnityBuildStage.Initialization);

            // Policies - mostly used by the build plan strategies
            _policies.SetDefault<IConstructorSelectorPolicy>(new DefaultUnityConstructorSelectorPolicy());
            _policies.SetDefault<IPropertySelectorPolicy>(new DefaultUnityPropertySelectorPolicy());
            _policies.SetDefault<IMethodSelectorPolicy>(new DefaultUnityMethodSelectorPolicy());

            _policies.SetDefault<IBuildPlanCreatorPolicy>(new DynamicMethodBuildPlanCreatorPolicy(_buildPlanStrategies));

            _policies.Set<IBuildPlanPolicy>(new DeferredResolveBuildPlanPolicy(), typeof(Func<>));
            _policies.Set<ILifetimePolicy>(new PerResolveLifetimeManager(), typeof(Func<>));

            _policies.Set<IBuildPlanCreatorPolicy>(new LazyDynamicMethodBuildPlanCreatorPolicy(), typeof(Lazy<>));
            _policies.Set<IBuildPlanCreatorPolicy>(new EnumerableDynamicMethodBuildPlanCreatorPolicy(), typeof(IEnumerable<>));
        }


        // TODO: obsolete
        private void OnRegister(object sender, RegisterEventArgs e)
        {
            _registeredNames.RegisterType(e.TypeFrom ?? e.TypeTo, e.Name);

            if (e.TypeFrom != null)
            {
                if (e.TypeFrom.GetTypeInfo().IsGenericTypeDefinition && e.TypeTo.GetTypeInfo().IsGenericTypeDefinition)
                {
                    _policies.Set<IBuildKeyMappingPolicy>(
                        new GenericTypeBuildKeyMappingPolicy(new NamedTypeBuildKey(e.TypeTo, e.Name)),
                        new NamedTypeBuildKey(e.TypeFrom, e.Name));
                }
                else
                {
                    _policies.Set<IBuildKeyMappingPolicy>(
                        new BuildKeyMappingPolicy(new NamedTypeBuildKey(e.TypeTo, e.Name)),
                        new NamedTypeBuildKey(e.TypeFrom, e.Name));
                }
            }

            if (e.LifetimeManager != null)
            {
                SetLifetimeManager(e.TypeFrom ?? e.TypeTo, e.Name, e.LifetimeManager);
            }


            if (null != e.InjectionMembers && e.InjectionMembers.Length > 0)
            {
                ClearExistingBuildPlan(e.TypeTo, e.Name);
                foreach (var member in e.InjectionMembers)
                {
                    member.AddPolicies(e.TypeFrom, e.TypeTo, e.Name, _policies);
                }
            }
        }

        private void OnTypeRegistration(object sender, RegisterEventArgs e)
        {
            var type = e.TypeFrom ?? e.TypeTo;
            var key = new NamedTypeBuildKey(type, e.Name);
            var registration = new ContainerRegistration(key, _policies, GetStrategies().OfType<IRegisterTypes>()
                .Select(s => s.OnRegisterType(e.TypeFrom, e.TypeTo, e.Name, e.LifetimeManager, e.InjectionMembers))
                .SelectMany(p => p));

            lock (_registrations)
            {
                _registrations[key] = registration;
            }
        }

        // TODO: obsolete
        private void OnRegisterInstance(object sender, RegisterInstanceEventArgs e)
        {
            _registeredNames.RegisterType(e.RegisteredType, e.Name);
            SetLifetimeManager(e.RegisteredType, e.Name, e.LifetimeManager);
            NamedTypeBuildKey identityKey = new NamedTypeBuildKey(e.RegisteredType, e.Name);
            _policies.Set<IBuildKeyMappingPolicy>(new BuildKeyMappingPolicy(identityKey), identityKey);
            e.LifetimeManager.SetValue(e.Instance);
        }

        private void OnInstanceRegistration(object sender, RegisterInstanceEventArgs e)
        {
            var key = new NamedTypeBuildKey(e.RegisteredType, e.Name);
            var registration = new ContainerRegistration(key, _policies, GetStrategies().OfType<IRegisterInstances>()
                .Select(s => s.OnRegisterInstance(e.RegisteredType, e.Name, e.Instance, e.LifetimeManager))
                .SelectMany(p => p));

            lock (_registrations)
            {
                _registrations[key] = registration;
            }
        }

        #endregion


        #region ObjectBuilder

        private object DoBuildUp(Type type, string name, IEnumerable<ResolverOverride> resolverOverrides)
        {
            IBuilderContext context = null;

            try
            {
                ContainerRegistration registration;

                var key = new NamedTypeBuildKey(type, name);
                lock (_registrations)
                {
                    if (!TryGetRegistration(key, out registration))
                    {
                        registration = new ContainerRegistration(key, _policies);

                        if (string.IsNullOrWhiteSpace(name) && null == resolverOverrides)
                            _registrations[key] = registration;
                    }
                }

                context = new BuilderContext(this, GetStrategies(), _lifetimeContainer, registration, key, null);
                context.AddResolverOverrides(resolverOverrides);

                if (type.GetTypeInfo().IsGenericTypeDefinition)
                {
                    throw new ArgumentException(
                        string.Format(CultureInfo.CurrentCulture,
                        Resources.CannotResolveOpenGenericType,
                        type.FullName), nameof(type));
                }

                return context.Strategies.ExecuteBuildUp(context);
            }
            catch (Exception ex)
            {
                throw new ResolutionFailedException(type, name, ex, context);
            }
        }

        private object DoBuildUp(Type t, object existing, string name, IEnumerable<ResolverOverride> resolverOverrides)
        {
            IBuilderContext context = null;

            try
            {
                var key = new NamedTypeBuildKey(t, name);
                context = new BuilderContext(this, GetStrategies(), _lifetimeContainer, _policies, key, existing);
                context.AddResolverOverrides(resolverOverrides);

                if (t.GetTypeInfo().IsGenericTypeDefinition)
                {
                    throw new ArgumentException(
                        string.Format(CultureInfo.CurrentCulture,
                        Resources.CannotResolveOpenGenericType,
                        t.FullName), nameof(t));
                }

                return context.Strategies.ExecuteBuildUp(context);
            }
            catch (Exception ex)
            {
                throw new ResolutionFailedException(t, name, ex, context);
            }
        }

        private IStrategyChain GetStrategies()
        {
            IStrategyChain buildStrategies = _cachedStrategies;
            if (buildStrategies == null)
            {
                lock (_cachedStrategiesLock)
                {
                    if (_cachedStrategies == null)
                    {
                        buildStrategies = _strategies.MakeStrategyChain();
                        _cachedStrategies = buildStrategies;
                    }
                    else
                    {
                        buildStrategies = _cachedStrategies;
                    }
                }
            }
            return buildStrategies;
        }

        private StagedStrategyChain<UnityBuildStage> ParentStrategies => _parent?._strategies;

        private StagedStrategyChain<UnityBuildStage> ParentBuildPlanStrategies => _parent?._buildPlanStrategies;

        private PolicyList ParentPolicies => _parent?._policies;

        private NamedTypesRegistry ParentNameRegistry => _parent?._registeredNames;

        #endregion


        #region Lifetime Management

        private void SetLifetimeManager(Type lifetimeType, string name, LifetimeManager lifetimeManager)
        {
            // TODO: Obsolete
            //if (lifetimeManager.InUse)
            //{
            //    throw new InvalidOperationException(Resources.LifetimeManagerInUse);
            //}
            if (lifetimeType.GetTypeInfo().IsGenericTypeDefinition)
            {
                LifetimeManagerFactory factory = new LifetimeManagerFactory(_extensionsContext, lifetimeManager.GetType());
                _policies.Set<ILifetimeFactoryPolicy>(factory, new NamedTypeBuildKey(lifetimeType, name));
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


        #region Registrations

        private bool TryGetRegistration(IBuildKey key, out ContainerRegistration registration)
        {
            lock (_registrations)
            {
                if (_registrations.TryGetValue(key, out registration))
                    return true;
            }

            return null != _parent && _parent.TryGetRegistration(key, out registration);
        }


        /// <summary>
        /// Remove policies associated with building this type. This removes the
        /// compiled build plan so that it can be rebuilt with the new settings
        /// the next time this type is resolved.
        /// </summary>
        /// <param name="typeToInject">Type of object to clear the plan for.</param>
        /// <param name="name">Name the object is being registered with.</param>
        private void ClearExistingBuildPlan(Type typeToInject, string name)
        {
            var buildKey = new NamedTypeBuildKey(typeToInject, name);
            DependencyResolverTrackerPolicy.RemoveResolvers(_policies, buildKey);
            _policies.Set<IBuildPlanPolicy>(new OverriddenBuildPlanMarkerPolicy(), buildKey);
        }

        private void FillTypeRegistrationDictionary(IDictionary<Type, List<string>> typeRegistrations)
        {
            _parent?.FillTypeRegistrationDictionary(typeRegistrations);

            foreach (Type t in _registeredNames.RegisteredTypes)
            {
                if (!typeRegistrations.ContainsKey(t))
                {
                    typeRegistrations[t] = new List<string>();
                }

                typeRegistrations[t] =
                    (typeRegistrations[t].Concat(_registeredNames.GetKeys(t))).Distinct().ToList();
            }
        }
        
        #endregion


        #region IDisposable Implementation

        /// <summary>
        /// Dispose this container instance.
        /// </summary>
        /// <remarks>
        /// Disposing the container also disposes any child containers,
        /// and disposes any instances whose lifetimes are managed
        /// by the container.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this); // Shut FxCop up
        }

        /// <summary>
        /// Dispose this container instance.
        /// </summary>
        /// <remarks>
        /// This class doesn't have a finalizer, so <paramref name="disposing"/> will always be true.</remarks>
        /// <param name="disposing">True if being called from the IDisposable.Dispose
        /// method, false if being called from a finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_lifetimeContainer != null)
                {
                    // Avoid infinite loop when someone
                    //  registers something which would end up 
                    //  disposing this container (e.g. container.RegisterInsance(container))
                    LifetimeContainer lifetimeContainerCopy = _lifetimeContainer;
                    _lifetimeContainer = null;
                    lifetimeContainerCopy.Dispose();

                    if (_parent != null && _parent._lifetimeContainer != null)
                    {
                        _parent._lifetimeContainer.Remove(this);
                    }
                }

                _extensions.OfType<IDisposable>().ForEach(ex => ex.Dispose());
                _extensions.Clear();
            }
        }

        #endregion


        #region Nested Types

        // Works like the ExternallyControlledLifetimeManager, but uses regular instead of weak references
        private class ContainerLifetimeManager : LifetimeManager
        {
            private object value;

            public override object GetValue()
            {
                return value;
            }

            public override void SetValue(object newValue)
            {
                value = newValue;
            }

            public override void RemoveValue()
            {
            }
        }

        /// <summary>
        /// Implementation of the ExtensionContext that is actually used
        /// by the UnityContainer implementation.
        /// </summary>
        /// <remarks>
        /// This is a nested class so that it can access state in the
        /// container that would otherwise be inaccessible.
        /// </remarks>
        private class ExtensionContextImpl : ExtensionContext
        {
            private readonly UnityContainer container;

            public ExtensionContextImpl(UnityContainer container)
            {
                this.container = container;
            }

            public override IUnityContainer Container
            {
                get { return this.container; }
            }

            public override StagedStrategyChain<UnityBuildStage> Strategies
            {
                get { return this.container._strategies; }
            }

            public override StagedStrategyChain<UnityBuildStage> BuildPlanStrategies
            {
                get { return this.container._buildPlanStrategies; }
            }

            public override IPolicyList Policies
            {
                get { return this.container._policies; }
            }

            public override ILifetimeContainer Lifetime
            {
                get { return this.container._lifetimeContainer; }
            }

            public override void RegisterNamedType(Type t, string name)
            {
                this.container._registeredNames.RegisterType(t, name);
            }

            public override event EventHandler<RegisterEventArgs> Registering
            {
                add { this.container.Registering += value; }
                remove { this.container.Registering -= value; }
            }

            /// <summary>
            /// This event is raised when the <see cref="UnityContainer.RegisterInstance(Type,string,object,LifetimeManager)"/> method,
            /// or one of its overloads, is called.
            /// </summary>
            public override event EventHandler<RegisterInstanceEventArgs> RegisteringInstance
            {
                add { this.container.RegisteringInstance += value; }
                remove { this.container.RegisteringInstance -= value; }
            }

            public override event EventHandler<ChildContainerCreatedEventArgs> ChildContainerCreated
            {
                add { this.container.ChildContainerCreated += value; }
                remove { this.container.ChildContainerCreated -= value; }
            }
        }

        #endregion
    }
}
