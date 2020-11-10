using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SplitAndMerge.ParserFunction;

namespace SplitAndMerge
{
    public class OutputAvailableEventArgs : EventArgs
    {
        public OutputAvailableEventArgs(string output)
        {
            Output = output;
        }
        public string Output { get; set; }
    }

    public partial class Interpreter
    {

        private static Interpreter instance;
        private bool m_bHasBeenInitialized = false;

        private Interpreter()
        {
            Init();
        }

        public static Interpreter Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Interpreter();
                }
                return instance;
            }
        }

        private int MAX_LOOPS;

        private StringBuilder m_output = new StringBuilder();
        public string Output
        {
            get
            {
                string output = m_output.ToString().Trim();
                m_output.Clear();
                return output;
            }
        }

        public event EventHandler<OutputAvailableEventArgs> OnOutput;
        public event EventHandler<OutputAvailableEventArgs> OnData;

        public void AppendOutput(string text, bool newLine = false)
        {
            EventHandler<OutputAvailableEventArgs> handler = OnOutput;
            if (handler != null)
            {
                OutputAvailableEventArgs args = new OutputAvailableEventArgs(text +
                                     (newLine ? Environment.NewLine : string.Empty));
                handler(this, args);
            }
        }

        public bool AppendData(string text, bool newLine = false)
        {
            EventHandler<OutputAvailableEventArgs> handler = OnData;
            if (handler != null)
            {
                OutputAvailableEventArgs args = new OutputAvailableEventArgs(text +
                                     (newLine ? Environment.NewLine : string.Empty));
                handler(this, args);
                return true;
            }
            return false;
        }

        public void Init()
        {
            if (m_bHasBeenInitialized)
                return;
            m_bHasBeenInitialized = true; // making sure the init gets call only once

            RegisterFunctions();
            RegisterEnums();
            RegisterActions();

            ParserFunction.AddGlobal(Constants.THIS,
                new GetVarFunction(new Variable(Variable.VarType.ARRAY)));

            InitStandalone();
            CompiledClass.Init();
        }

        public void RegisterFunctions()
        {
            ParserFunction.RegisterFunction(Constants.IF, new IfStatement());
            ParserFunction.RegisterFunction(Constants.DO, new DoWhileStatement());
            ParserFunction.RegisterFunction(Constants.WHILE, new WhileStatement());
            ParserFunction.RegisterFunction(Constants.SWITCH, new SwitchStatement());
            ParserFunction.RegisterFunction(Constants.CASE, new CaseStatement());
            ParserFunction.RegisterFunction(Constants.DEFAULT, new CaseStatement());
            ParserFunction.RegisterFunction(Constants.FOR, new ForStatement());
            ParserFunction.RegisterFunction(Constants.BREAK, new BreakStatement());
            ParserFunction.RegisterFunction(Constants.COMPILED_FUNCTION, new CompiledFunctionCreator(false));
            ParserFunction.RegisterFunction(Constants.CONTINUE, new ContinueStatement());
            ParserFunction.RegisterFunction(Constants.CLASS, new ClassCreator());
            ParserFunction.RegisterFunction(Constants.ENUM, new EnumFunction());
            ParserFunction.RegisterFunction(Constants.INFINITY, new InfinityFunction());
            ParserFunction.RegisterFunction(Constants.NEG_INFINITY, new NegInfinityFunction());
            ParserFunction.RegisterFunction(Constants.ISFINITE, new IsFiniteFunction());
            ParserFunction.RegisterFunction(Constants.ISNAN, new IsNaNFunction());
            ParserFunction.RegisterFunction(Constants.NEW, new NewObjectFunction());
            ParserFunction.RegisterFunction(Constants.NULL, new NullFunction());
            ParserFunction.RegisterFunction(Constants.RETURN, new ReturnStatement());
            ParserFunction.RegisterFunction(Constants.FUNCTION, new FunctionCreator());
            ParserFunction.RegisterFunction(Constants.GET_PROPERTIES, new GetPropertiesFunction());
            ParserFunction.RegisterFunction(Constants.GET_PROPERTY, new GetPropertyFunction());
            ParserFunction.RegisterFunction(Constants.INCLUDE, new IncludeFile());
            ParserFunction.RegisterFunction(Constants.SET_PROPERTY, new SetPropertyFunction());
            ParserFunction.RegisterFunction(Constants.TRY, new TryBlock());
            ParserFunction.RegisterFunction(Constants.THROW, new ThrowFunction());
            ParserFunction.RegisterFunction(Constants.TYPE, new TypeFunction());
            ParserFunction.RegisterFunction(Constants.TYPE_OF, new TypeOfFunction());
            ParserFunction.RegisterFunction(Constants.TRUE, new BoolFunction(true));
            ParserFunction.RegisterFunction(Constants.FALSE, new BoolFunction(false));
            ParserFunction.RegisterFunction(Constants.UNDEFINED, new UndefinedFunction());

            ParserFunction.RegisterFunction(Constants.ADD, new AddFunction());
            ParserFunction.RegisterFunction(Constants.ADD_TO_HASH, new AddVariableToHashFunction());
            ParserFunction.RegisterFunction(Constants.ADD_ALL_TO_HASH, new AddVariablesToHashFunction());
            ParserFunction.RegisterFunction(Constants.CANCEL, new CancelFunction());
            ParserFunction.RegisterFunction(Constants.CANCEL_RUN, new ScheduleRunFunction(false));
            ParserFunction.RegisterFunction(Constants.CHECK_LOADER_MAIN, new CheckLoaderMainFunction());
            ParserFunction.RegisterFunction(Constants.CONTAINS, new ContainsFunction());
            ParserFunction.RegisterFunction(Constants.CURRENT_PATH, new CurrentPathFunction());
            ParserFunction.RegisterFunction(Constants.DATE_TIME, new DateTimeFunction(false));
            ParserFunction.RegisterFunction(Constants.DEEP_COPY, new DeepCopyFunction());
            ParserFunction.RegisterFunction(Constants.DEFINE_LOCAL, new DefineLocalFunction());
            ParserFunction.RegisterFunction(Constants.ENV, new GetEnvFunction());
            ParserFunction.RegisterFunction(Constants.FIND_INDEX, new FindIndexFunction());
            ParserFunction.RegisterFunction(Constants.GET_COLUMN, new GetColumnFunction());
            ParserFunction.RegisterFunction(Constants.GET_FILE_FROM_DEBUGGER, new GetFileFromDebugger());
            ParserFunction.RegisterFunction(Constants.GET_KEYS, new GetAllKeysFunction());
            ParserFunction.RegisterFunction(Constants.INCLUDE_SECURE, new IncludeFileSecure());
            ParserFunction.RegisterFunction(Constants.JSON, new GetVariableFromJSONFunction());
            ParserFunction.RegisterFunction(Constants.LOCK, new LockFunction());
            ParserFunction.RegisterFunction(Constants.NAMESPACE, new NamespaceFunction());
            ParserFunction.RegisterFunction(Constants.NAME_EXISTS, new NameExistsFunction());
            ParserFunction.RegisterFunction(Constants.NOW, new DateTimeFunction());
            ParserFunction.RegisterFunction(Constants.PRINT, new PrintFunction());
            ParserFunction.RegisterFunction(Constants.PSTIME, new ProcessorTimeFunction());
            ParserFunction.RegisterFunction(Constants.REGEX, new RegexFunction());
            ParserFunction.RegisterFunction(Constants.REMOVE, new RemoveFunction());
            ParserFunction.RegisterFunction(Constants.REMOVE_AT, new RemoveAtFunction());
            ParserFunction.RegisterFunction(Constants.RESET_VARS, new ResetVariablesFunction());
            ParserFunction.RegisterFunction(Constants.SCHEDULE_RUN, new ScheduleRunFunction(true));
            ParserFunction.RegisterFunction(Constants.SHOW, new ShowFunction());
            ParserFunction.RegisterFunction(Constants.SETENV, new SetEnvFunction());
            ParserFunction.RegisterFunction(Constants.SIGNAL, new SignalWaitFunction(true));
            ParserFunction.RegisterFunction(Constants.SINGLETON, new SingletonFunction());
            ParserFunction.RegisterFunction(Constants.SIZE, new SizeFunction());
            ParserFunction.RegisterFunction(Constants.SLEEP, new SleepFunction());
            ParserFunction.RegisterFunction(Constants.START_DEBUGGER, new DebuggerFunction(true));
            ParserFunction.RegisterFunction(Constants.STOP_DEBUGGER, new DebuggerFunction(false));
            ParserFunction.RegisterFunction(Constants.STR_BETWEEN, new StringManipulationFunction(StringManipulationFunction.Mode.BEETWEEN));
            ParserFunction.RegisterFunction(Constants.STR_BETWEEN_ANY, new StringManipulationFunction(StringManipulationFunction.Mode.BEETWEEN_ANY));
            ParserFunction.RegisterFunction(Constants.STR_CONTAINS, new StringManipulationFunction(StringManipulationFunction.Mode.CONTAINS));
            ParserFunction.RegisterFunction(Constants.STR_LOWER, new StringManipulationFunction(StringManipulationFunction.Mode.LOWER));
            ParserFunction.RegisterFunction(Constants.STR_ENDS_WITH, new StringManipulationFunction(StringManipulationFunction.Mode.ENDS_WITH));
            ParserFunction.RegisterFunction(Constants.STR_EQUALS, new StringManipulationFunction(StringManipulationFunction.Mode.EQUALS));
            ParserFunction.RegisterFunction(Constants.STR_INDEX_OF, new StringManipulationFunction(StringManipulationFunction.Mode.INDEX_OF));
            ParserFunction.RegisterFunction(Constants.STR_REPLACE, new StringManipulationFunction(StringManipulationFunction.Mode.REPLACE));
            ParserFunction.RegisterFunction(Constants.STR_STARTS_WITH, new StringManipulationFunction(StringManipulationFunction.Mode.STARTS_WITH));
            ParserFunction.RegisterFunction(Constants.STR_SUBSTR, new StringManipulationFunction(StringManipulationFunction.Mode.SUBSTRING));
            ParserFunction.RegisterFunction(Constants.STR_TRIM, new StringManipulationFunction(StringManipulationFunction.Mode.TRIM));
            ParserFunction.RegisterFunction(Constants.STR_UPPER, new StringManipulationFunction(StringManipulationFunction.Mode.UPPER));
            ParserFunction.RegisterFunction(Constants.THREAD, new ThreadFunction());
            ParserFunction.RegisterFunction(Constants.THREAD_ID, new ThreadIDFunction());
            ParserFunction.RegisterFunction(Constants.TOKENIZE, new TokenizeFunction());
            ParserFunction.RegisterFunction(Constants.TOKENIZE_LINES, new TokenizeLinesFunction());
            ParserFunction.RegisterFunction(Constants.TOKEN_COUNTER, new TokenCounterFunction());
            ParserFunction.RegisterFunction(Constants.TO_BOOL, new ToBoolFunction());
            ParserFunction.RegisterFunction(Constants.TO_DECIMAL, new ToDecimalFunction());
            ParserFunction.RegisterFunction(Constants.TO_DOUBLE, new ToDoubleFunction());
            ParserFunction.RegisterFunction(Constants.TO_INT, new ToIntFunction());
            //ParserFunction.RegisterFunction(Constants.TO_INTEGER, new ToIntFunction());
            ParserFunction.RegisterFunction(Constants.TO_NUMBER, new ToDoubleFunction());
            ParserFunction.RegisterFunction(Constants.TO_STRING, new ToStringFunction());
            ParserFunction.RegisterFunction(Constants.VAR, new VarFunction());
            ParserFunction.RegisterFunction(Constants.WAIT, new SignalWaitFunction(false));
            ParserFunction.RegisterFunction(Constants.WEB_REQUEST, new WebRequestFunction());

            ParserFunction.RegisterFunction(Constants.ADD_DATA, new DataFunction(DataFunction.DataMode.ADD));
            ParserFunction.RegisterFunction(Constants.COLLECT_DATA, new DataFunction(DataFunction.DataMode.SUBSCRIBE));
            ParserFunction.RegisterFunction(Constants.GET_DATA, new DataFunction(DataFunction.DataMode.SEND));

            // Math Functions
            ParserFunction.RegisterFunction(Constants.MATH_ABS, new AbsFunction());
            ParserFunction.RegisterFunction(Constants.MATH_ACOS, new AcosFunction());
            ParserFunction.RegisterFunction(Constants.MATH_ACOSH, new AcoshFunction());
            ParserFunction.RegisterFunction(Constants.MATH_ASIN, new AsinFunction());
            ParserFunction.RegisterFunction(Constants.MATH_ASINH, new AsinhFunction());
            ParserFunction.RegisterFunction(Constants.MATH_ATAN, new TanFunction());
            ParserFunction.RegisterFunction(Constants.MATH_ATAN2, new Atan2Function());
            ParserFunction.RegisterFunction(Constants.MATH_ATANH, new AtanhFunction());
            ParserFunction.RegisterFunction(Constants.MATH_CBRT, new CbrtFunction());
            ParserFunction.RegisterFunction(Constants.MATH_CEIL, new CeilFunction());
            ParserFunction.RegisterFunction(Constants.MATH_COS, new CosFunction());
            ParserFunction.RegisterFunction(Constants.MATH_COSH, new CoshFunction());
            ParserFunction.RegisterFunction(Constants.MATH_E, new EFunction());
            ParserFunction.RegisterFunction(Constants.MATH_EXP, new ExpFunction());
            ParserFunction.RegisterFunction(Constants.MATH_FLOOR, new FloorFunction());
            ParserFunction.RegisterFunction(Constants.MATH_LN2, new Ln2Function());
            ParserFunction.RegisterFunction(Constants.MATH_LN10, new Ln10Function());
            ParserFunction.RegisterFunction(Constants.MATH_LOG, new LogFunction());
            ParserFunction.RegisterFunction(Constants.MATH_LOG2E, new Log2EFunction());
            ParserFunction.RegisterFunction(Constants.MATH_LOG10E, new Log10EFunction());
            ParserFunction.RegisterFunction(Constants.MATH_MIN, new MinFunction());
            ParserFunction.RegisterFunction(Constants.MATH_MAX, new MaxFunction());
            ParserFunction.RegisterFunction(Constants.MATH_PI, new PiFunction());
            ParserFunction.RegisterFunction(Constants.MATH_POW, new PowFunction());
            ParserFunction.RegisterFunction(Constants.MATH_RANDOM, new GetRandomFunction(true));
            ParserFunction.RegisterFunction(Constants.MATH_ROUND, new RoundFunction());
            ParserFunction.RegisterFunction(Constants.MATH_SQRT, new SqrtFunction());
            ParserFunction.RegisterFunction(Constants.MATH_SQRT1_2, new Sqrt1_2Function());
            ParserFunction.RegisterFunction(Constants.MATH_SQRT2, new Sqrt2Function());
            ParserFunction.RegisterFunction(Constants.MATH_SIGN, new SignFunction());
            ParserFunction.RegisterFunction(Constants.MATH_SIN, new SinFunction());
            ParserFunction.RegisterFunction(Constants.MATH_SINH, new SinhFunction());
            ParserFunction.RegisterFunction(Constants.MATH_TAN, new TanFunction());
            ParserFunction.RegisterFunction(Constants.MATH_TANH, new TanhFunction());
            ParserFunction.RegisterFunction(Constants.MATH_TRUNC, new FloorFunction());

            ParserFunction.RegisterFunction(Constants.CONSOLE_LOG, new PrintFunction());


            ParserFunction.RegisterFunction(Constants.OBJECT_DEFPROP, new ObjectPropsFunction());
        }

        public void RegisterEnums()
        {
            ParserFunction.RegisterEnum(Constants.VARIABLE_TYPE, "SplitAndMerge.Variable.VarType");
        }

        public void RegisterActions()
        {
            ParserFunction.AddAction(Constants.ASSIGNMENT, new AssignFunction());
            ParserFunction.AddAction(Constants.INCREMENT, new IncrementDecrementFunction());
            ParserFunction.AddAction(Constants.DECREMENT, new IncrementDecrementFunction());

            for (int i = 0; i < Constants.OPER_ACTIONS.Length; i++)
            {
                ParserFunction.AddAction(Constants.OPER_ACTIONS[i], new OperatorAssignFunction());
            }
        }

        public Variable ProcessFile(string filename, bool mainFile = false)
        {
            string script = Utils.GetFileContents(filename);
            return Process(script, filename, mainFile);
        }

        public async Task<Variable> ProcessFileAsync(string filename, bool mainFile = false)
        {
            string script = Utils.GetFileContents(filename);
            Variable result = await ProcessAsync(script, filename, mainFile);
            return result;
        }

        public Variable Process(string script, string filename = "", bool mainFile = false)
        {
            Dictionary<int, int> char2Line;
            string data = Utils.ConvertToScript(script, out char2Line, filename);
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            ParsingScript toParse = new ParsingScript(data, 0, char2Line);
            toParse.OriginalScript = script;
            toParse.Filename = filename;

            if (mainFile)
            {
                toParse.MainFilename = toParse.Filename;
            }

            Variable result = null;

            while (toParse.Pointer < data.Length)
            {
                result = toParse.Execute();
                toParse.GoToNextStatement();
            }

            return result;
        }
        public async Task<Variable> ProcessAsync(string script, string filename = "", bool mainFile = false)
        {
            Dictionary<int, int> char2Line;
            string data = Utils.ConvertToScript(script, out char2Line, filename);
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            ParsingScript toParse = new ParsingScript(data, 0, char2Line);
            toParse.OriginalScript = script;
            toParse.Filename = filename;

            if (mainFile)
            {
                toParse.MainFilename = toParse.Filename;
            }

            Variable result = null;

            while (toParse.Pointer < data.Length)
            {
                result = await toParse.ExecuteAsync();
                toParse.GoToNextStatement();
            }

            return result;
        }

        internal Variable ProcessFor(ParsingScript script)
        {
            string forString = Utils.GetBodyBetween(script, Constants.START_ARG, Constants.END_ARG);
            script.Forward();
            if (forString.Contains(Constants.END_STATEMENT.ToString()))
            {
                // Looks like: "for(i = 0; i < 10; i++)".
                ProcessCanonicalFor(script, forString);
            }
            else
            {
                // Otherwise looks like: "for(item : array)"
                ProcessArrayFor(script, forString);
            }

            return Variable.EmptyInstance;
        }
        internal async Task<Variable> ProcessForAsync(ParsingScript script)
        {
            string forString = Utils.GetBodyBetween(script, Constants.START_ARG, Constants.END_ARG);
            script.Forward();
            if (forString.Contains(Constants.END_STATEMENT.ToString()))
            {
                // Looks like: "for(i = 0; i < 10; i++)".
                await ProcessCanonicalForAsync(script, forString);
            }
            else
            {
                // Otherwise looks like: "for(item : array)"
                await ProcessArrayForAsync(script, forString);
            }

            return Variable.EmptyInstance;
        }

        void ProcessArrayFor(ParsingScript script, string forString)
        {
            var tokens = forString.Split(' ');
            var sep = tokens.Length > 2 ? tokens[1] : "";
            string varName = tokens[0];

            if (sep != Constants.FOR_EACH && sep != Constants.FOR_IN && sep != Constants.FOR_OF)
            {
                int index = forString.IndexOf(Constants.FOR_EACH);
                if (index <= 0 || index == forString.Length - 1)
                {
                    Utils.ThrowErrorMsg("Expecting: for(item :/in/of array)",
                                     script, Constants.FOR);
                }
                varName = forString.Substring(0, index);
            }

            ParsingScript forScript = script.GetTempScript(forString, varName.Length + sep.Length + 1);
            forScript.Debugger = script.Debugger;

            Variable arrayValue = Utils.GetItem(forScript);

            if (arrayValue.Type == Variable.VarType.STRING)
            {
                arrayValue = new Variable(new List<string>(arrayValue.ToString().ToCharArray().Select(c => c.ToString())));
            }

            int cycles = arrayValue.Count;
            if (cycles == 0)
            {
                SkipBlock(script);
                return;
            }
            int startForCondition = script.Pointer;

            for (int i = 0; i < cycles; i++)
            {
                script.Pointer = startForCondition;
                Variable current = arrayValue.GetValue(i);
                ParserFunction.AddGlobalOrLocalVariable(varName,
                               new GetVarFunction(current));
                Variable result = ProcessBlock(script);
                if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                {
                    //script.Pointer = startForCondition;
                    //SkipBlock(script);
                    //return;
                    break;
                }
            }
            script.Pointer = startForCondition;
            SkipBlock(script);
        }

        async Task ProcessArrayForAsync(ParsingScript script, string forString)
        {
            var tokens = forString.Split(' ');
            var sep = tokens.Length > 2 ? tokens[1] : "";
            string varName = tokens[0];

            if (sep != Constants.FOR_EACH && sep != Constants.FOR_IN && sep != Constants.FOR_OF)
            {
                int index = forString.IndexOf(Constants.FOR_EACH);
                if (index <= 0 || index == forString.Length - 1)
                {
                    Utils.ThrowErrorMsg("Expecting: for(item :/in/of array)",
                                     script, Constants.FOR);
                }
                varName = forString.Substring(0, index);
            }

            ParsingScript forScript = script.GetTempScript(forString, varName.Length + sep.Length + 1);
            forScript.Debugger = script.Debugger;

            Variable arrayValue = await Utils.GetItemAsync(forScript);

            if (arrayValue.Type == Variable.VarType.STRING)
            {
                arrayValue = new Variable(new List<string>(arrayValue.ToString().ToCharArray().Select(c => c.ToString())));
            }

            int cycles = arrayValue.Count;
            if (cycles == 0)
            {
                SkipBlock(script);
                return;
            }
            int startForCondition = script.Pointer;

            for (int i = 0; i < cycles; i++)
            {
                script.Pointer = startForCondition;
                Variable current = arrayValue.GetValue(i);
                ParserFunction.AddGlobalOrLocalVariable(varName,
                               new GetVarFunction(current));
                Variable result = await ProcessBlockAsync(script);
                if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                {
                    //script.Pointer = startForCondition;
                    //SkipBlock(script);
                    //return;
                    break;
                }
            }
            script.Pointer = startForCondition;
            SkipBlock(script);
        }

        void ProcessCanonicalFor(ParsingScript script, string forString)
        {
            string[] forTokens = forString.Split(Constants.END_STATEMENT);
            if (forTokens.Length != 3)
            {
                throw new ArgumentException("Expecting: for(init; condition; loopStatement)");
            }

            int startForCondition = script.Pointer;

            ParsingScript initScript = script.GetTempScript(forTokens[0] + Constants.END_STATEMENT);
            ParsingScript condScript = script.GetTempScript(forTokens[1] + Constants.END_STATEMENT);
            ParsingScript loopScript = script.GetTempScript(forTokens[2] + Constants.END_STATEMENT);

            initScript.Execute(null, 0);

            int cycles = 0;
            bool stillValid = true;

            while (stillValid)
            {
                Variable condResult = condScript.Execute(null, 0);
                stillValid = Convert.ToBoolean(condResult.Value);
                if (!stillValid)
                {
                    break;
                }

                if (MAX_LOOPS > 0 && ++cycles >= MAX_LOOPS)
                {
                    throw new ArgumentException("Looks like an infinite loop after " +
                                                  cycles + " cycles.");
                }

                script.Pointer = startForCondition;
                Variable result = ProcessBlock(script);
                if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                {
                    //script.Pointer = startForCondition;
                    //SkipBlock(script);
                    //return;
                    break;
                }
                loopScript.Execute(null, 0);
            }

            script.Pointer = startForCondition;
            SkipBlock(script);
        }
        async Task ProcessCanonicalForAsync(ParsingScript script, string forString)
        {
            string[] forTokens = forString.Split(Constants.END_STATEMENT);
            if (forTokens.Length != 3)
            {
                throw new ArgumentException("Expecting: for(init; condition; loopStatement)");
            }

            int startForCondition = script.Pointer;

            ParsingScript initScript = script.GetTempScript(forTokens[0] + Constants.END_STATEMENT);
            ParsingScript condScript = script.GetTempScript(forTokens[1] + Constants.END_STATEMENT);
            ParsingScript loopScript = script.GetTempScript(forTokens[2] + Constants.END_STATEMENT);

            await initScript.ExecuteAsync(null, 0);

            int cycles = 0;
            bool stillValid = true;

            while (stillValid)
            {
                Variable condResult = await condScript.ExecuteAsync(null, 0);
                stillValid = Convert.ToBoolean(condResult.Value);
                if (!stillValid)
                {
                    break;
                }

                if (MAX_LOOPS > 0 && ++cycles >= MAX_LOOPS)
                {
                    throw new ArgumentException("Looks like an infinite loop after " +
                                                  cycles + " cycles.");
                }

                script.Pointer = startForCondition;
                Variable result = await ProcessBlockAsync(script);
                if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                {
                    //script.Pointer = startForCondition;
                    //SkipBlock(script);
                    //return;
                    break;
                }
                await loopScript.ExecuteAsync(null, 0);
            }

            script.Pointer = startForCondition;
            SkipBlock(script);
        }

        internal Variable ProcessWhile(ParsingScript script)
        {
            int startWhileCondition = script.Pointer;

            // A check against an infinite loop.
            int cycles = 0;
            bool stillValid = true;
            Variable result = Variable.EmptyInstance;

            while (stillValid)
            {
                script.Pointer = startWhileCondition;

                //int startSkipOnBreakChar = from;
                Variable condResult = script.Execute(Constants.END_ARG_ARRAY);
                stillValid = Convert.ToBoolean(condResult.Value);
                if (!stillValid)
                {
                    break;
                }

                // Check for an infinite loop if we are comparing same values:
                if (MAX_LOOPS > 0 && ++cycles >= MAX_LOOPS)
                {
                    throw new ArgumentException("Looks like an infinite loop after " +
                        cycles + " cycles.");
                }

                result = ProcessBlock(script);
                if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                {
                    script.Pointer = startWhileCondition;
                    break;
                }
            }

            // The while condition is not true anymore: must skip the whole while
            // block before continuing with next statements.
            SkipBlock(script);
            return result.IsReturn ? result : Variable.EmptyInstance;
        }

        internal async Task<Variable> ProcessWhileAsync(ParsingScript script)
        {
            int startWhileCondition = script.Pointer;

            // A check against an infinite loop.
            int cycles = 0;
            bool stillValid = true;
            Variable result = Variable.EmptyInstance;

            while (stillValid)
            {
                script.Pointer = startWhileCondition;

                //int startSkipOnBreakChar = from;
                Variable condResult = await script.ExecuteAsync(Constants.END_ARG_ARRAY);
                stillValid = Convert.ToBoolean(condResult.Value);
                if (!stillValid)
                {
                    break;
                }

                // Check for an infinite loop if we are comparing same values:
                if (MAX_LOOPS > 0 && ++cycles >= MAX_LOOPS)
                {
                    throw new ArgumentException("Looks like an infinite loop after " +
                        cycles + " cycles.");
                }

                result = await ProcessBlockAsync(script);
                if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                {
                    script.Pointer = startWhileCondition;
                    break;
                }
            }

            // The while condition is not true anymore: must skip the whole while
            // block before continuing with next statements.
            SkipBlock(script);
            return result.IsReturn ? result : Variable.EmptyInstance;
        }

        internal Variable ProcessDoWhile(ParsingScript script)
        {
            int startDoCondition = script.Pointer;
            bool stillValid = true;
            Variable result = Variable.EmptyInstance;

            while (stillValid)
            {
                script.Pointer = startDoCondition;

                result = ProcessBlock(script);
                if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                {
                    script.Pointer = startDoCondition;
                    break;
                }
                script.Forward(Constants.WHILE.Length + 1);
                Variable condResult = script.Execute(Constants.END_ARG_ARRAY);
                stillValid = Convert.ToBoolean(condResult.Value);
                if (!stillValid)
                {
                    break;
                }
            }

            SkipBlock(script);
            return result.IsReturn ? result : Variable.EmptyInstance;
        }

        internal Variable ProcessCase(ParsingScript script, string reason)
        {
            if (reason == Constants.CASE)
            {
                /*var token = */
                Utils.GetToken(script, Constants.TOKEN_SEPARATION);
            }
            script.MoveForwardIf(':');

            Variable result = ProcessBlock(script);
            script.MoveBackIfPrevious('}');

            return result;
        }

        internal Variable ProcessSwitch(ParsingScript script)
        {
            Variable switchValue = Utils.GetItem(script);
            script.Forward();

            Variable result = Variable.EmptyInstance;
            var caseSep = ":".ToCharArray();

            bool caseDone = false;

            while (script.StillValid())
            {
                var nextToken = Utils.GetBodySize(script, Constants.CASE, Constants.DEFAULT);
                if (string.IsNullOrEmpty(nextToken))
                {
                    break;
                }
                if (nextToken == Constants.DEFAULT && !caseDone)
                {
                    result = ProcessBlock(script);
                    break;
                }
                if (!caseDone)
                {
                    Variable caseValue = script.Execute(caseSep);
                    script.Forward();

                    if (switchValue.Type == caseValue.Type && switchValue.Equals(caseValue))
                    {
                        caseDone = true;
                        result = ProcessBlock(script);
                        if (script.Prev == '}')
                        {
                            break;
                        }
                        script.Forward();
                    }
                }
            }
            script.MoveForwardIfNotPrevious('}');
            script.GoToNextStatement();
            return result;
        }

        internal Variable ProcessIf(ParsingScript script)
        {
            int startIfCondition = script.Pointer;

            Variable result = script.Execute(Constants.END_ARG_ARRAY);
            bool isTrue = Convert.ToBoolean(result.Value);

            if (isTrue)
            {
                result = ProcessBlock(script);

                if (result.IsReturn ||
                    result.Type == Variable.VarType.BREAK ||
                    result.Type == Variable.VarType.CONTINUE)
                {
                    // We are here from the middle of the if-block. Skip it.
                    script.Pointer = startIfCondition;
                    SkipBlock(script);
                }
                SkipRestBlocks(script);

                //return result;
                return result.IsReturn ||
                       result.Type == Variable.VarType.BREAK ||
                       result.Type == Variable.VarType.CONTINUE ? result : Variable.EmptyInstance;
            }

            // We are in Else. Skip everything in the If statement.
            SkipBlock(script);

            ParsingScript nextData = new ParsingScript(script);
            nextData.ParentScript = script;

            string nextToken = Utils.GetNextToken(nextData);

            if (Constants.ELSE_IF == nextToken)
            {
                script.Pointer = nextData.Pointer + 1;
                result = ProcessIf(script);
            }
            else if (Constants.ELSE == nextToken)
            {
                script.Pointer = nextData.Pointer + 1;
                result = ProcessBlock(script);
            }

            return result.IsReturn ||
                   result.Type == Variable.VarType.BREAK ||
                   result.Type == Variable.VarType.CONTINUE ? result : Variable.EmptyInstance;
        }

        internal async Task<Variable> ProcessIfAsync(ParsingScript script)
        {
            int startIfCondition = script.Pointer;

            Variable result = await script.ExecuteAsync(Constants.END_ARG_ARRAY);
            bool isTrue = Convert.ToBoolean(result.Value);

            if (isTrue)
            {
                result = await ProcessBlockAsync(script);

                if (result.IsReturn ||
                    result.Type == Variable.VarType.BREAK ||
                    result.Type == Variable.VarType.CONTINUE)
                {
                    // We are here from the middle of the if-block. Skip it.
                    script.Pointer = startIfCondition;
                    SkipBlock(script);
                }
                SkipRestBlocks(script);

                //return result;
                return result.IsReturn ||
                       result.Type == Variable.VarType.BREAK ||
                       result.Type == Variable.VarType.CONTINUE ? result : Variable.EmptyInstance;
            }

            // We are in Else. Skip everything in the If statement.
            SkipBlock(script);

            ParsingScript nextData = new ParsingScript(script);
            nextData.ParentScript = script;

            string nextToken = Utils.GetNextToken(nextData);

            if (Constants.ELSE_IF == nextToken)
            {
                script.Pointer = nextData.Pointer + 1;
                result = await ProcessIfAsync(script);
            }
            else if (Constants.ELSE == nextToken)
            {
                script.Pointer = nextData.Pointer + 1;
                result = await ProcessBlockAsync(script);
            }

            return result.IsReturn ||
                   result.Type == Variable.VarType.BREAK ||
                   result.Type == Variable.VarType.CONTINUE ? result : Variable.EmptyInstance;
        }

        internal Variable ProcessTry(ParsingScript script)
        {
            int startTryCondition = script.Pointer - 1;
            int currentStackLevel = ParserFunction.GetCurrentStackLevel();
            Exception exception = null;

            Variable result = null;

            bool alreadyInTryBlock = script.InTryBlock;
            script.InTryBlock = true;
            try
            {
                result = ProcessBlock(script);
            }
            catch (Exception exc)
            {
                exception = exc;
            }
            finally
            {
                script.InTryBlock = alreadyInTryBlock;
            }

            if (exception != null || result.IsReturn ||
                result.Type == Variable.VarType.BREAK ||
                result.Type == Variable.VarType.CONTINUE)
            {
                // We are here from the middle of the try-block either because
                // an exception was thrown or because of a Break/Continue. Skip it.
                script.Pointer = startTryCondition;
                SkipBlock(script);
            }

            string catchToken = Utils.GetNextToken(script);
            script.Forward(); // skip opening parenthesis
                              // The next token after the try block must be a catch.
            if (Constants.CATCH != catchToken)
            {
                throw new ArgumentException("Expecting a 'catch()' but got [" +
                    catchToken + "]");
            }

            string exceptionName = Utils.GetNextToken(script);
            script.Forward(); // skip closing parenthesis

            if (exception != null)
            {
                string excStack = CreateExceptionStack(exceptionName, currentStackLevel);
                ParserFunction.InvalidateStacksAfterLevel(currentStackLevel);

                GetVarFunction excMsgFunc = new GetVarFunction(new Variable(exception.Message));
                ParserFunction.AddGlobalOrLocalVariable(exceptionName, excMsgFunc);
                GetVarFunction excStackFunc = new GetVarFunction(new Variable(excStack));
                ParserFunction.AddGlobalOrLocalVariable(exceptionName + ".Stack", excStackFunc);

                result = ProcessBlock(script);
                ParserFunction.PopLocalVariable(exceptionName);
            }
            else
            {
                SkipBlock(script);
            }

            SkipRestBlocks(script);
            return result;
        }
        internal async Task<Variable> ProcessTryAsync(ParsingScript script)
        {
            int startTryCondition = script.Pointer - 1;
            int currentStackLevel = ParserFunction.GetCurrentStackLevel();
            Exception exception = null;

            Variable result = null;

            bool alreadyInTryBlock = script.InTryBlock;
            script.InTryBlock = true;
            try
            {
                result = await ProcessBlockAsync(script);
            }
            catch (Exception exc)
            {
                exception = exc;
            }
            finally
            {
                script.InTryBlock = alreadyInTryBlock;
            }

            if (exception != null || result.IsReturn ||
                result.Type == Variable.VarType.BREAK ||
                result.Type == Variable.VarType.CONTINUE)
            {
                // We are here from the middle of the try-block either because
                // an exception was thrown or because of a Break/Continue. Skip it.
                script.Pointer = startTryCondition;
                SkipBlock(script);
            }

            string catchToken = Utils.GetNextToken(script);
            script.Forward(); // skip opening parenthesis
                              // The next token after the try block must be a catch.
            if (Constants.CATCH != catchToken)
            {
                throw new ArgumentException("Expecting a 'catch()' but got [" +
                    catchToken + "]");
            }

            string exceptionName = Utils.GetNextToken(script);
            script.Forward(); // skip closing parenthesis

            if (exception != null)
            {
                string excStack = CreateExceptionStack(exceptionName, currentStackLevel);
                ParserFunction.InvalidateStacksAfterLevel(currentStackLevel);

                GetVarFunction excMsgFunc = new GetVarFunction(new Variable(exception.Message));
                ParserFunction.AddGlobalOrLocalVariable(exceptionName, excMsgFunc);
                GetVarFunction excStackFunc = new GetVarFunction(new Variable(excStack));
                ParserFunction.AddGlobalOrLocalVariable(exceptionName + ".Stack", excStackFunc);

                result = await ProcessBlockAsync(script);
                ParserFunction.PopLocalVariable(exceptionName);
            }
            else
            {
                SkipBlock(script);
            }

            SkipRestBlocks(script);
            return result;
        }

        private static string CreateExceptionStack(string exceptionName, int lowestStackLevel)
        {
            string result = "";
            Stack<ParserFunction.StackLevel> stack = ParserFunction.ExecutionStack;
            int level = stack.Count;
            foreach (ParserFunction.StackLevel stackLevel in stack)
            {
                if (level-- < lowestStackLevel)
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(stackLevel.Name))
                {
                    continue;
                }
                result += Environment.NewLine + "  " + stackLevel.Name + "()";
            }

            if (!string.IsNullOrWhiteSpace(result))
            {
                result = " --> " + exceptionName + result;
            }

            return result;
        }
        public static string GetStack(int lowestStackLevel = 0)
        {
            string result = "";
            Stack<ParserFunction.StackLevel> stack = ParserFunction.ExecutionStack;
            int level = stack.Count;
            foreach (ParserFunction.StackLevel stackLevel in stack)
            {
                if (level-- < lowestStackLevel)
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(stackLevel.Name))
                {
                    continue;
                }
                result += Environment.NewLine + "  " + stackLevel.Name + "()";
            }

            return result;
        }

        private Variable ProcessBlock(ParsingScript script)
        {
            int blockStart = script.Pointer;
            Variable result = null;

            if (script.Debugger != null)
            {
                bool done = false;
                result = script.Debugger.DebugBlockIfNeeded(script, done, (newDone) => { done = newDone; }).Result;
                if (done)
                {
                    return result;
                }
            }
            while (script.StillValid())
            {
                int endGroupRead = script.GoToNextStatement();
                if (endGroupRead > 0 || !script.StillValid())
                {
                    return result != null ? result : new Variable();
                }

                result = script.Execute();

                if (result.IsReturn ||
                    result.Type == Variable.VarType.BREAK ||
                    result.Type == Variable.VarType.CONTINUE)
                {
                    return result;
                }
            }
            return result;
        }

        private async Task<Variable> ProcessBlockAsync(ParsingScript script)
        {
            int blockStart = script.Pointer;
            Variable result = null;

            if (script.Debugger != null)
            {
                bool done = false;
                result = await script.Debugger.DebugBlockIfNeeded(script, done, (newDone) => { done = newDone; });
                if (done)
                {
                    return result;
                }
            }
            while (script.StillValid())
            {
                int endGroupRead = script.GoToNextStatement();
                if (endGroupRead > 0 || !script.StillValid())
                {
                    return result != null ? result : new Variable();
                }

                result = await script.ExecuteAsync();

                if (result.IsReturn ||
                    result.Type == Variable.VarType.BREAK ||
                    result.Type == Variable.VarType.CONTINUE)
                {
                    return result;
                }
            }
            return result;
        }

        private void SkipBlock(ParsingScript script)
        {
            int blockStart = script.Pointer;
            int startCount = 0;
            int endCount = 0;
            bool inQuotes = false;
            bool inQuotes1 = false;
            bool inQuotes2 = false;
            char previous = Constants.EMPTY;
            char prevprev = Constants.EMPTY;
            while (startCount == 0 || startCount > endCount)
            {
                if (!script.StillValid())
                {
                    throw new ArgumentException("Couldn't skip block [" +
                    script.Substr(blockStart, Constants.MAX_CHARS_TO_SHOW) + "]");
                }
                char currentChar = script.CurrentAndForward();
                switch (currentChar)
                {
                    case Constants.QUOTE1:
                        if (!inQuotes2 && (previous != '\\' || prevprev == '\\'))
                        {
                            inQuotes = inQuotes1 = !inQuotes1;
                        }
                        break;
                    case Constants.QUOTE:
                        if (!inQuotes1 && (previous != '\\' || prevprev == '\\'))
                        {
                            inQuotes = inQuotes2 = !inQuotes2;
                        }
                        break;
                    case Constants.START_GROUP:
                        if (!inQuotes)
                        {
                            startCount++;
                        }
                        break;
                    case Constants.END_GROUP:
                        if (!inQuotes)
                        {
                            endCount++;
                        }
                        break;
                }
                prevprev = previous;
                previous = currentChar;
            }

            if (startCount != endCount)
            {
                throw new ArgumentException("Mismatched parentheses");
            }
        }

        private void SkipRestBlocks(ParsingScript script)
        {
            while (script.StillValid())
            {
                int endOfToken = script.Pointer;
                ParsingScript nextData = new ParsingScript(script);
                string nextToken = Utils.GetNextToken(nextData);
                if (Constants.ELSE_IF != nextToken &&
                    Constants.ELSE != nextToken)
                {
                    return;
                }
                script.Pointer = nextData.Pointer;
                SkipBlock(script);
            }
        }

        public static Variable Run(string functionName, Variable arg1 = null, Variable arg2 = null, Variable arg3 = null, ParsingScript script = null)
        {
            System.Threading.Tasks.Task<Variable> task = null;
            try
            {
                task = CustomFunction.Run(functionName, arg1, arg2, arg3, script);
            }
            catch (Exception exc)
            {
                task = CustomFunction.Run(Constants.ON_EXCEPTION, new Variable(functionName),
                                          new Variable(exc.Message), arg2, script);
                if (task == null)
                {
                    throw;
                }
            }
            return task == null ? Variable.EmptyInstance : task.Result;
        }


        public static Variable Run(CustomFunction function, List<Variable> args, ParsingScript script = null)
        {
            Variable result = null;
            try
            {
                result = function.Run(args, script);
            }
            catch (Exception exc)
            {
                var task = CustomFunction.Run(Constants.ON_EXCEPTION, new Variable(function.Name),
                                          new Variable(exc.Message), args.Count > 0 ? args[0] : Variable.EmptyInstance, script);
                if (task == null)
                {
                    throw;
                }
                result = task.Result;
            }
            return result;
        }
    }
}

