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
}
