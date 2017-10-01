using System;
using System.Collections.Generic;
using System.Text;
using Unity;

namespace ObjectBuilder2
{
    /// <summary>
    /// Represents a strategy in the chain of BuildUp responsibility. 
    /// This strategy holds a reference to Container context
    /// </summary>
    public class BuilderUpStrategy : BuilderStrategy
    {
        /// <summary>
        /// Reference to corresponding container context
        /// </summary>
        public IContainerContext ContainerContext { get; set; }
    }
}
