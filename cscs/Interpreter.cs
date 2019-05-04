using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
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

        public event EventHandler<OutputAvailableEventArgs> GetOutput;

        public void AppendOutput(string text, bool newLine = false)
        {
            EventHandler<OutputAvailableEventArgs> handler = GetOutput;
            if (handler != null)
            {
                OutputAvailableEventArgs args = new OutputAvailableEventArgs(text +
                                     (newLine ? Environment.NewLine : string.Empty));
                handler(this, args);
            }
        }

        public void Init()
        {
            ParserFunction.RegisterFunction(Constants.IF, new IfStatement());
            ParserFunction.RegisterFunction(Constants.WHILE, new WhileStatement());
            ParserFunction.RegisterFunction(Constants.FOR, new ForStatement());
            ParserFunction.RegisterFunction(Constants.BREAK, new BreakStatement());
            ParserFunction.RegisterFunction(Constants.CONTINUE, new ContinueStatement());
            ParserFunction.RegisterFunction(Constants.CLASS, new ClassCreator());
            ParserFunction.RegisterFunction(Constants.ENUM, new EnumFunction());
            ParserFunction.RegisterFunction(Constants.NEW, new NewObjectFunction());
            ParserFunction.RegisterFunction(Constants.RETURN, new ReturnStatement());
            ParserFunction.RegisterFunction(Constants.FUNCTION, new FunctionCreator());
            ParserFunction.RegisterFunction(Constants.GET_PROPERTIES, new GetPropertiesFunction());
            ParserFunction.RegisterFunction(Constants.GET_PROPERTY, new GetPropertyFunction());
            ParserFunction.RegisterFunction(Constants.INCLUDE, new IncludeFile());
            ParserFunction.RegisterFunction(Constants.SET_PROPERTY, new SetPropertyFunction());
            ParserFunction.RegisterFunction(Constants.TRY, new TryBlock());
            ParserFunction.RegisterFunction(Constants.THROW, new ThrowFunction());
            ParserFunction.RegisterFunction(Constants.TYPE, new TypeFunction());
            ParserFunction.RegisterFunction(Constants.TRUE, new BoolFunction(true));
            ParserFunction.RegisterFunction(Constants.FALSE, new BoolFunction(false));

            ParserFunction.RegisterFunction(Constants.ABS, new AbsFunction());
            ParserFunction.RegisterFunction(Constants.ACOS, new AcosFunction());
            ParserFunction.RegisterFunction(Constants.ADD, new AddFunction());
            ParserFunction.RegisterFunction(Constants.ADD_TO_HASH, new AddVariableToHashFunction());
            ParserFunction.RegisterFunction(Constants.ADD_ALL_TO_HASH, new AddVariablesToHashFunction());
            ParserFunction.RegisterFunction(Constants.ASIN, new AsinFunction());
            ParserFunction.RegisterFunction(Constants.CANCEL, new CancelFunction());
            ParserFunction.RegisterFunction(Constants.CANCEL_RUN, new ScheduleRunFunction(false));
            ParserFunction.RegisterFunction(Constants.CEIL, new CeilFunction());
            ParserFunction.RegisterFunction(Constants.CHECK_LOADER_MAIN, new CheckLoaderMainFunction());
            ParserFunction.RegisterFunction(Constants.CONTAINS, new ContainsFunction());
            ParserFunction.RegisterFunction(Constants.COS, new CosFunction());
            ParserFunction.RegisterFunction(Constants.DEEP_COPY, new DeepCopyFunction());
            ParserFunction.RegisterFunction(Constants.DEFINE_LOCAL, new DefineLocalFunction());
            ParserFunction.RegisterFunction(Constants.ENV, new GetEnvFunction());
            ParserFunction.RegisterFunction(Constants.EXP, new ExpFunction());
            ParserFunction.RegisterFunction(Constants.FIND_INDEX, new FindIndexFunction());
            ParserFunction.RegisterFunction(Constants.FLOOR, new FloorFunction());
            ParserFunction.RegisterFunction(Constants.GET_COLUMN, new GetColumnFunction());
            ParserFunction.RegisterFunction(Constants.GET_FILE_FROM_DEBUGGER, new GetFileFromDebugger());
            ParserFunction.RegisterFunction(Constants.GET_KEYS, new GetAllKeysFunction());
            ParserFunction.RegisterFunction(Constants.LOCK, new LockFunction());
            ParserFunction.RegisterFunction(Constants.LOG, new LogFunction());
            ParserFunction.RegisterFunction(Constants.NAMESPACE, new NamespaceFunction());
            ParserFunction.RegisterFunction(Constants.NAME_EXISTS, new NameExistsFunction());
            ParserFunction.RegisterFunction(Constants.NOW, new DateTimeFunction());
            ParserFunction.RegisterFunction(Constants.PI, new PiFunction());
            ParserFunction.RegisterFunction(Constants.POW, new PowFunction());
            ParserFunction.RegisterFunction(Constants.PRINT, new PrintFunction());
            ParserFunction.RegisterFunction(Constants.PSTIME, new ProcessorTimeFunction());
            ParserFunction.RegisterFunction(Constants.RANDOM, new GetRandomFunction());
            ParserFunction.RegisterFunction(Constants.REMOVE, new RemoveFunction());
            ParserFunction.RegisterFunction(Constants.REMOVE_AT, new RemoveAtFunction());
            ParserFunction.RegisterFunction(Constants.ROUND, new RoundFunction());
            ParserFunction.RegisterFunction(Constants.SCHEDULE_RUN, new ScheduleRunFunction(true));
            ParserFunction.RegisterFunction(Constants.SHOW, new ShowFunction());
            ParserFunction.RegisterFunction(Constants.SETENV, new SetEnvFunction());
            ParserFunction.RegisterFunction(Constants.SIGNAL, new SignalWaitFunction(true));
            ParserFunction.RegisterFunction(Constants.SIN, new SinFunction());
            ParserFunction.RegisterFunction(Constants.SINGLETON, new SingletonFunction());
            ParserFunction.RegisterFunction(Constants.SIZE, new SizeFunction());
            ParserFunction.RegisterFunction(Constants.SLEEP, new SleepFunction());
            ParserFunction.RegisterFunction(Constants.SQRT, new SqrtFunction());
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
            ParserFunction.RegisterFunction(Constants.TO_STRING, new ToStringFunction());
            ParserFunction.RegisterFunction(Constants.WAIT, new SignalWaitFunction(false));
            ParserFunction.RegisterFunction(Constants.WEB_REQUEST, new WebRequestFunction());

            ParserFunction.RegisterEnum(Constants.VARIABLE_TYPE, "SplitAndMerge.Variable.VarType");

            ParserFunction.AddAction(Constants.ASSIGNMENT, new AssignFunction());
            ParserFunction.AddAction(Constants.INCREMENT,  new IncrementDecrementFunction());
            ParserFunction.AddAction(Constants.DECREMENT,  new IncrementDecrementFunction());

            for (int i = 0; i < Constants.OPER_ACTIONS.Length; i++)
            {
                ParserFunction.AddAction(Constants.OPER_ACTIONS[i], new OperatorAssignFunction());
            }

            Constants.ELSE_LIST.Add(Constants.ELSE);
            Constants.ELSE_IF_LIST.Add(Constants.ELSE_IF);
            Constants.CATCH_LIST.Add(Constants.CATCH);

            InitStandalone();
            CompiledClass.Init();
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
                result = toParse.ExecuteTo();
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
                result = await toParse.ExecuteToAsync();
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
            int index = forString.IndexOf(Constants.FOR_EACH);
            if (index <= 0 || index == forString.Length - 1)
            {
                throw new ArgumentException("Expecting: for(item : array)");
            }

            string varName = forString.Substring(0, index);

            ParsingScript forScript = new ParsingScript(forString, 0, script.Char2Line);
            forScript.ParentScript = script;
            forScript.Filename = script.Filename;
            forScript.Debugger = script.Debugger;
            forScript.Pointer = index + 1;
            Variable arrayValue = Utils.GetItem(forScript);

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
            int index = forString.IndexOf(Constants.FOR_EACH);
            if (index <= 0 || index == forString.Length - 1)
            {
                throw new ArgumentException("Expecting: for(item : array)");
            }

            string varName = forString.Substring(0, index);

            ParsingScript forScript = new ParsingScript(forString, 0, script.Char2Line);
            forScript.ParentScript = script;
            forScript.Filename = script.Filename;
            forScript.Debugger = script.Debugger;
            forScript.Pointer = index + 1;
            Variable arrayValue = await Utils.GetItemAsync(forScript);

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

            ParsingScript initScript = new ParsingScript(forTokens[0] + Constants.END_STATEMENT);
            ParsingScript condScript = new ParsingScript(forTokens[1] + Constants.END_STATEMENT);
            ParsingScript loopScript = new ParsingScript(forTokens[2] + Constants.END_STATEMENT);

            initScript.ParentScript = script;
            condScript.ParentScript = script;
            loopScript.ParentScript = script;

            initScript.ExecuteFrom(0);

            int cycles = 0;
            bool stillValid = true;

            while (stillValid)
            {
                Variable condResult = condScript.ExecuteFrom(0);
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
                loopScript.ExecuteFrom(0);
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

            ParsingScript initScript = new ParsingScript(forTokens[0] + Constants.END_STATEMENT);
            ParsingScript condScript = new ParsingScript(forTokens[1] + Constants.END_STATEMENT);
            ParsingScript loopScript = new ParsingScript(forTokens[2] + Constants.END_STATEMENT);

            initScript.ParentScript = script;
            condScript.ParentScript = script;
            loopScript.ParentScript = script;

            await initScript.ExecuteFromAsync(0);

            int cycles = 0;
            bool stillValid = true;

            while (stillValid)
            {
                Variable condResult = await condScript.ExecuteFromAsync(0);
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
                await loopScript.ExecuteFromAsync(0);
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

            while (stillValid)
            {
                script.Pointer = startWhileCondition;

                //int startSkipOnBreakChar = from;
                Variable condResult = script.ExecuteTo(Constants.END_ARG);
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

                Variable result = ProcessBlock(script);
                if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                {
                    script.Pointer = startWhileCondition;
                    break;
                }
            }

            // The while condition is not true anymore: must skip the whole while
            // block before continuing with next statements.
            SkipBlock(script);
            return Variable.EmptyInstance;
        }
        internal async Task<Variable> ProcessWhileAsync(ParsingScript script)
        {
            int startWhileCondition = script.Pointer;

            // A check against an infinite loop.
            int cycles = 0;
            bool stillValid = true;

            while (stillValid)
            {
                script.Pointer = startWhileCondition;

                //int startSkipOnBreakChar = from;
                Variable condResult = await script.ExecuteToAsync(Constants.END_ARG);
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

                Variable result = await ProcessBlockAsync(script);
                if (result.IsReturn || result.Type == Variable.VarType.BREAK)
                {
                    script.Pointer = startWhileCondition;
                    break;
                }
            }

            // The while condition is not true anymore: must skip the whole while
            // block before continuing with next statements.
            SkipBlock(script);
            return Variable.EmptyInstance;
        }

        internal Variable ProcessIf(ParsingScript script)
        {
            int startIfCondition = script.Pointer;

            Variable result = script.ExecuteTo(Constants.END_ARG);
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

                return result;
                //return Variable.EmptyInstance;
            }

            // We are in Else. Skip everything in the If statement.
            SkipBlock(script);

            ParsingScript nextData = new ParsingScript(script);
            nextData.ParentScript = script;

            string nextToken = Utils.GetNextToken(nextData);

            if (Constants.ELSE_IF_LIST.Contains(nextToken))
            {
                script.Pointer = nextData.Pointer + 1;
                result = ProcessIf(script);
            }
            else if (Constants.ELSE_LIST.Contains(nextToken))
            {
                script.Pointer = nextData.Pointer + 1;
                result = ProcessBlock(script);
            }

            if (result.IsReturn)
            {
                return result;
            }
            return Variable.EmptyInstance;
        }
        internal async Task<Variable> ProcessIfAsync(ParsingScript script)
        {
            int startIfCondition = script.Pointer;

            Variable result = await script.ExecuteToAsync(Constants.END_ARG);
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

                return result;
                //return Variable.EmptyInstance;
            }

            // We are in Else. Skip everything in the If statement.
            SkipBlock(script);

            ParsingScript nextData = new ParsingScript(script);
            nextData.ParentScript = script;

            string nextToken = Utils.GetNextToken(nextData);

            if (Constants.ELSE_IF_LIST.Contains(nextToken))
            {
                script.Pointer = nextData.Pointer + 1;
                result = await ProcessIfAsync(script);
            }
            else if (Constants.ELSE_LIST.Contains(nextToken))
            {
                script.Pointer = nextData.Pointer + 1;
                result = await ProcessBlockAsync(script);
            }

            if (result.IsReturn)
            {
                return result;
            }
            return Variable.EmptyInstance;
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
            if (!Constants.CATCH_LIST.Contains(catchToken))
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
            if (!Constants.CATCH_LIST.Contains(catchToken))
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

                result = script.ExecuteTo();

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

                result = await script.ExecuteToAsync();

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
            bool inQuotes  = false;
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
                if (!Constants.ELSE_IF_LIST.Contains(nextToken) &&
                      !Constants.ELSE_LIST.Contains(nextToken))
                {
                    return;
                }
                script.Pointer = nextData.Pointer;
                SkipBlock(script);
            }
        }
    }
}
