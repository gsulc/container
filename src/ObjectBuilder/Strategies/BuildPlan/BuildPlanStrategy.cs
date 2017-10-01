// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Unity;
using Unity.Container.Registration;

namespace ObjectBuilder2
{
    /// <summary>
    /// A <see cref="BuilderStrategy"/> that will look for a build plan
    /// in the current context. If it exists, it invokes it, otherwise
    /// it creates one and stores it for later, and invokes it.
    /// </summary>
    public class BuildPlanStrategy : BuilderStrategy, IRegisterTypes
    {
        #region Registerations

        public IEnumerable<IBuilderPolicy> OnRegisterType(Type typeFrom, Type typeTo, string name, LifetimeManager lifetimeManager, InjectionMember[] injectionMembers)
        {
            return new[] { new OverriddenBuildPlanMarkerPolicy() };
        }

        #endregion

        
        /// <summary>
        /// Called during the chain of responsibility for a build operation.
        /// </summary>
        /// <param name="builderContext">The context for the operation.</param>
        public override void PreBuildUp(IBuilderContext builderContext)
        {
            var context = builderContext ?? throw new ArgumentNullException(nameof(builderContext));
            var plan = context.Policies.Get<IBuildPlanPolicy>(context.BuildKey, out var buildPlanLocation);

            if (plan == null || plan is OverriddenBuildPlanMarkerPolicy)
            {
                var planCreator = context.Policies.Get<IBuildPlanCreatorPolicy>(context.BuildKey, out var creatorLocation);
                if (planCreator != null)
                {
                    plan = planCreator.CreatePlan(context, context.BuildKey);
                    context.Policies.Set(plan, context.OriginalBuildKey);
                }
            }

            plan?.BuildUp(context);
        }

    }
}
