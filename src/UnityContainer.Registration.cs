using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Unity.Builder;
using Unity.Events;
using Unity.Lifetime;
using Unity.Policy;
using Unity.Registration;
using Unity.Storage;

namespace Unity
{
    public partial class UnityContainer
    {
        #region Constants

        private const int ContainerInitialCapacity = 37;
        private const int ListToHashCutoverPoint = 8;

        #endregion


        #region Fields

        private readonly object _syncRoot = new object();
        private HashRegistry<Type, IRegistry<string, IPolicySet>> _policies = 
            new HashRegistry<Type, IRegistry<string, IPolicySet>>(ContainerInitialCapacity);

        #endregion


        #region Type Registration

        /// <summary>
        /// Register a type mapping with the container, where the created instances will use
        /// the given <see cref="LifetimeManager"/>.
        /// </summary>
        /// <remarks><paramref name="registeredType"/> type and <paramref name="mappedTo"/> type must be assignable.</remarks>
        /// <param name="registeredType">Registered <see cref="Type"/>. Or the <see cref="Type"/> that 
        /// will be requested when resolving.</param>
        /// <param name="name">Name to use for registration, null if a default registration.</param>
        /// <param name="mappedTo"><see cref="Type"/> that will be used to actually costruct registered type. 
        /// <paramref name="mappedTo"/> must be derived from or implement <paramref name="registeredType"/> </param>
        /// <param name="lifetimeManager">The <see cref="LifetimeManager"/> that controls the lifetime
        /// of the returned instance.</param>
        /// <param name="injectionMembers">Injection configuration objects. Can be null.</param>
        /// <returns>The <see cref="IUnityContainer"/> object that this method was called on (this in C#, Me in Visual Basic).</returns>
        /// <example> 
        /// // To register mapping between interface IFoo and implementation type foo use following:
        /// container.RegisterType(typeof(IFoo), name, typeof(Foo), ...); 
        /// 
        /// // To register just a type Foo use following syntax:
        /// container.RegisterType(typeof(Foo), name, null, ...);
        /// </example>
        public IUnityContainer RegisterType(Type registeredType, string name, Type mappedTo, LifetimeManager lifetimeManager, params InjectionMember[] injectionMembers)
        {
            // Validate imput
            if (string.Empty == name) name = null; 
            if (null == registeredType) throw new ArgumentNullException(nameof(registeredType));
            if (null == lifetimeManager) lifetimeManager = TransientLifetimeManager.Instance;
            if (mappedTo != null && !registeredType.GetTypeInfo().IsGenericType && !mappedTo.GetTypeInfo().IsGenericType && 
                                    !registeredType.GetTypeInfo().IsAssignableFrom( mappedTo.GetTypeInfo()))
            {
                throw new ArgumentException(string.Format(CultureInfo.CurrentCulture,
                    Constants.TypesAreNotAssignable, registeredType, mappedTo), nameof(registeredType));
            }

            // Create registration and add to appropriate storage
            var container = (lifetimeManager is ISingletonLifetimePolicy) ? GetRootContainer() : this;
            var registration = new TypeRegistration(registeredType, name, mappedTo, lifetimeManager);

            // Add or replace existing 
            if (container.SetOrUpdate(registration) is IDisposable disposable)
            {
                container._lifetimeContainer.Remove(disposable);
                disposable.Dispose();
            }

            // If Disposable add to container's lifetime
            if (registration.LifetimeManager is IDisposable manager)
                container._lifetimeContainer.Add(manager);

            // Add Injection Members
            var context = container._context.RegistrationContext(registration);
            if (null != injectionMembers && injectionMembers.Length > 0)
            {
                foreach (var member in injectionMembers)
                {
                    member.AddPolicies(registration.RegisteredType, 
                                       registration.MappedToType ?? 
                                       registration.RegisteredType, 
                                       registration.Name, context.Policies);
                }
            }

            // Register policies for each strategy
            var strategies = container._registerTypeStrategies;
            foreach (var strategy in strategies)
                strategy.RegisterType(context, registration.RegisteredType, 
                                               registration.Name, 
                                               registration.MappedToType,
                                               registration.LifetimeManager, injectionMembers);

            // Raise event
            container.Registering?.Invoke(this, new RegisterEventArgs(registration.RegisteredType, 
                                                                      registration.MappedToType,
                                                                      registration.Name, 
                                                                      registration.LifetimeManager));
            return this;
        }

