using System;

namespace Unity.Container.Registration
{
    public interface IBuildKey
    {
        string Name { get; }

        Type Type { get; }
    }
}