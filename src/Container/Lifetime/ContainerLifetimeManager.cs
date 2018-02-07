using Unity.Builder;
using Unity.Lifetime;

namespace Unity.Container.Lifetime
{
    /// <summary>
    /// Internal container lifetime manager. 
    /// </summary>
    internal class ContainerLifetimeManager : LifetimeManager
    {
        public override object GetValue(IBuilderContext context = null)
        {
            return context.Container;
        }

        protected override LifetimeManager OnCreateLifetimeManager()
        {
            return new ContainerLifetimeManager();
        }
    }
}