        #endregion


        #region Instance Registration

        /// <summary>
        /// Register an instance with the container.
        /// </summary>
        /// <remarks> <para>
        /// Instance registration is much like setting a type as a singleton, except that instead
        /// of the container creating the instance the first time it is requested, the user
        /// creates the instance ahead of type and adds that instance to the container.
        /// </para></remarks>
        /// <param name="registrationType">Type of instance to register (may be an implemented interface instead of the full type).</param>
        /// <param name="instance">Object to be returned.</param>
        /// <param name="registrationName">Name for registration.</param>
        /// <param name="lifetimeManager">
        /// <para>If null or <see cref="ContainerControlledLifetimeManager"/>, the container will take over the lifetime of the instance,
        /// calling Dispose on it (if it's <see cref="IDisposable"/>) when the container is Disposed.</para>
        /// <para>
        ///  If <see cref="ExternallyControlledLifetimeManager"/>, container will not maintain a strong reference to <paramref name="instance"/>. 
        /// User is responsible for disposing instance, and for keeping the instance typeFrom being garbage collected.</para></param>
        /// <returns>The <see cref="UnityContainer"/> object that this method was called on (this in C#, Me in Visual Basic).</returns>
        public IUnityContainer RegisterInstance(Type registrationType, string registrationName, object instance, LifetimeManager lifetimeManager)
        {
            // Validate imput
            if (string.Empty == registrationName) registrationName = null;
            if (null == instance) throw new ArgumentNullException(nameof(instance));

            // TODO: Move to strategy
            var lifetime = lifetimeManager ?? new ContainerControlledLifetimeManager();
            if (lifetime.InUse) throw new InvalidOperationException(Constants.LifetimeManagerInUse);
            lifetime.SetValue(instance, new BuilderContext(this, _lifetimeContainer, null, null, null, null, null, null));

            // Create registration and add to appropriate storage
            var registration = new InstanceRegistration(registrationType, registrationName, instance, lifetime);
            var container = (lifetimeManager is ISingletonLifetimePolicy) ? GetRootContainer() : this;

            // Add or replace existing 
            if (container.SetOrUpdate(registration) is IDisposable disposable)
            {
                container._lifetimeContainer.Remove(disposable);
                disposable.Dispose();
            }

            if (registration.LifetimeManager is IDisposable manager)
                container._lifetimeContainer.Add(manager);

            // Raise event
            container.RegisteringInstance?.Invoke(this, new RegisterInstanceEventArgs(registration.RegisteredType, instance,
                                                                   registration.Name, registration.LifetimeManager));
            return this;
        }

        #endregion


        #region Check Registration


        public bool IsRegistered(Type type, string name)
        {
            for (var registry = this; null != registry; registry = registry._parent)
            {
                if (null == registry[type, name]) continue;

                return true;
            }

            return false;
        }

        #endregion


        private IRegistry<string, IPolicySet> this[Type type]
        {
            get
            {
                var hashCode = (type?.GetHashCode() ?? 0) & 0x7FFFFFFF;
                var targetBucket = hashCode % _policies.Buckets.Length;
                for (var i = _policies.Buckets[targetBucket]; i >= 0; i = _policies.Entries[i].Next)
                {
                    if (_policies.Entries[i].HashCode != hashCode ||
                        _policies.Entries[i].Key != type)
                    {
                        continue;
                    }

                    return _policies.Entries[i].Value;
                }

                return null;
            }
        }

