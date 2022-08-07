using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class ParserFunction
    {
        public Interpreter InterpreterInstance { get; set; }

        public static Action<string, Variable, bool> OnVariableChange;

        public ParserFunction()
        {
            m_impl = this;
        }

        // A "virtual" Constructor
        public ParserFunction(ParsingScript script, string item, char ch, ref string action)
        {
            InterpreterInstance = script.InterpreterInstance;

            if (item.Length == 0 && (ch == Constants.START_ARG || !script.StillValid()))
            {
                // There is no function, just an expression in parentheses
                m_impl = s_idFunction;
                return;
            }

            m_impl = CheckString(script, item, ch);
            if (m_impl != null)
            {
                return;
            }

            item = Constants.ConvertName(item);

            m_impl = InterpreterInstance.GetRegisteredAction(item, script, ref action);
            if (m_impl != null)
            {
                return;
            }

            m_impl = InterpreterInstance.GetArrayFunction(item, script, action);
            if (m_impl != null)
            {
                return;
            }

            m_impl = InterpreterInstance.GetObjectFunction(item, script);
            if (m_impl != null)
            {
                return;
            }

            m_impl = InterpreterInstance.GetVariable(item, script);
            if (m_impl != null)
            {
                return;
            }

            if (m_impl == s_strOrNumFunction && string.IsNullOrWhiteSpace(item))
            {
                string problem = (!string.IsNullOrWhiteSpace(action) ? action : ch.ToString());
                string restData = ch.ToString() + script.Rest;
                throw new ArgumentException("Couldn't parse [" + problem + "] in " + restData + "...");
            }

            // Function not found, will try to parse this as a string in quotes or a number.
            s_strOrNumFunction.Item = item;
            m_impl = s_strOrNumFunction;
        }

        static ParserFunction CheckString(ParsingScript script, string item, char ch)
        {
            if (item.Length > 1 &&
              ((item[0] == Constants.QUOTE && item[item.Length - 1] == Constants.QUOTE) ||
               (item[0] == Constants.QUOTE1 && item[item.Length - 1] == Constants.QUOTE1)))
            {
                // We are dealing with a string.
                s_strOrNumFunction.Item = item;
                return s_strOrNumFunction;
            }
            if (script.ProcessingList && ch == ':')
            {
                s_strOrNumFunction.Item = '"' + item + '"';
                return s_strOrNumFunction;
            }
            return null;
        }

        public Variable GetValue(ParsingScript script)
        {
            return m_impl.Evaluate(script);
        }

        public async Task<Variable> GetValueAsync(ParsingScript script)
        {
            return await m_impl.EvaluateAsync(script);
        }

        protected virtual Variable Evaluate(ParsingScript script)
        {
            // The real implementation will be in the derived classes.
            return new Variable();
        }

        protected virtual Task<Variable> EvaluateAsync(ParsingScript script)
        {
            // If not overriden, the non-sync version will be called.
            return Task.FromResult( Evaluate(script) );
        }

        // Derived classes may want to return a new instance in order to
        // not to use same object in calculations.
        public virtual ParserFunction NewInstance()
        {
            return this;
        }

        public ParsingScript NewParsingScript(string data, int from = 0,
                     Dictionary<int, int> char2Line = null)
        {
            return new ParsingScript(InterpreterInstance, data, from, char2Line);
        }

        public virtual string Description()
        {
            var name = this.GetType().Name;
            return name;
        }

        protected string m_name;
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        public bool m_isGlobal = true;
        public bool isGlobal { get { return m_isGlobal; } set { m_isGlobal = value; } }

        protected bool m_isNative = true;
        public bool isNative { get { return m_isNative; } set { m_isNative = value; } }

        ParserFunction m_impl;

        public class StackLevel
        {
            static int s_id;

            public StackLevel(string name = null, bool isNamespace = false)
            {
                Id = ++s_id;
                Name = name;
                IsNamespace = isNamespace;
                Variables = new Dictionary<string, ParserFunction>();
            }

            public string Name { get; private set; }
            public bool IsNamespace { get; private set; }
            public int Id { get; private set; }

            public Dictionary<string, ParserFunction> Variables { get; set; }
        }

        // Local variables:
        // Stack of the functions being executed:

        static StringOrNumberFunction s_strOrNumFunction =
          new StringOrNumberFunction();
        static IdentityFunction s_idFunction =
          new IdentityFunction();

        public static int StackLevelDelta { get; set; }
    }

    public abstract class ActionFunction : ParserFunction
    {
        protected string m_action;
        public string Action { set { m_action = value; } }
    }
}