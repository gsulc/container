using ObjectBuilder2;
using Unity.ObjectBuilder;

namespace Unity
{
    public interface IContainerContext
    {
        /// <summary>
        /// The container that this context is associated with.
        /// </summary>
        /// <value>The <see cref="IUnityContainer"/> object.</value>
        IUnityContainer Container { get; }

        /// <summary>
        /// The <see cref="ILifetimeContainer"/> that this container uses.
        /// </summary>
        /// <value>The <see cref="ILifetimeContainer"/> is used to manage <see cref="IDisposable"/> objects that the container is managing.</value>
        ILifetimeContainer Lifetime { get; }

        /// <summary>
        /// The strategies this container uses.
        /// </summary>
        /// <value>The <see cref="StagedStrategyChain{TStageEnum}"/> that the container uses to build objects.</value>
        StagedStrategyChain<UnityBuildStage> Strategies { get; }

        /// <summary>
        /// The strategies this container uses to construct build plans.
        /// </summary>
        /// <value>The <see cref="StagedStrategyChain{TStageEnum}"/> that this container uses when creating
        /// build plans.</value>
        StagedStrategyChain<UnityBuildStage> BuildPlanStrategies { get; }
    }
}