        private IPolicySet this[Type type, string name]
        {
            get
            {
                var hashCode = (type?.GetHashCode() ?? 0) & 0x7FFFFFFF;
                var targetBucket = hashCode % _policies.Buckets.Length;
                for (var i = _policies.Buckets[targetBucket]; i >= 0; i = _policies.Entries[i].Next)
                {
                    if (_policies.Entries[i].HashCode != hashCode ||
                        _policies.Entries[i].Key != type)
                    {
                        continue;
                    }

                    return _policies.Entries[i].Value?[name];
                }

                return null;
            }
            set
            {
                var hashCode = (type?.GetHashCode() ?? 0) & 0x7FFFFFFF;
                var targetBucket = hashCode % _policies.Buckets.Length;
                var collisions = 0;
                lock (_syncRoot)
                {
                    for (var i = _policies.Buckets[targetBucket]; i >= 0; i = _policies.Entries[i].Next)
                    {
                        if (_policies.Entries[i].HashCode != hashCode ||
                            _policies.Entries[i].Key != type)
                        {
                            collisions++;
                            continue;
                        }

                        var existing = _policies.Entries[i].Value;
                        if (existing.RequireToGrow)
                        {
                            existing = existing is HashRegistry<string, IPolicySet> registry
                                ? new HashRegistry<string, IPolicySet>(registry)
                                : new HashRegistry<string, IPolicySet>(LinkedRegistry.ListToHashCutoverPoint * 2,
                                    (LinkedRegistry)existing);

                            _policies.Entries[i].Value = existing;
                        }

                        existing[name] = value;
                        return;
                    }

                    if (_policies.RequireToGrow || ListToHashCutoverPoint < collisions)
                    {
                        _policies = new HashRegistry<Type, IRegistry<string, IPolicySet>>(_policies);
                        targetBucket = hashCode % _policies.Buckets.Length;
                    }

                    _policies.Entries[_policies.Count].HashCode = hashCode;
                    _policies.Entries[_policies.Count].Next = _policies.Buckets[targetBucket];
                    _policies.Entries[_policies.Count].Key = type;
                    _policies.Entries[_policies.Count].Value = new LinkedRegistry(name, value); 
                    _policies.Buckets[targetBucket] = _policies.Count;
                    _policies.Count++;
                }
            }
        }

