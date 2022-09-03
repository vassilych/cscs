using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public abstract class CompiledClass : CSCSClass
    {
        public abstract ScriptObject GetImplementation(List<Variable> args);
    }

    public abstract class CompiledClassAsync : CSCSClass
    {
        public abstract Task<ScriptObject> GetImplementationAsync(List<Variable> args);
    }

}
