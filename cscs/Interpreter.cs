using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SplitAndMerge.ParserFunction;
using System.IO;
using System.Xml.Linq;
using static System.Collections.Specialized.BitVector32;


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
        #region Original Interpreter code (tweaked in some places)

        public TranslationManager Translation { get; private set; }

        private static Interpreter firstInstance;
        private static Interpreter lastInstance;
        private static bool m_bHasBeenInitialized = false;

        public static Interpreter FirstInstance
        {
            get
            {
                if (firstInstance == null)
                {
                    firstInstance = lastInstance;
                    if (firstInstance == null)
                    {
                        firstInstance = lastInstance = new Interpreter();
                    }
                }
                return firstInstance;
            }
        }
        public static Interpreter LastInstance
        {
            get
            {
                if (lastInstance == null)
                {
                    lastInstance = firstInstance;
                    if (lastInstance == null)
                    {
                        firstInstance = lastInstance = new Interpreter();
                    }
                }
                return lastInstance;
            }
            set
            {
                lastInstance = value;
            }
        }

        // Global functions:

        // TODO: Pass this a collection of ScriptModule objects.
        // Each ScriptModule can add functionality
        public Interpreter(int id = 1)
        {
            Id = id;
            Init();
            if (firstInstance == null)
            {
                firstInstance = this;
            }
            lastInstance = this;
        }

        public int Id { get; set; }

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
            else
            {
                Console.WriteLine(text);
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

        public bool IsRunning { get; set; } = true;
        public int ExitCode { get; set; }

        private void Init()
        {
            Translation = new TranslationManager(this);

            RegisterFunctions();
            RegisterEnums();
            RegisterActions();

            AddGlobal(Constants.THIS,
                new GetVarFunction(new Variable(Variable.VarType.ARRAY)));

            InitStandalone();

        }

        public void RegisterFunctions()
        {
            RegisterFunction(Constants.IF, new IfStatement());
            RegisterFunction(Constants.IFF, new IffStatement());
            RegisterFunction(Constants.DO, new DoWhileStatement());
            RegisterFunction(Constants.WHILE, new WhileStatement());
            RegisterFunction(Constants.SWITCH, new SwitchStatement());
            RegisterFunction(Constants.CASE, new CaseStatement());
            RegisterFunction(Constants.DEFAULT, new CaseStatement());
            RegisterFunction(Constants.FOR, new ForStatement());
            RegisterFunction(Constants.BREAK, new BreakStatement());
            RegisterFunction(Constants.COMPILED_FUNCTION, new CompiledFunctionCreator(false));
            RegisterFunction(Constants.CONTINUE, new ContinueStatement());
            RegisterFunction(Constants.CLASS, new ClassCreator());
            RegisterFunction(Constants.ENUM, new EnumFunction());
            RegisterFunction(Constants.FREE, new FreeFunction());
            RegisterFunction(Constants.NEW, new NewObjectFunction());
            RegisterFunction(Constants.NULL, new NullFunction());
            RegisterFunction(Constants.RETURN, new ReturnStatement());
            RegisterFunction(Constants.FUNCTION, new FunctionCreator());
            RegisterFunction(Constants.GET_PROPERTIES, new GetPropertiesFunction());
            RegisterFunction(Constants.GET_PROPERTY, new GetPropertyFunction());
            RegisterFunction(Constants.INCLUDE, new IncludeFile());
            RegisterFunction(Constants.MARSHAL, new MarshalFunction(true));
            RegisterFunction(Constants.QUIT, new QuitFunction());
            RegisterFunction(Constants.SET_PROPERTY, new SetPropertyFunction());
            RegisterFunction(Constants.TRY, new TryBlock());
            RegisterFunction(Constants.THROW, new ThrowFunction());
            RegisterFunction(Constants.TYPE, new TypeFunction());
            RegisterFunction(Constants.TYPE_OF, new TypeOfFunction());
            RegisterFunction(Constants.TYPE_REF, new TypeRefFunction());
            RegisterFunction(Constants.TRUE, new BoolFunction(true));
            RegisterFunction(Constants.FALSE, new BoolFunction(false));
            RegisterFunction(Constants.UNDEFINED, new UndefinedFunction());
            RegisterFunction(Constants.UNMARSHAL, new MarshalFunction(false));

            RegisterFunction(Constants.ADD, new AddFunction());
            RegisterFunction(Constants.ADD_TO_HASH, new AddVariableToHashFunction());
            RegisterFunction(Constants.ADD_ALL_TO_HASH, new AddVariablesToHashFunction());
            RegisterFunction(Constants.CANCEL, new CancelFunction());
            RegisterFunction(Constants.CANCEL_RUN, new ScheduleRunFunction(false));
            RegisterFunction(Constants.CHECK_LOADER_MAIN, new CheckLoaderMainFunction());
            RegisterFunction(Constants.COMMLINE_ARGS, new CommandLineArgsFunction());
            RegisterFunction(Constants.CONTAINS, new ContainsFunction());
            RegisterFunction(Constants.CURRENT_PATH, new CurrentPathFunction());
            RegisterFunction(Constants.DATE_TIME, new DateTimeFunction(false));
            RegisterFunction(Constants.DECODE, new EncodeDecodeFunction(false));
            RegisterFunction(Constants.DEEP_COPY, new DeepCopyFunction());
            RegisterFunction(Constants.DEFINE_LOCAL, new DefineLocalFunction());
            RegisterFunction(Constants.DOWNLOAD, new DownloadFileFunction());
            RegisterFunction(Constants.ENCODE, new EncodeDecodeFunction(true));
            RegisterFunction(Constants.ENV, new GetEnvFunction());
            RegisterFunction(Constants.FIND_INDEX, new FindIndexFunction());
            RegisterFunction(Constants.GET_COLUMN, new GetColumnFunction());
            RegisterFunction(Constants.GET_FILE_FROM_DEBUGGER, new GetFileFromDebugger());
            RegisterFunction(Constants.GET_KEYS, new GetAllKeysFunction());
            RegisterFunction(Constants.HELP, new HelpFunction());
            RegisterFunction(Constants.INCLUDE_SECURE, new IncludeFileSecure());
            RegisterFunction(Constants.JSON, new GetVariableFromJSONFunction());
            RegisterFunction(Constants.LOCK, new LockFunction());
            RegisterFunction(Constants.NAMESPACE, new NamespaceFunction());
            RegisterFunction(Constants.NAME_EXISTS, new NameExistsFunction());
            RegisterFunction(Constants.NEW_THREAD, new ThreadFunction(false, false));
            RegisterFunction(Constants.NOW, new DateTimeFunction());
            RegisterFunction(Constants.PRINT, new PrintFunction());
            RegisterFunction(Constants.PSTIME, new ProcessorTimeFunction());
            RegisterFunction(Constants.REGEX, new RegexFunction());
            RegisterFunction(Constants.REMOVE, new RemoveFunction());
            RegisterFunction(Constants.REMOVE_AT, new RemoveAtFunction());
            RegisterFunction(Constants.RESET_VARS, new ResetVariablesFunction());
            RegisterFunction(Constants.SCHEDULE_RUN, new ScheduleRunFunction(true));
            RegisterFunction(Constants.SHOW, new ShowFunction());
            RegisterFunction(Constants.SETENV, new SetEnvFunction());
            RegisterFunction(Constants.SIGNAL, new SignalWaitFunction(true));
            RegisterFunction(Constants.SINGLETON, new SingletonFunction());
            RegisterFunction(Constants.SIZE, new SizeFunction());
            RegisterFunction(Constants.SLEEP, new SleepFunction());
            RegisterFunction(Constants.START_DEBUGGER, new DebuggerFunction(true));
            RegisterFunction(Constants.STOP_DEBUGGER, new DebuggerFunction(false));
            RegisterFunction(Constants.STR_BETWEEN, new StringManipulationFunction(StringManipulationFunction.Mode.BEETWEEN));
            RegisterFunction(Constants.STR_BETWEEN_ANY, new StringManipulationFunction(StringManipulationFunction.Mode.BEETWEEN_ANY));
            RegisterFunction(Constants.STR_CONTAINS, new StringManipulationFunction(StringManipulationFunction.Mode.CONTAINS));
            RegisterFunction(Constants.STR_LOWER, new StringManipulationFunction(StringManipulationFunction.Mode.LOWER));
            RegisterFunction(Constants.STR_ENDS_WITH, new StringManipulationFunction(StringManipulationFunction.Mode.ENDS_WITH));
            RegisterFunction(Constants.STR_EQUALS, new StringManipulationFunction(StringManipulationFunction.Mode.EQUALS));
            RegisterFunction(Constants.STR_INDEX_OF, new StringManipulationFunction(StringManipulationFunction.Mode.INDEX_OF));
            RegisterFunction(Constants.STR_REPLACE, new StringManipulationFunction(StringManipulationFunction.Mode.REPLACE));
            RegisterFunction(Constants.STR_STARTS_WITH, new StringManipulationFunction(StringManipulationFunction.Mode.STARTS_WITH));
            RegisterFunction(Constants.STR_SUBSTR, new StringManipulationFunction(StringManipulationFunction.Mode.SUBSTRING));
            RegisterFunction(Constants.STR_TRIM, new StringManipulationFunction(StringManipulationFunction.Mode.TRIM));
            RegisterFunction(Constants.STR_UPPER, new StringManipulationFunction(StringManipulationFunction.Mode.UPPER));
            RegisterFunction(Constants.THREAD, new ThreadFunction());
            RegisterFunction(Constants.THREAD_ID, new ThreadIDFunction());
            RegisterFunction(Constants.THREAD_RESULT, new ThreadFunction(false, true));
            RegisterFunction(Constants.TOKENIZE, new TokenizeFunction());
            RegisterFunction(Constants.TOKENIZE_LINES, new TokenizeLinesFunction());
            RegisterFunction(Constants.TOKEN_COUNTER, new TokenCounterFunction());
            RegisterFunction(Constants.TO_BYTEARRAY, new ToByteArrayFunction());
            RegisterFunction(Constants.TO_BOOL, new ToBoolFunction());
            RegisterFunction(Constants.TO_DECIMAL, new ToDecimalFunction());
            RegisterFunction(Constants.TO_DOUBLE, new ToDoubleFunction());
            RegisterFunction(Constants.TO_INT, new ToIntFunction());
            //RegisterFunction(Constants.TO_INTEGER, new ToIntFunction());
            RegisterFunction(Constants.TO_NUMBER, new ToDoubleFunction());
            RegisterFunction(Constants.TO_STRING, new ToStringFunction());
            RegisterFunction(Constants.VAR, new VarFunction());
            RegisterFunction(Constants.WAIT, new SignalWaitFunction(false));
            RegisterFunction(Constants.WEB_REQUEST, new WebRequestFunction());

            RegisterFunction(Constants.ADD_DATA, new DataFunction(DataFunction.DataMode.ADD));
            RegisterFunction(Constants.COLLECT_DATA, new DataFunction(DataFunction.DataMode.SUBSCRIBE));
            RegisterFunction(Constants.GET_DATA, new DataFunction(DataFunction.DataMode.SEND));

            RegisterFunction(Constants.CONSOLE_LOG, new PrintFunction());


            RegisterFunction(Constants.OBJECT_DEFPROP, new ObjectPropsFunction());
        }

        public void RegisterEnums()
        {
            RegisterEnum(Constants.VARIABLE_TYPE, "SplitAndMerge.Variable.VarType");
        }

        public void RegisterActions()
        {
            AddAction(Constants.ASSIGNMENT, new AssignFunction());
            AddAction(Constants.INCREMENT, new IncrementDecrementFunction());
            AddAction(Constants.DECREMENT, new IncrementDecrementFunction());

            for (int i = 0; i < Constants.OPER_ACTIONS.Length; i++)
            {
                AddAction(Constants.OPER_ACTIONS[i], new OperatorAssignFunction());
            }
        }

        public Variable ProcessFile(string filename, bool mainFile = false)
        {
            string script = Utils.GetFileContents(filename);
            return Process(script, filename, mainFile);
        }

        public async Task<Variable> ProcessFileAsync(string filename, bool mainFile = false, object context = null)
        {
            string script = Utils.GetFileContents(filename);
            Variable result = await ProcessAsync(script, filename, mainFile, context);
            return result;
        }

        public Variable Process(string script, string filename = "", bool mainFile = false, object context = null)
        {
            Dictionary<int, int> char2Line;
            string data = Utils.ConvertToScript(this, script, out char2Line, filename);
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            ParsingScript toParse = new ParsingScript(this, data, 0, char2Line);
            toParse.OriginalScript = script;
            toParse.Filename = filename;
            toParse.Context = context;

            //var tokens = new HashSet<string>() { "function", "dllfunction", "define" };
            //var first = Utils.GetSubscript(toParse, tokens);

            if (mainFile)
            {
                toParse.MainFilename = toParse.Filename;
            }

            Variable result = null;

            // Functions leave their locals on the stack when an exception runs through them
            // -- whoever catches it trims the stack back, as a CSCS "catch" does. An exception
            // leaving here has no catcher left inside the interpreter, so this is where the
            // dead levels go: kept, the next script ran on top of them, and its own top-level
            // assignments went into a finished function's locals instead of the globals.
            int stackLevel = GetCurrentStackLevel();
            try
            {
                while (toParse.Pointer < data.Length)
                {
                    result = toParse.Execute();
                    if (result.Type == Variable.VarType.QUIT)
                    {
                        return result;
                    }
                    toParse.GoToNextStatement();
                }
            }
            catch
            {
                InvalidateStacksAfterLevel(stackLevel);
                throw;
            }

            return result;
        }
        public async Task<Variable> ProcessAsync(string script, string filename = "", bool mainFile = false, object context = null)
        {
            Dictionary<int, int> char2Line;
            string data = Utils.ConvertToScript(this, script, out char2Line, filename);
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            ParsingScript toParse = new ParsingScript(this, data, 0, char2Line);
            toParse.OriginalScript = script;
            toParse.Filename = filename;
            toParse.Context = context;

            if (mainFile)
            {
                toParse.MainFilename = toParse.Filename;
            }

            Variable result = null;

            // As in Process: an exception leaving here takes the dead levels with it.
            int stackLevel = GetCurrentStackLevel();
            try
            {
                while (toParse.Pointer < data.Length)
                {
                    result = await toParse.ExecuteAsync();
                    if (result.Type == Variable.VarType.QUIT)
                    {
                        return result;
                    }
                    toParse.GoToNextStatement();
                }
            }
            catch
            {
                InvalidateStacksAfterLevel(stackLevel);
                throw;
            }

            return result;
        }

        internal Variable ProcessFor(ParsingScript script)
        {
            Variable result = Variable.EmptyInstance;
            string forString = Utils.GetBodyBetween(script, Constants.START_ARG, Constants.END_ARG);
            script.Forward();
            if (forString.Contains(Constants.END_STATEMENT.ToString()))
            {
                // Looks like: "for(i = 0; i < 10; i++)".
                result = ProcessCanonicalFor(script, forString);
            }
            else
            {
                // Otherwise looks like: "for(item : array)"
                result = ProcessArrayFor(script, forString);
            }

            return result.IsReturn ? result : Variable.EmptyInstance;
        }
        internal async Task<Variable> ProcessForAsync(ParsingScript script)
        {
            Variable result = Variable.EmptyInstance;
            string forString = Utils.GetBodyBetween(script, Constants.START_ARG, Constants.END_ARG);
            script.Forward();
            if (forString.Contains(Constants.END_STATEMENT.ToString()))
            {
                // Looks like: "for(i = 0; i < 10; i++)".
                result = await ProcessCanonicalForAsync(script, forString);
            }
            else
            {
                // Otherwise looks like: "for(item : array)"
                result = await ProcessArrayForAsync(script, forString);
            }

            return result.IsReturn ? result : Variable.EmptyInstance;
        }

        Variable ProcessArrayFor(ParsingScript script, string forString)
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
            Variable result = Variable.EmptyInstance;
            Variable arrayValue = Utils.GetItem(forScript);

            int startForCondition = script.Pointer;

            if ((arrayValue.Type == Variable.VarType.OBJECT) && (arrayValue.Object is IEnumerable ienum))
            {
                foreach (object item in ienum)
                {
                    // We're using IEnumerable, which is typeless.
                    // But it may be important to make the correct type.
                    // TODO: Determine what type this is supposed to be (not sure how right now)
                    // and set the type in the Variable
                    Variable current = new Variable(item);

                    script.Pointer = startForCondition;
                    // With no script the binding has nowhere to go when a local of that name
                    // already exists: "k = \"z\"; for (k in keys)" left k as "z" for every
                    // pass, so the body read the value from before the loop. An assignment
                    // passes the script for the same reason.
                    AddGlobalOrLocalVariable(varName, new GetVarFunction(current), script);
                    result = ProcessBlock(script);
                    if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                    {
                        break;
                    }
                }
            }
            else
            {
                if (arrayValue.Type == Variable.VarType.STRING)
                {
                    arrayValue = new Variable(new List<string>(arrayValue.ToString().ToCharArray().Select(c => c.ToString())));
                }

                int cycles = arrayValue.Count;

                for (int i = 0; i < cycles; i++)
                {
                    Variable current = arrayValue.GetValue(i);

                    script.Pointer = startForCondition;
                    // With no script the binding has nowhere to go when a local of that name
                    // already exists: "k = \"z\"; for (k in keys)" left k as "z" for every
                    // pass, so the body read the value from before the loop. An assignment
                    // passes the script for the same reason.
                    AddGlobalOrLocalVariable(varName, new GetVarFunction(current), script);
                    result = ProcessBlock(script);
                    if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                    {
                        break;
                    }
                }
            }
            script.Pointer = startForCondition;
            SkipBlock(script);
            return result.IsReturn ? result : Variable.EmptyInstance;
        }

        async Task<Variable> ProcessArrayForAsync(ParsingScript script, string forString)
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
            Variable result = Variable.EmptyInstance;
            Variable arrayValue = await Utils.GetItemAsync(forScript);

            int startForCondition = script.Pointer;
            if ((arrayValue.Type == Variable.VarType.OBJECT) && (arrayValue.Object is IEnumerable ienum))
            {
                foreach (object item in ienum)
                {
                    // We're using IEnumerable, which is typeless.
                    // But it may be important to make the correct type.
                    // TODO: Determine what type this is supposed to be (not sure how right now)
                    // and set the type in the Variable
                    Variable current = new Variable(item);
                    script.Pointer = startForCondition;
                    // With no script the binding has nowhere to go when a local of that name
                    // already exists: "k = \"z\"; for (k in keys)" left k as "z" for every
                    // pass, so the body read the value from before the loop. An assignment
                    // passes the script for the same reason.
                    AddGlobalOrLocalVariable(varName, new GetVarFunction(current), script);
                    result = ProcessBlock(script);
                    if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                    {
                        break;
                    }
                }
            }
            else
            {
                if (arrayValue.Type == Variable.VarType.STRING)
                {
                    arrayValue = new Variable(new List<string>(arrayValue.ToString().ToCharArray().Select(c => c.ToString())));
                }

                int cycles = arrayValue.Count;

                for (int i = 0; i < cycles; i++)
                {
                    Variable current = arrayValue.GetValue(i);

                    script.Pointer = startForCondition;
                    // With no script the binding has nowhere to go when a local of that name
                    // already exists: "k = \"z\"; for (k in keys)" left k as "z" for every
                    // pass, so the body read the value from before the loop. An assignment
                    // passes the script for the same reason.
                    AddGlobalOrLocalVariable(varName, new GetVarFunction(current), script);
                    result = await ProcessBlockAsync(script);
                    if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                    {
                        break;
                    }
                }
            }
            script.Pointer = startForCondition;
            SkipBlock(script);
            return result.IsReturn ? result : Variable.EmptyInstance;
        }

        Variable ProcessCanonicalFor(ParsingScript script, string forString)
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
            Variable result = Variable.EmptyInstance;
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
                result = ProcessBlock(script);
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
            return result.IsReturn ? result : Variable.EmptyInstance;
        }
        async Task<Variable> ProcessCanonicalForAsync(ParsingScript script, string forString)
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
            Variable result = Variable.EmptyInstance;

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
                result = await ProcessBlockAsync(script);
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
            return result.IsReturn ? result : Variable.EmptyInstance;
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

                var block = ProcessBlock(script);
                if (block == null)
                {
                    // Nothing to run means this is not a do-loop body -- something handed the
                    // statement a fragment. Saying so beats dereferencing null, and beats
                    // treating it as an empty iteration and looping on a script that never
                    // advances.
                    throw new ArgumentException("Couldn't parse the body of a do-loop");
                }

                result = block;
                bool stop = result.IsReturn || result.Type == Variable.VarType.BREAK;
                if (stop || result.Type == Variable.VarType.CONTINUE)
                {
                    // break, return and continue all make ProcessBlock stop part-way, leaving
                    // the pointer inside the body, so rewind and skip the block to get past
                    // it -- the same thing a while loop does. Without this a continue left
                    // the rest of the body to be read as the loop condition, which then ran
                    // it a second time.
                    script.Pointer = BlockStartBefore(script, startDoCondition);
                    SkipBlock(script);
                }

                // Always consume the trailing "while (...)", even on the way out: a do-loop
                // has to test its condition after a continue, and leaving the keyword behind
                // makes the interpreter run into it and execute it as a fresh while statement.
                Variable condResult = ConsumeDoWhileCondition(script);
                stillValid = !stop && Convert.ToBoolean(condResult.Value);
            }

            // Unlike a while loop, whose block is still ahead of the pointer when the
            // condition fails, both the body and the trailing "while (...)" have been
            // consumed by now, so there is nothing left to skip. Calling SkipBlock here made
            // every "do {...} while (...);" followed by another statement fail with
            // "Couldn't skip block".
            return result.IsReturn ? result : Variable.EmptyInstance;
        }

        /// <summary>
        /// The offset of the "{" that opens the do-loop body. The pointer the "do" token
        /// leaves behind is already inside the block, and SkipBlock counts from the opening
        /// brace -- started one character too late it stopped at the first nested "}" and
        /// left the loop's own "}" for the parser to trip over.
        /// </summary>
        static int BlockStartBefore(ParsingScript script, int inside)
        {
            int i = inside - 1;
            while (i >= 0 && char.IsWhiteSpace(script.At(i)))
            {
                i--;
            }
            return i >= 0 && script.At(i) == Constants.START_GROUP ? i : inside;
        }

        /// <summary>
        /// Steps over the "while" that closes a do-loop and evaluates its condition. The
        /// keyword is matched rather than skipped by a fixed character count: the layout
        /// between "}" and "while" is up to whoever wrote the script.
        /// </summary>
        Variable ConsumeDoWhileCondition(ParsingScript script)
        {
            while (script.StillValid() && char.IsWhiteSpace(script.Current))
            {
                script.Forward();
            }
            if (script.Rest.StartsWith(Constants.WHILE, StringComparison.OrdinalIgnoreCase))
            {
                script.Forward(Constants.WHILE.Length);
            }
            return script.Execute(Constants.END_ARG_ARRAY);
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
            int switchStart = script.Pointer;
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
                        if (LeavesSwitch(result) || script.Prev == '}')
                        {
                            break;
                        }
                        script.Forward();
                    }
                }
            }
            return ExitSwitch(script, switchStart, result);
        }

        /// <summary>Whether the clause ended the switch rather than running to its end.</summary>
        static bool LeavesSwitch(Variable result)
        {
            return result.IsReturn ||
                   result.Type == Variable.VarType.BREAK ||
                   result.Type == Variable.VarType.CONTINUE;
        }

        /// <summary>
        /// Ends the switch, whichever clause ran and however it finished. A clause that
        /// stopped in its middle -- or a "default", which the scan does not read to its end --
        /// leaves the pointer nowhere near the closing brace, so it is moved past the whole
        /// switch here, exactly as ProcessIf does. Without that the enclosing block carried on
        /// parsing from inside the switch: a loop holding one ran a single pass.
        ///
        /// "break" ends the switch and nothing else, the way C# and JavaScript read it, so it
        /// is not handed upwards. "return" and "continue" belong to the function and to the
        /// enclosing loop, so those do travel outwards.
        /// </summary>
        Variable ExitSwitch(ParsingScript script, int switchStart, Variable result)
        {
            script.Pointer = switchStart;
            SkipBlock(script);
            // Only the separator after the switch is skipped: whitespace and an optional ";".
            // Not GoToNextStatement, which also consumes a "}" -- in
            // "for (...) { switch (...) { ... } }" that swallowed the loop's own closing brace
            // and the loop ran a single pass. Leaving the pointer on the "}" lets the
            // enclosing block end it, and leaving it before the next statement lets the block
            // read that statement rather than merging it into the switch's own expression.
            while (script.StillValid() &&
                   (char.IsWhiteSpace(script.Current) || script.Current == Constants.END_STATEMENT))
            {
                script.Forward();
            }
            // Only control flow leaves the switch. A clause's own last value must not: a
            // switch is a statement, and letting the value out merged it into whatever
            // followed -- "case 0: t += 1; } t += 100;" tried to make one expression of 1 and
            // 100. "break" is swallowed here as well, so it ends the switch and nothing else.
            return result.IsReturn || result.Type == Variable.VarType.CONTINUE ?
                   result : Variable.EmptyInstance;
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

        internal Variable ProcessIff(ParsingScript script)
        {
            Variable result = script.Execute(Constants.NEXT_ARG_ARRAY);
            bool isTrue = Convert.ToBoolean(result.Value);
            script.MoveForwardIf(Constants.NEXT_ARG);

            if (isTrue)
            {
                result = script.Execute(Constants.NEXT_ARG_ARRAY);
                SkipExpression(script);
            }
            else
            {
                SkipExpression(script);
                result = script.Execute(Constants.END_ARG_ARRAY);
            }

            return result;
        }
        internal async Task<Variable> ProcessIffAsync(ParsingScript script)
        {
            Variable result = await script.ExecuteAsync(Constants.NEXT_ARG_ARRAY);
            bool isTrue = Convert.ToBoolean(result.Value);
            script.MoveForwardIf(Constants.NEXT_ARG);

            if (isTrue)
            {
                result = await script.ExecuteAsync(Constants.NEXT_ARG_ARRAY);
                SkipExpression(script);
            }
            else
            {
                SkipExpression(script);
                result = await script.ExecuteAsync(Constants.END_ARG_ARRAY);
            }

            return result;
        }

        internal Variable ProcessTry(ParsingScript script)
        {
            int startTryCondition = script.Pointer - 1;
            int currentStackLevel = GetCurrentStackLevel();
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
                InvalidateStacksAfterLevel(currentStackLevel);

                GetVarFunction excMsgFunc = new GetVarFunction(new Variable(exception.Message));
                // With no script the binding has nowhere to go when a local of that name
                // already exists, so "e = \"pre\"; try { throw \"boom\"; } catch (e)" caught the
                // exception and then read "pre". A "for (x in ...)" had the same fault.
                AddGlobalOrLocalVariable(exceptionName, excMsgFunc, script);
                GetVarFunction excStackFunc = new GetVarFunction(new Variable(excStack));
                AddGlobalOrLocalVariable(exceptionName + ".Stack", excStackFunc, script);

                result = ProcessBlock(script);
                PopLocalVariable(exceptionName);
            }
            else
            {
                SkipBlock(script);
            }

            SkipRestBlocks(script);
            var pos = script.Pointer;
            var fin = Utils.GetNextToken(script);
            if (string.Compare(fin, Constants.FINALLY, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = ProcessBlock(script);
            }
            else
            {
                script.Pointer = pos;
            }
            // Empty unless the block left with a return, a break or a continue, which is how
            // ProcessIf and ProcessWhile end too: the value is what tells the parser the
            // statement is over. Handing back the block's last value -- a number, for
            // "try { x = 1; }" -- left the parser inside the same expression, so the next
            // statement was merged into it and an assignment after a try/catch failed with
            // "Can't process last token". A ";" right after the "}" hid it.
            return result != null && (result.IsReturn ||
                   result.Type == Variable.VarType.BREAK ||
                   result.Type == Variable.VarType.CONTINUE) ? result : Variable.EmptyInstance;
        }
        internal async Task<Variable> ProcessTryAsync(ParsingScript script)
        {
            int startTryCondition = script.Pointer - 1;
            int currentStackLevel = GetCurrentStackLevel();
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
                InvalidateStacksAfterLevel(currentStackLevel);

                GetVarFunction excMsgFunc = new GetVarFunction(new Variable(exception.Message));
                // With no script the binding has nowhere to go when a local of that name
                // already exists, so "e = \"pre\"; try { throw \"boom\"; } catch (e)" caught the
                // exception and then read "pre". A "for (x in ...)" had the same fault.
                AddGlobalOrLocalVariable(exceptionName, excMsgFunc, script);
                GetVarFunction excStackFunc = new GetVarFunction(new Variable(excStack));
                AddGlobalOrLocalVariable(exceptionName + ".Stack", excStackFunc, script);

                result = await ProcessBlockAsync(script);
                PopLocalVariable(exceptionName);
            }
            else
            {
                SkipBlock(script);
            }

            SkipRestBlocks(script);
            var pos = script.Pointer;
            var fin = Utils.GetNextToken(script);
            if (string.Compare(fin, Constants.FINALLY, StringComparison.OrdinalIgnoreCase) == 0)
            {
                result = await ProcessBlockAsync(script);
            }
            else
            {
                script.Pointer = pos;
            }
            // Empty unless the block left with a return, a break or a continue, which is how
            // ProcessIf and ProcessWhile end too: the value is what tells the parser the
            // statement is over. Handing back the block's last value -- a number, for
            // "try { x = 1; }" -- left the parser inside the same expression, so the next
            // statement was merged into it and an assignment after a try/catch failed with
            // "Can't process last token". A ";" right after the "}" hid it.
            return result != null && (result.IsReturn ||
                   result.Type == Variable.VarType.BREAK ||
                   result.Type == Variable.VarType.CONTINUE) ? result : Variable.EmptyInstance;
        }

        private string CreateExceptionStack(string exceptionName, int lowestStackLevel)
        {
            string result = "";
            Stack<ParserFunction.StackLevel> stack = ExecutionStack;
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

        public string GetStack(int lowestStackLevel = 0)
        {
            string result = "";
            Stack<ParserFunction.StackLevel> stack = ExecutionStack;
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

        internal Variable ProcessBlock(ParsingScript script)
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

        internal async Task<Variable> ProcessBlockAsync(ParsingScript script)
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

        internal void SkipBlock(ParsingScript script)
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

        // Skips an expression up to a , or a closing ). Does not let {}.
        internal void SkipExpression(ParsingScript script)
        {
            int blockStart = script.Pointer;
            int startParenthesis = 0;
            int endParenthesis = 0;
            bool inQuotes = false;
            bool inQuotes1 = false;
            bool inQuotes2 = false;
            char previous = Constants.EMPTY;
            char prevprev = Constants.EMPTY;
            bool done = false;
            script.MoveForwardIf(Constants.NEXT_ARG);

            while (!done)
            {
                if (!script.StillValid())
                {
                    throw new ArgumentException("Couldn't skip expression [" +
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
                    case Constants.START_ARG:
                        if (!inQuotes)
                        {
                            startParenthesis++;
                            done = startParenthesis < endParenthesis;
                        }
                        break;
                    case Constants.END_ARG:
                        if (!inQuotes)
                        {
                            endParenthesis++;
                            done = startParenthesis < endParenthesis;
                        }
                        break;
                    case Constants.NEXT_ARG:
                        if (!inQuotes)
                        {
                            done = startParenthesis <= endParenthesis;
                        }
                        break;
                    case Constants.START_GROUP:
                    case Constants.END_GROUP:
                        if (!inQuotes)
                        {
                            throw new ArgumentException("Not sllowed to have curly braces in this block [" +
                            script.Substr(blockStart, Constants.MAX_CHARS_TO_SHOW) + "]");
                        }
                        break;
                }
                prevprev = previous;
                previous = currentChar;
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

        public Variable Run(string functionName, Variable arg1 = null, Variable arg2 = null, Variable arg3 = null, ParsingScript script = null)
        {
            Task<Variable> task;
            try
            {
                task = CustomFunction.Run(this, functionName, arg1, arg2, arg3, script);
            }
            catch (Exception exc)
            {
                task = CustomFunction.Run(this, Constants.ON_EXCEPTION, new Variable(functionName),
                                          new Variable(exc.Message), arg2, script);
                if (task == null)
                {
                    throw;
                }
            }
            return task == null ? Variable.EmptyInstance : task.Result;
        }


        public Variable Run(string functionName, List<Variable> args, ParsingScript script = null)
        {
            Task<Variable> task = null;
            try
            {
                task = CustomFunction.Run(this, functionName, args, script);
            }
            catch (Exception exc)
            {
                task = CustomFunction.Run(this, Constants.ON_EXCEPTION, new Variable(functionName),
                                          new Variable(exc.Message), args.Count > 0 ? args[0] : Variable.EmptyInstance, script);
                if (task == null)
                {
                    throw;
                }
            }
            return task == null ? Variable.EmptyInstance : task.Result;
        }

        public Variable Run(CustomFunction function, List<Variable> args, ParsingScript script = null)
        {
            Variable result = null;
            try
            {
                result = function.Run(args, script);
            }
            catch (Exception exc)
            {
                var task = CustomFunction.Run(this, Constants.ON_EXCEPTION, new Variable(function.Name),
                                          new Variable(exc.Message), args.Count > 0 ? args[0] : Variable.EmptyInstance, script);
                if (task == null)
                {
                    throw;
                }
                result = task.Result;
            }
            return result;
        }

        public void RunScript(string fileName = "start.cscs")
        {
            string script = FileToString(fileName);
            Variable result = null;
            try
            {
                result = Process(script, fileName);
            }
            catch (Exception exc)
            {
                Console.WriteLine("Exception: " + exc.Message);
                Console.WriteLine(exc.StackTrace);
                InvalidateStacksAfterLevel(0);
                throw;
            }
        }

        public static string FileToString(string filename)
        {
            string contents = "";
            string[] lines = System.IO.File.ReadAllLines(filename);
            contents = string.Join("\n", lines);
            return contents;
        }

        #endregion

        #region Code that was moved from ParserFunction

        // These were all static in ParserFunction. Now they are
        // member variables and code in Interpreter

        // Global functions:
        Dictionary<string, ParserFunction> s_functions = new Dictionary<string, ParserFunction>();

        // Global variables:
        Dictionary<string, ParserFunction> s_variables = new Dictionary<string, ParserFunction>();

        // Global actions to functions map:
        Dictionary<string, ActionFunction> s_actions = new Dictionary<string, ActionFunction>();

        // Local scope variables:
        Dictionary<string, Dictionary<string, ParserFunction>> s_localScope =
           new Dictionary<string, Dictionary<string, ParserFunction>>();


        Stack<StackLevel> s_locals = new Stack<StackLevel>();
        public Stack<StackLevel> ExecutionStack { get { return s_locals; } }

        StackLevel s_lastExecutionLevel;

        Dictionary<string, StackLevel> s_namespaces = new Dictionary<string, StackLevel>();
        string s_namespace;
        string s_namespacePrefix;

        public string GetCurrentNamespace { get { return s_namespace; } }

        public Dictionary<string, CSCSClass> s_allClasses = new Dictionary<string, CSCSClass>();

        private DataFunctionData _dataFunctionData;
        public DataFunctionData DataFunctionData
        {
            get
            {
                if (_dataFunctionData == null)
                    _dataFunctionData = new DataFunctionData(this);
                return _dataFunctionData;
            }
        }


        public ParserFunction GetFromNamespace(string name)
        {
            ParserFunction result = GetFromNamespace(name, s_namespace);
            return result;
        }

        public ParserFunction GetFromNamespace(string name, string nameSpace)
        {
            if (string.IsNullOrWhiteSpace(nameSpace))
            {
                return null;
            }

            int ind = nameSpace.IndexOf('.');
            string prop = "";
            if (ind >= 0)
            {
                prop = name;
                name = nameSpace.Substring(ind + 1);
                nameSpace = nameSpace.Substring(0, ind);
            }

            StackLevel level;
            if (!s_namespaces.TryGetValue(nameSpace, out level))
            {
                return null;
            }

            if (!name.StartsWith(nameSpace, StringComparison.OrdinalIgnoreCase))
            {
                name = nameSpace + "." + name;
            }

            var vars = level.Variables;
            ParserFunction impl;
            if (!vars.TryGetValue(name, out impl) &&
                !s_variables.TryGetValue(name, out impl) &&
                !s_functions.TryGetValue(name, out impl)
                )
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(prop) && impl is GetVarFunction getVarFunction)
            {
                getVarFunction.PropertyName = prop;
            }
            return impl;
        }

        public bool TryAddToNamespace(string name, string nameSpace, Variable varValue)
        {
            StackLevel level;
            if (string.IsNullOrWhiteSpace(nameSpace) ||
               !s_namespaces.TryGetValue(nameSpace, out level))
            {
                return false;
            }

            var vars = level.Variables;
            vars[name] = new GetVarFunction(varValue);

            return true;
        }

        public ParserFunction GetVariable(string name, ParsingScript script = null, bool force = false)
        {
            if (!force && script != null && script.TryPrev() == Constants.START_ARG)
            {
                return GetFunction(name);
            }
            name = Constants.ConvertName(name);
            ParserFunction impl;
            StackLevel localStack = script?.StackLevel != null ? script.StackLevel : script != null && script.Compiled ?
                s_lastExecutionLevel : null;
            // script may legitimately be null -- PointerReferenceFunction.GetRefValue passes
            // null -- and the line above already allows for that. Without the same guard
            // here, "5 & 3" (& is the reference operator in CSCS, not bitwise AND) died with
            // a bare NullReferenceException carrying no script or line information.
            if (localStack == null && script != null && !string.IsNullOrWhiteSpace(script.Namespace) &&
                s_namespaces.TryGetValue(script.Namespace, out StackLevel level))
            {
                localStack = level;
            }
            if (localStack != null)
            {
                Dictionary<string, ParserFunction> local = localStack.Variables;
                if (local.TryGetValue(name, out impl))
                {
                    return impl;
                }
            }

            string scopeName = script == null || script.Filename == null ? "" : script.Filename;
            impl = GetLocalScopeVariable(name, scopeName);
            if (impl != null)
            {
                return impl;
            }

            if (s_variables.TryGetValue(name, out impl))
            {
                return impl.NewInstance();
            }
            if (script != null && !string.IsNullOrWhiteSpace(script.Namespace))
            {
                var cand = script.Namespace + "." + name;
                if (s_variables.TryGetValue(cand, out impl))
                {
                    return impl.NewInstance();
                }
                if (localStack != null && localStack.Variables.TryGetValue(cand, out impl))
                {
                    return impl.NewInstance();
                }
            }

            return GetFunction(name);
        }

        public Variable GetVariableValue(string name, ParsingScript script = null)
        {
            name = Constants.ConvertName(name);
            ParserFunction impl = null;
            StackLevel localStack = script?.StackLevel != null ?
                 script.StackLevel : s_locals.Count > StackLevelDelta ? s_lastExecutionLevel : null;
            if (localStack != null && localStack.Variables.TryGetValue(name, out impl) &&
                impl is GetVarFunction)
            {
                return (impl as GetVarFunction).Value;
            }

            string scopeName = script == null || script.Filename == null ? "" : script.Filename;
            impl = GetLocalScopeVariable(name, scopeName);
            if (impl == null && s_variables.TryGetValue(name, out impl))
            {
                impl = impl.NewInstance();
            }

            if (impl != null && impl is GetVarFunction)
            {
                return (impl as GetVarFunction).Value;
            }

            return null;
        }

        public ParserFunction GetFunction(string name)
        {
            name = Constants.ConvertName(name);
            ParserFunction impl;

            if (s_functions.TryGetValue(name, out impl))
            {
                // Global function exists and is registered (e.g. pi, exp, or a variable)
                return impl.NewInstance();
            }

            return GetFromNamespace(name);
        }

        public void UpdateFunction(Variable variable)
        {
            UpdateFunction(variable.ParsingToken, new GetVarFunction(variable));
        }
        public void UpdateFunction(string name, ParserFunction function)
        {
            name = Constants.ConvertName(name);
            Utils.CheckLegalName(name);
            lock (s_variables)
            {
                // First search among local variables.
                if (s_lastExecutionLevel != null && s_locals.Count > StackLevelDelta)
                {
                    Dictionary<string, ParserFunction> local = s_lastExecutionLevel.Variables;

                    if (local.ContainsKey(name))
                    {
                        // Local function exists (a local variable)
                        local[name] = function;
                        return;
                    }
                }
            }
            // If it's not a local variable, update global.
            s_variables[name] = function;
        }
        public ActionFunction GetAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return null;
            }

            ActionFunction impl;
            if (s_actions.TryGetValue(action, out impl))
            {
                // Action exists and is registered (e.g. =, +=, --, etc.)
                return impl;
            }

            return null;
        }

        public bool FunctionExists(string item)
        {
            // If it is not defined locally, then check globally:
            return LocalNameExists(item) || GlobalNameExists(item);
        }

        public void AddGlobalOrLocalVariable(string name, GetVarFunction function,
            ParsingScript script = null, bool localIfPossible = false)
        {
            name = Constants.ConvertName(name);
            Utils.CheckLegalName(name, script);

            bool shouldbeLocal = script?.StackLevel != null && script?.ParentScript != null;
            bool globalOnly = !localIfPossible && !LocalNameExists(name) && !shouldbeLocal;
            Dictionary<string, ParserFunction> lastLevel = GetLastLevel();
            if (!globalOnly && lastLevel != null && s_lastExecutionLevel.IsNamespace && !string.IsNullOrWhiteSpace(s_namespace))
            {
                name = s_namespacePrefix + name;
            }
            else if (!string.IsNullOrWhiteSpace(script?.Namespace))
            {
                name = script.Namespace + "." + name;
            }

            function.Name = Constants.GetRealName(name);
            function.Value.ParamName = function.Name;

            if (!globalOnly && !localIfPossible && script?.StackLevel != null && !GlobalNameExists(name))
            {
                script.StackLevel.Variables[name] = function;
            }

            if (!globalOnly && s_locals.Count > StackLevelDelta && shouldbeLocal &&
               (localIfPossible || LocalNameExists(name) || !GlobalNameExists(name)))
            {
                AddLocalVariable(function, script);
            }
            else
            {
                AddGlobal(name, function, false /* not native */);
                var val = function is GetVarFunction ? (function as GetVarFunction).Value.AsInt() : -1;
            }
        }

        string CreateVariableEntry(Variable var, string name, bool isLocal = false)
        {
            try
            {
                string value = var.AsString(true, true, 16);
                string localGlobal = isLocal ? "0" : "1";
                string varData = name + ":" + localGlobal + ":" +
                                 Constants.TypeToString(var.Type).ToLower() + ":" + value;
                return varData.Trim();
            }
            catch (Exception exc)
            {
                // TODO: Clean up not used objects.
                bool removed = isLocal ? PopLocalVariable(name) : RemoveGlobal(name);
                Console.WriteLine("Object {0} is probably dead ({1}): {2}. Removing it.", name, removed, exc);
                return null;
            }
        }

        void GetVariables(Dictionary<string, ParserFunction> variablesScope,
                                 StringBuilder sb, bool isLocal = false)
        {
            var all = variablesScope.Values.ToList();
            for (int i = 0; i < all.Count; i++)
            {
                var variable = all[i];
                GetVarFunction gvf = variable as GetVarFunction;
                if (gvf == null || string.IsNullOrWhiteSpace(variable.Name))
                {
                    continue;
                }

                string varData = CreateVariableEntry(gvf.Value, variable.Name, isLocal);
                if (!string.IsNullOrWhiteSpace(varData))
                {
                    sb.AppendLine(varData);
                    if (gvf.Value.Type == Variable.VarType.OBJECT)
                    {
                        var props = gvf.Value.GetProperties();
                        foreach (Variable var in props)
                        {
                            var val = gvf.Value.GetProperty(var.AsString());
                            varData = CreateVariableEntry(val, variable.Name + "." + var.AsString(), isLocal);
                            if (!string.IsNullOrWhiteSpace(varData))
                            {
                                sb.AppendLine(varData);
                            }
                        }
                    }
                }
            }
        }

        public string GetVariables(ParsingScript script)
        {
            StringBuilder sb = new StringBuilder();
            // Locals, if any:
            if (s_lastExecutionLevel != null)
            {
                Dictionary<string, ParserFunction> locals = s_lastExecutionLevel.Variables;
                GetVariables(locals, sb, true);
            }

            // Variables in the local file scope:
            if (script != null && script.Filename != null)
            {
                Dictionary<string, ParserFunction> localScope;
                string scopeName = Path.GetFileName(script.Filename);
                if (s_localScope.TryGetValue(scopeName, out localScope))
                {
                    GetVariables(localScope, sb, true);
                }
            }

            // Globals:
            GetVariables(s_variables, sb, false);

            return sb.ToString().Trim();
        }

        Dictionary<string, ParserFunction> GetLastLevel()
        {
            lock (s_variables)
            {
                if (s_lastExecutionLevel == null || s_locals.Count <= StackLevelDelta)
                {
                    return null;
                }
                var result = s_lastExecutionLevel.Variables;
                return result;
            }
        }

        public bool LocalNameExists(string name)
        {
            Dictionary<string, ParserFunction> lastLevel = GetLastLevel();
            if (lastLevel == null)
            {
                return false;
            }
            name = Constants.ConvertName(name);
            return lastLevel.ContainsKey(name);
        }

        public bool GlobalNameExists(string name)
        {
            name = Constants.ConvertName(name);
            return s_variables.ContainsKey(name) || s_functions.ContainsKey(name);
        }

        public Variable RegisterEnum(string varName, string enumName)
        {
            Variable enumVar = EnumFunction.UseExistingEnum(enumName);
            if (enumVar.Type == Variable.VarType.NONE)
            {
                return enumVar;
            }

            RegisterFunction(varName, new GetVarFunction(enumVar));
            return enumVar;
        }

        /// <summary>The names of every registered function, as the parser looks them up.</summary>
        public IList<string> FunctionNames => s_functions.Keys.ToList();

        /// <summary>
        /// Removes every registered function the predicate does not keep, and returns how many went.
        /// For a host that runs untrusted scripts: an allowlist applied after all registration is
        /// done, so a function added to the language later stays out until someone lets it in.
        /// Names are compared as the parser stores them (Constants.ConvertName).
        /// </summary>
        public int RestrictFunctions(Func<string, ParserFunction, bool> keep)
        {
            var removed = 0;
            foreach (var name in s_functions.Keys.ToList())
            {
                if (!keep(name, s_functions[name]))
                {
                    s_functions.Remove(name);
                    removed++;
                }
            }
            return removed;
        }

        public void RegisterFunction(string name, ParserFunction function,
                                            bool isNative = true)
        {
            function.InterpreterInstance = this;

            name = Constants.ConvertName(name);
            if (s_functions.TryGetValue(name, out ParserFunction old))
            {
                var msg = "Warning: Overriding function [" + old.Name + "].";
                System.Diagnostics.Debug.WriteLine(msg);
                AppendOutput(msg, true);
            }
            function.Name = Constants.GetRealName(name);

            if (!string.IsNullOrWhiteSpace(s_namespace))
            {
                StackLevel level;
                if (s_namespaces.TryGetValue(s_namespace, out level) &&
                   function is CustomFunction)
                {
                    ((CustomFunction)function).NamespaceData = level;
                    name = s_namespacePrefix + name;
                }
            }

            s_functions[name] = function;
            function.isNative = isNative;
        }

        public string GetDefinedFunctions()
        {
            StringBuilder sb = new StringBuilder();
            var keys = s_functions.Keys.ToList();
            keys.Sort();
            foreach (var key in keys)
            {
                var func = s_functions[key];
                if (func.isNative)
                {
                    sb.AppendLine(key + ": " + func.Description());
                }
            }

            return sb.ToString();
        }

        public bool UnregisterFunction(string name)
        {
            name = Constants.ConvertName(name);

            bool removed = s_functions.Remove(name);
            return removed;
        }

        public bool RemoveGlobal(string name)
        {
            name = Constants.ConvertName(name);
            return s_variables.Remove(name);
        }

        void NormalizeValue(ParserFunction function)
        {
            GetVarFunction gvf = function as GetVarFunction;
            if (gvf != null)
            {
                gvf.Value.CurrentAssign = "";
            }
        }

        void AddVariables(List<Variable> vars, Dictionary<string, ParserFunction> dict)
        {
            foreach (var val in dict.Values)
            {
                if (val.isNative || !(val is GetVarFunction))
                {
                    continue;
                }
                Variable var = ((GetVarFunction)val).Value.DeepClone();
                var.ParamName = ((GetVarFunction)val).Name;
                vars.Add(var);
            }
        }

        public List<Variable> VariablesSnaphot(ParsingScript script = null, bool includeGlobals = false)
        {
            List<Variable> vars = new List<Variable>();
            if (includeGlobals)
            {
                AddVariables(vars, s_variables);
            }
            Dictionary<string, ParserFunction> lastLevel = GetLastLevel();
            if (lastLevel != null)
            {
                AddVariables(vars, lastLevel);
            }
            if (script?.StackLevel != null)
            {
                AddVariables(vars, script.StackLevel.Variables);
            }
            return vars;
        }

        public void AddGlobal(string name, ParserFunction function,
                                     bool isNative = true)
        {
            function.InterpreterInstance = this;
            Utils.CheckLegalName(name);
            name = Constants.ConvertName(name);
            NormalizeValue(function);
            function.isNative = isNative;

            var handle = OnVariableChange;
            bool exists = handle != null && s_variables.ContainsKey(name);
            s_variables[name] = function;

            function.Name = Constants.GetRealName(name);
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
            if (!isNative)
            {
                //Translation.AddTempKeyword(name);
            }
#endif
            if (handle != null && function is GetVarFunction)
            {
                handle.Invoke(function.Name, ((GetVarFunction)function).Value, exists);
            }
        }

        public void AddLocalScopeVariable(string name, string scopeName, ParserFunction variable)
        {
            variable.InterpreterInstance = this;

            name = Constants.ConvertName(name);
            variable.isNative = false;
            variable.Name = Constants.GetRealName(name);
            if (variable is GetVarFunction)
            {
                ((GetVarFunction)variable).Value.ParamName = variable.Name;
            }

            if (scopeName == null)
            {
                scopeName = "";
            }

            Dictionary<string, ParserFunction> localScope;
            if (!s_localScope.TryGetValue(scopeName, out localScope))
            {
                localScope = new Dictionary<string, ParserFunction>();
            }
            localScope[name] = variable;
            s_localScope[scopeName] = localScope;
        }

        ParserFunction GetLocalScopeVariable(string name, string scopeName)
        {
            scopeName = Path.GetFileName(scopeName);
            Dictionary<string, ParserFunction> localScope;
            if (!s_localScope.TryGetValue(scopeName, out localScope))
            {
                return null;
            }

            name = Constants.ConvertName(name);
            ParserFunction function = null;
            localScope.TryGetValue(name, out function);
            return function;
        }

        public void AddAction(string name, ActionFunction action)
        {
            action.InterpreterInstance = this;
            s_actions[name] = action;
        }

        public void AddLocalVariables(StackLevel locals)
        {
            lock (s_variables)
            {
                if (InterpreterSecurity.MaxCallDepth > 0 && s_locals.Count >= InterpreterSecurity.MaxCallDepth)
                {
                    throw new ArgumentException("Recursion too deep: more than " +
                        InterpreterSecurity.MaxCallDepth + " nested function calls.");
                }
                s_locals.Push(locals);
                s_lastExecutionLevel = locals;
            }
        }

        public void AddNamespace(string namespaceName)
        {
            namespaceName = Constants.ConvertName(namespaceName);
            if (!string.IsNullOrWhiteSpace(s_namespace))
            {
                throw new ArgumentException("Already inside of namespace [" + s_namespace + "].");
            }

            StackLevel level;
            if (!s_namespaces.TryGetValue(namespaceName, out level))
            {
                level = new StackLevel(namespaceName, true); ;
            }

            lock (s_variables)
            {
                s_locals.Push(level);
                s_lastExecutionLevel = level;
            }

            s_namespaces[namespaceName] = level;

            s_namespace = namespaceName;
            s_namespacePrefix = namespaceName + ".";
        }

        public void PopNamespace()
        {
            s_namespace = s_namespacePrefix = "";
            lock (s_variables)
            {
                while (s_locals.Count > 0)
                {
                    var level = s_locals.Pop();
                    s_lastExecutionLevel = s_locals.Count == 0 ? null : s_locals.Peek();
                    if (level.IsNamespace)
                    {
                        return;
                    }
                }
            }
        }

        public string AdjustWithNamespace(string name)
        {
            name = Constants.ConvertName(name);
            return s_namespacePrefix + name;
        }

        public StackLevel AddStackLevel(string scopeName)
        {
            lock (s_variables)
            {
                s_locals.Push(new StackLevel(scopeName));
                s_lastExecutionLevel = s_locals.Peek();
                return s_lastExecutionLevel;
            }
        }

        public void AddLocalVariable(ParserFunction local, ParsingScript script, string varName = "")
        {
            local.InterpreterInstance = this;

            NormalizeValue(local);
            local.m_isGlobal = false;
            var handle = OnVariableChange;
            bool exists = false;

            lock (s_variables)
            {
                if (s_lastExecutionLevel == null)
                {
                    s_lastExecutionLevel = new StackLevel();
                    s_locals.Push(s_lastExecutionLevel);
                }

                var name = Constants.ConvertName(string.IsNullOrWhiteSpace(varName) ? local.Name : varName);
                local.Name = Constants.GetRealName(name);
                if (local is GetVarFunction getVarFunction)
                {
                    getVarFunction.Value.ParamName = local.Name;
                }

                exists = handle != null && s_lastExecutionLevel.Variables.ContainsKey(name);

                s_lastExecutionLevel.Variables[name] = local;
                if (script?.StackLevel != null)
                {
                    script.StackLevel.Variables[name] = local;
                }
            }
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
            //Translation.AddTempKeyword(name);
#endif
            if (handle != null && local is GetVarFunction localFunction)
            {
                handle.Invoke(local.Name, localFunction.Value, exists);
            }
        }

        /// <summary>
        /// Writes back a local of a compiled function. An existing global of that name is what
        /// the interpreter itself writes when a function assigns to one, so that still goes to
        /// the global; anything else goes into the function's own level, where the callbacks
        /// of the compiled body can see it and where it disappears when the call returns.
        /// AddGlobalOrLocalVariable, called with no script as the generated code calls it,
        /// treated every one of them as a global: a compiled function's locals were published
        /// for good, and one sharing a name with a global overwrote it.
        /// </summary>
        public void AddCompiledLocalVariable(string name, GetVarFunction function)
        {
            name = Constants.ConvertName(name);
            if (GlobalNameExists(name) || s_lastExecutionLevel == null ||
                s_locals.Count <= ParserFunction.StackLevelDelta)
            {
                AddGlobal(name, function, false /* not native */);
                return;
            }
            AddLocalVariable(function, null, name);
        }

        /// <summary>
        /// The same, for a name the script never assigned: a loop or catch variable, published
        /// only so that the callbacks of a compiled body can resolve it. It stays in the
        /// function's own level even when a global of that name exists -- a counter written
        /// there left the global holding the value the counter had inside the loop, not the
        /// one the loop exited on.
        /// </summary>
        public void AddCompiledLocalOnlyVariable(string name, GetVarFunction function)
        {
            name = Constants.ConvertName(name);
            if (s_lastExecutionLevel == null || s_locals.Count <= ParserFunction.StackLevelDelta)
            {
                AddGlobal(name, function, false /* not native */);
                return;
            }
            AddLocalVariable(function, null, name);
        }

        public bool RemoveVariable(string name)
        {
            name = Constants.ConvertName(name);
            var result = RemoveGlobal(name);
            if (result)
            {
                return true;
            }
            result = s_lastExecutionLevel!= null && s_lastExecutionLevel.Variables.Remove(name);
            if (result)
            {
                return true;
            }

            return UnregisterFunction(name);
        }

        public void PopLocalVariables(int id)
        {
            lock (s_variables)
            {
                if (s_lastExecutionLevel == null)
                {
                    return;
                }
                if (id < 0 || s_lastExecutionLevel.Id == id)
                {
                    s_locals.Pop();
                    s_lastExecutionLevel = s_locals.Count == 0 ? null : s_locals.Peek();
                    return;
                }

                var array = s_locals.ToArray();
                for (int i = 1; i < array.Length; i++)
                {
                    var stack = array[i];
                    if (stack.Id == id)
                    {
                        for (int j = 0; j < i + 1 && s_locals.Count > 0; j++)
                        {
                            s_locals.Pop();
                        }
                        for (int j = 0; j < i; j++)
                        {
                            s_locals.Push(array[j]);
                        }
                        s_lastExecutionLevel = s_locals.Peek();
                        return;
                    }
                }
            }
        }

        public int GetCurrentStackLevel()
        {
            lock (s_variables)
            {
                return s_locals.Count;
            }
        }

        public void InvalidateStacksAfterLevel(int level)
        {
            lock (s_variables)
            {
                while (level >= 0 && s_locals.Count > level)
                {
                    s_locals.Pop();
                }
                s_lastExecutionLevel = s_locals.Count == 0 ? null : s_locals.Peek();
            }
        }

        public bool PopLocalVariable(string name)
        {
            if (s_lastExecutionLevel == null)
            {
                return false;
            }
            Dictionary<string, ParserFunction> locals = s_lastExecutionLevel.Variables;
            name = Constants.ConvertName(name);
            return locals.Remove(name);
        }

        public ParserFunction GetObjectFunction(string name, ParsingScript script)
        {
            if (script.CurrentClass != null && script.CurrentClass.Name == name)
            {
                script.Backward(name.Length + 1);
                return new FunctionCreator();
            }
            if (script.ClassInstance != null &&
               (script.ClassInstance.PropertyExists(name) || script.ClassInstance.FunctionExists(name)))
            {
                // Through "this", which is bound to the instance the method runs on, rather
                // than through the instance's name: that name is the one the script assigned
                // the instance to, and an instance built inside a function is a local there,
                // invisible from here -- so a method calling a sibling method of its own class
                // failed with "Sequence contains no elements" whenever the instance had been
                // created inside a function. A field read never went this way, which is why
                // only the calls broke.
                name = Constants.THIS + "." + name;
            }
            //int ind = name.LastIndexOf('.');
            int ind = name.IndexOf('.');
            if (ind <= 0)
            {
                return null;
            }
            string baseName = name.Substring(0, ind);
            if (s_namespaces.ContainsKey(baseName))
            {
                int ind2 = name.IndexOf('.', ind + 1);
                if (ind2 > 0)
                {
                    ind = ind2;
                    baseName = name.Substring(0, ind);
                }
            }

            string prop = name.Substring(ind + 1);

            ParserFunction pf = GetFromNamespace(prop, baseName);
            if (pf != null)
            {
                return pf;
            }

            pf = GetVariable(baseName, script, true);
            if (pf == null || !(pf is GetVarFunction))
            {
                pf = GetFunction(baseName);
                if (pf == null)
                {
                    pf = Utils.ExtractArrayElement(this, baseName, script);
                }
            }

            GetVarFunction varFunc = pf as GetVarFunction;
            if (varFunc == null)
            {
                return null;
            }

            varFunc.PropertyName = prop;
            return varFunc;
        }

        public ParserFunction GetArrayFunction(string name, ParsingScript script, string action)
        {
            int arrayStart = name.IndexOf(Constants.START_ARRAY);
            if (arrayStart < 0)
            {
                return null;
            }

            if (arrayStart == 0)
            {
                Variable arr = Utils.ProcessArrayMap(new ParsingScript(this, name));
                return new GetVarFunction(arr);
            }

            string arrayName = name;

            int delta = 0;
            List<Variable> arrayIndices = Utils.GetArrayIndices(script, arrayName, delta, (string arr, int del) => { arrayName = arr; delta = del; });

            if (arrayIndices.Count == 0)
            {
                return null;
            }

            ParserFunction pf = GetVariable(arrayName, script);
            GetVarFunction varFunc = pf as GetVarFunction;
            if (varFunc == null)
            {
                return null;
            }

            // we temporarily backtrack for the processing
            script.Backward(name.Length - arrayStart - 1);
            script.Backward(action != null ? action.Length : 0);
            // delta shows us how manxy chars we need to advance forward in GetVarFunction()
            delta -= arrayName.Length;
            delta += action != null ? action.Length : 0;

            varFunc.Indices = arrayIndices;
            varFunc.Delta = delta;
            return varFunc;
        }

        public static Variable TryExtractArray(Variable candidate, string data, ParsingScript script)
        {
            if (candidate.Type == Variable.VarType.ARRAY && script.Current == Constants.START_ARRAY)
            {
                GetVarFunction varFunc = new GetVarFunction(candidate);
                var varName = data + script.Rest;
                var delta = 0;
                var arrayName = "";
                var arrayIndices = Utils.GetArrayIndices(script, varName, delta,
                    (string arr, int del) => { arrayName = arr; delta = del; });
                delta -= data.Length;
                script.Forward(1);
                varFunc.Indices = arrayIndices;
                varFunc.Delta = delta;

                candidate = varFunc.GetValue(script);
            }
            return candidate;
        }

        static bool ActionForUndefined(string action)
        {
            return !string.IsNullOrWhiteSpace(action) && action.EndsWith("=") && action.Length > 1;
        }

        public ParserFunction GetRegisteredAction(string name, ParsingScript script, ref string action)
        {
            if (Constants.CheckReserved(name))
            {
                return null;
            }

            if (false && ActionForUndefined(action) && script.Rest.StartsWith(Constants.UNDEFINED))
            {
                IsUndefinedFunction undef = new IsUndefinedFunction(name, action);
                return undef;
            }

            ActionFunction actionFunction = GetAction(action);

            // If passed action exists and is registered we are done.
            if (actionFunction == null)
            {
                return null;
            }

            ActionFunction theAction = actionFunction.NewInstance() as ActionFunction;
            theAction.Name = name;
            theAction.Action = action;

            action = null;
            return theAction;
        }

        public void CleanUp()
        {
            s_functions.Clear();
            s_actions.Clear();
            s_allClasses.Clear();
            CleanUpVariables();
        }

        public void CleanUpVariables()
        {
            s_variables.Clear();
            s_locals.Clear();
            s_localScope.Clear();
            s_namespaces.Clear();
            s_namespace = s_namespacePrefix = "";
        }

        public bool IsNumericFunction(string paramName, ParsingScript script = null)
        {
            ParserFunction function = GetFunction(paramName);
            return function is INumericFunction;
        }

        public void RegisterClass(string className, CSCSClass obj)
        {
            obj.InterpreterInstance = this;
            obj.Namespace = GetCurrentNamespace;
            obj.OriginalName = className;

            if (!string.IsNullOrWhiteSpace(obj.Namespace))
            {
                className = obj.Namespace + "." + className;
            }

            className = Constants.ConvertName(className);
            obj.Name = className;
            s_allClasses[className] = obj;
        }

        public CSCSClass GetClass(string name)
        {
            string currNamespace = GetCurrentNamespace;
            if (!string.IsNullOrWhiteSpace(currNamespace))
            {
                bool namespacePresent = name.Contains(".");
                if (!namespacePresent)
                {
                    name = currNamespace + "." + name;
                }
            }

            CSCSClass theClass = null;
            s_allClasses.TryGetValue(name, out theClass);
            return theClass;
        }

        public static List<Variable> GetClassProperties(CSCSClass cscsClass)
        {
            var props = new List<Variable>();
            foreach (var prop in cscsClass.ClassProperties)
            {
                props.Add(new Variable(prop.Key));
            }

            return props;
        }

        #endregion
    }
}