        private IBuilderPolicy this[Type type, string name, Type interfaceType]
        {
            get
            {
                var collisions = 0;
                var hashCode = (type?.GetHashCode() ?? 0) & 0x7FFFFFFF;
                var targetBucket = hashCode % _policies.Buckets.Length;
                IPolicySet registration = null;
                lock (_syncRoot)
                {
                    for (var i = _policies.Buckets[targetBucket]; i >= 0; i = _policies.Entries[i].Next)
                    {
                        if (_policies.Entries[i].HashCode != hashCode ||
                            _policies.Entries[i].Key != type)
                        {
                            collisions++;
                            continue;
                        }

                        var existing = _policies.Entries[i].Value;
                        if (existing.RequireToGrow)
                        {
                            existing = existing is HashRegistry<string, IPolicySet> registry
                                     ? new HashRegistry<string, IPolicySet>(registry)
                                     : new HashRegistry<string, IPolicySet>(LinkedRegistry.ListToHashCutoverPoint * 2,
                                                                                           (LinkedRegistry)existing);

                            _policies.Entries[i].Value = existing;
                        }

                        registration = existing.GetOrAdd(name, () => CreateRegistration(type, name));
                        break;
                    }

                    if (null == registration)
                    {
                        if (_policies.RequireToGrow || ListToHashCutoverPoint < collisions)
                        {
                            _policies = new HashRegistry<Type, IRegistry<string, IPolicySet>>(_policies);
                            targetBucket = hashCode % _policies.Buckets.Length;
                        }

                        registration = CreateRegistration(type, name);
                        _policies.Entries[_policies.Count].HashCode = hashCode;
                        _policies.Entries[_policies.Count].Next = _policies.Buckets[targetBucket];
                        _policies.Entries[_policies.Count].Key = type;
                        _policies.Entries[_policies.Count].Value = new LinkedRegistry(name, registration);
                        _policies.Buckets[targetBucket] = _policies.Count;
                        _policies.Count++;
                    }

                    return registration.Get(interfaceType);
                }
            }
            set
            {
                var collisions = 0;
                var hashCode = (type?.GetHashCode() ?? 0) & 0x7FFFFFFF;
                var targetBucket = hashCode % _policies.Buckets.Length;
                lock (_syncRoot)
                {
                    for (var i = _policies.Buckets[targetBucket]; i >= 0; i = _policies.Entries[i].Next)
                    {
                        if (_policies.Entries[i].HashCode != hashCode ||
                            _policies.Entries[i].Key != type)
                        {
                            collisions++;
                            continue;
                        }

                        var existing = _policies.Entries[i].Value;
                        if (existing.RequireToGrow)
                        {
                            existing = existing is HashRegistry<string, IPolicySet> registry
                                     ? new HashRegistry<string, IPolicySet>(registry)
                                     : new HashRegistry<string, IPolicySet>(LinkedRegistry.ListToHashCutoverPoint * 2, 
                                                                                           (LinkedRegistry)existing);

                            _policies.Entries[i].Value = existing;
                        }

                        existing.GetOrAdd(name, () => CreateRegistration(type, name)).Set(interfaceType, value);
                        return;
                    }

                    if (_policies.RequireToGrow || ListToHashCutoverPoint < collisions)
                    {
                        _policies = new HashRegistry<Type, IRegistry<string, IPolicySet>>(_policies);
                        targetBucket = hashCode % _policies.Buckets.Length;
                    }

                    var registration = CreateRegistration(type, name);
                    registration.Set(interfaceType, value);

                    _policies.Entries[_policies.Count].HashCode = hashCode;
                    _policies.Entries[_policies.Count].Next = _policies.Buckets[targetBucket];
                    _policies.Entries[_policies.Count].Key = type;                       
                    _policies.Entries[_policies.Count].Value = new LinkedRegistry(name, registration);
                    _policies.Buckets[targetBucket] = _policies.Count;
                    _policies.Count++;
                }
            }
        }


        #region Special Accessors

        private IPolicySet SetOrUpdate(INamedType registration)
        {
            var collisions = 0;
            var hashCode = (registration.Type?.GetHashCode() ?? 0) & 0x7FFFFFFF;
            var targetBucket = hashCode % _policies.Buckets.Length;
            lock (_syncRoot)
            {
                for (var i = _policies.Buckets[targetBucket]; i >= 0; i = _policies.Entries[i].Next)
                {
                    if (_policies.Entries[i].HashCode != hashCode ||
                        _policies.Entries[i].Key != registration.Type)
                    {
                        collisions++;
                        continue;
                    }

                    var existing = _policies.Entries[i].Value;
                    if (existing.RequireToGrow)
                    {
                        existing = existing is HashRegistry<string, IPolicySet> registry
                                 ? new HashRegistry<string, IPolicySet>(registry)
                                 : new HashRegistry<string, IPolicySet>(LinkedRegistry.ListToHashCutoverPoint * 2, (LinkedRegistry)existing);

                        _policies.Entries[i].Value = existing;
                    }

                    return existing.SetOrReplace(registration.Name, (IPolicySet)registration);
                }

                if (_policies.RequireToGrow || ListToHashCutoverPoint < collisions)
                {
                    _policies = new HashRegistry<Type, IRegistry<string, IPolicySet>>(_policies);
                    targetBucket = hashCode % _policies.Buckets.Length;
                }

                _policies.Entries[_policies.Count].HashCode = hashCode;
                _policies.Entries[_policies.Count].Next = _policies.Buckets[targetBucket];
                _policies.Entries[_policies.Count].Key = registration.Type;
                _policies.Entries[_policies.Count].Value = new LinkedRegistry(registration.Name, (IPolicySet)registration);
                _policies.Buckets[targetBucket] = _policies.Count;
                _policies.Count++;

                return null;
            }
        }

