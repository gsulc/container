using System;
using System.Reflection;
using Unity.Builder;
using Unity.ObjectBuilder.BuildPlan.DynamicMethod;
using Unity.Policy;

namespace Unity.ObjectBuilder.Policies
{
    public class DelegateBasedBuildPlanCreatorPolicy : IBuildPlanCreatorPolicy
    {
        #region Fields

        private readonly MethodInfo _resolveMethod;
        private readonly Func<IBuilderContext, Type> _getTypeFunc;

        #endregion


        #region Constructors

        public DelegateBasedBuildPlanCreatorPolicy(MethodInfo resolveMethod, Func<IBuilderContext, Type> getTypeFunc)
        {
            _resolveMethod = resolveMethod;
            _getTypeFunc = getTypeFunc;
        }

        #endregion


        #region IBuildPlanCreatorPolicy

        public IBuildPlanPolicy CreatePlan(IBuilderContext context, INamedType buildKey)
        {
            var buildMethod = _resolveMethod.MakeGenericMethod(_getTypeFunc(context))
                                            .CreateDelegate(typeof(DynamicBuildPlanMethod));

            return new DynamicMethodBuildPlan((DynamicBuildPlanMethod)buildMethod);
        }

        #endregion
    }
}