        private IPolicySet GetOrAdd(Type type, string name)
        {
            var collisions = 0;
            var hashCode = (type?.GetHashCode() ?? 0) & 0x7FFFFFFF;
            var targetBucket = hashCode % _policies.Buckets.Length;
            lock (_syncRoot)
            {
                for (var i = _policies.Buckets[targetBucket]; i >= 0; i = _policies.Entries[i].Next)
                {
                    if (_policies.Entries[i].HashCode != hashCode ||
                        _policies.Entries[i].Key != type)
                    {
                        collisions++;
                        continue;
                    }

                    var existing = _policies.Entries[i].Value;
                    if (existing.RequireToGrow)
                    {
                        existing = existing is HashRegistry<string, IPolicySet> registry
                                 ? new HashRegistry<string, IPolicySet>(registry)
                                 : new HashRegistry<string, IPolicySet>(LinkedRegistry.ListToHashCutoverPoint * 2,
                                                                                       (LinkedRegistry)existing);

                        _policies.Entries[i].Value = existing;
                    }

                    return existing.GetOrAdd(name, () => CreateRegistration(type, name));
                }

                if (_policies.RequireToGrow || ListToHashCutoverPoint < collisions)
                {
                    _policies = new HashRegistry<Type, IRegistry<string, IPolicySet>>(_policies);
                    targetBucket = hashCode % _policies.Buckets.Length;
                }

                var registration = CreateRegistration(type, name);
                _policies.Entries[_policies.Count].HashCode = hashCode;
                _policies.Entries[_policies.Count].Next = _policies.Buckets[targetBucket];
                _policies.Entries[_policies.Count].Key = type;
                _policies.Entries[_policies.Count].Value = new LinkedRegistry(name, registration);
                _policies.Buckets[targetBucket] = _policies.Count;
                _policies.Count++;

                return registration;
            }

        }

        #endregion


        #region Registrations Collection

        /// <summary>
        /// GetOrDefault a sequence of <see cref="IContainerRegistration"/> that describe the current state
        /// of the container.
        /// </summary>
        public IEnumerable<IContainerRegistration> Registrations
        {
            get
            {
                return GetRegisteredTypes(this).SelectMany(type => GetRegisteredType(this, type));
            }
        }

        private ISet<Type> GetRegisteredTypes(UnityContainer container)
        {
            var set = null == container._parent ? new HashSet<Type>() 
                                                : GetRegisteredTypes(container._parent);

            foreach (var type in container._policies.Keys.Where(t => null != t))
                set.Add(type);

            return set;
        }

        private IEnumerable<IContainerRegistration> GetRegisteredType(UnityContainer container, Type type)
        {
            ReverseHashSet set;

            if (null != container._parent)
                set = (ReverseHashSet)GetRegisteredType(container._parent, type);
            else 
                set = new ReverseHashSet();

            foreach (var registration in container[type]?.Values.OfType<IContainerRegistration>()
                                      ?? Enumerable.Empty<IContainerRegistration>())
            {
                set.Add(registration);
            }

            return set;
        }

        private IEnumerable<string> GetRegisteredNames(UnityContainer container, Type type)
        {
            ISet<string> set;

            if (null != container._parent)
                set = (ISet<string>)GetRegisteredNames(container._parent, type);
            else
                set = new HashSet<string>();

            foreach (var registration in container[type]?.Values.OfType<IContainerRegistration>()
                                      ?? Enumerable.Empty<IContainerRegistration>())
            {
                set.Add(registration.Name);
            }

            var generic = type.GetTypeInfo().IsGenericType ? type.GetGenericTypeDefinition() : type;

            if (generic != type)
            {
                foreach (var registration in container[generic]?.Values.OfType<IContainerRegistration>()
                                          ?? Enumerable.Empty<IContainerRegistration>())
                {
                    set.Add(registration.Name);
                }
            }

            return set;
        }

        #endregion
    }
}
