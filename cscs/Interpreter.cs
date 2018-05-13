using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Text;


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

  public class Interpreter
  {

    private static Interpreter instance;

    private Interpreter()
    {
      Init();
    }

    public static Interpreter Instance {
      get {
        if (instance == null) {
          instance = new Interpreter();
        }
        return instance;
      }
    }

    private int MAX_LOOPS;

    private StringBuilder m_output = new StringBuilder();
    public string Output {
      get {
        string output = m_output.ToString().Trim();
        m_output.Clear();
        return output;
      }
    }

    public event EventHandler<OutputAvailableEventArgs> GetOutput;

    public void AppendOutput(string text, bool newLine = false)
    {
      EventHandler<OutputAvailableEventArgs> handler = GetOutput;
      if (handler != null) {
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
      ParserFunction.RegisterFunction(Constants.RETURN, new ReturnStatement());
      ParserFunction.RegisterFunction(Constants.FUNCTION, new FunctionCreator());
      ParserFunction.RegisterFunction(Constants.COMPILED_FUNCTION, new CompiledFunctionCreator());
      ParserFunction.RegisterFunction(Constants.INCLUDE, new IncludeFile());
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
      ParserFunction.RegisterFunction(Constants.APPEND, new AppendFunction());
      ParserFunction.RegisterFunction(Constants.APPENDLINE, new AppendLineFunction());
      ParserFunction.RegisterFunction(Constants.APPENDLINES, new AppendLinesFunction());
      ParserFunction.RegisterFunction(Constants.ASIN, new AsinFunction());
      ParserFunction.RegisterFunction(Constants.CD, new CdFunction());
      ParserFunction.RegisterFunction(Constants.CD__, new Cd__Function());
      ParserFunction.RegisterFunction(Constants.CEIL, new CeilFunction());
      ParserFunction.RegisterFunction(Constants.CONNECTSRV, new ClientSocket());
      ParserFunction.RegisterFunction(Constants.CONSOLE_CLR, new ClearConsole());
      ParserFunction.RegisterFunction(Constants.CONTAINS, new ContainsFunction());
      ParserFunction.RegisterFunction(Constants.COPY, new CopyFunction());
      ParserFunction.RegisterFunction(Constants.COS, new CosFunction());
      ParserFunction.RegisterFunction(Constants.DEEP_COPY, new DeepCopyFunction());
      ParserFunction.RegisterFunction(Constants.DELETE, new DeleteFunction());
      ParserFunction.RegisterFunction(Constants.DIR, new DirFunction());
      ParserFunction.RegisterFunction(Constants.ENV, new GetEnvFunction());
      ParserFunction.RegisterFunction(Constants.EXISTS, new ExistsFunction());
      ParserFunction.RegisterFunction(Constants.EXIT, new ExitFunction());
      ParserFunction.RegisterFunction(Constants.EXP, new ExpFunction());
      ParserFunction.RegisterFunction(Constants.FINDFILES, new FindfilesFunction());
      ParserFunction.RegisterFunction(Constants.FINDSTR, new FindstrFunction());
      ParserFunction.RegisterFunction(Constants.FLOOR, new FloorFunction());
      ParserFunction.RegisterFunction(Constants.GET_COLUMN, new GetColumnFunction());
      ParserFunction.RegisterFunction(Constants.GET_KEYS, new GetAllKeysFunction());
      ParserFunction.RegisterFunction(Constants.INDEX_OF, new IndexOfFunction());
      ParserFunction.RegisterFunction(Constants.KILL, new KillFunction());
      ParserFunction.RegisterFunction(Constants.LOCK, new LockFunction());
      ParserFunction.RegisterFunction(Constants.LOG, new LogFunction());
      ParserFunction.RegisterFunction(Constants.MKDIR, new MkdirFunction());
      ParserFunction.RegisterFunction(Constants.MORE, new MoreFunction());
      ParserFunction.RegisterFunction(Constants.MOVE, new MoveFunction());
      ParserFunction.RegisterFunction(Constants.NOW, new DateTimeFunction());
      ParserFunction.RegisterFunction(Constants.PI, new PiFunction());
      ParserFunction.RegisterFunction(Constants.POW, new PowFunction());
      ParserFunction.RegisterFunction(Constants.PRINT, new PrintFunction());
      ParserFunction.RegisterFunction(Constants.PRINT_BLACK, new PrintFunction(ConsoleColor.Black));
      ParserFunction.RegisterFunction(Constants.PRINT_GRAY, new PrintFunction(ConsoleColor.DarkGray));
      ParserFunction.RegisterFunction(Constants.PRINT_GREEN, new PrintFunction(ConsoleColor.Green));
      ParserFunction.RegisterFunction(Constants.PRINT_RED, new PrintFunction(ConsoleColor.Red));
      ParserFunction.RegisterFunction(Constants.PSINFO, new PsInfoFunction());
      ParserFunction.RegisterFunction(Constants.PSTIME, new ProcessorTimeFunction());
      ParserFunction.RegisterFunction(Constants.PWD, new PwdFunction());
      ParserFunction.RegisterFunction(Constants.RANDOM, new GetRandomFunction());
      ParserFunction.RegisterFunction(Constants.READ, new ReadConsole());
      ParserFunction.RegisterFunction(Constants.READFILE, new ReadCSCSFileFunction());
      ParserFunction.RegisterFunction(Constants.READNUMBER, new ReadConsole(true));
      ParserFunction.RegisterFunction(Constants.REMOVE, new RemoveFunction());
      ParserFunction.RegisterFunction(Constants.REMOVE_AT, new RemoveAtFunction());
      ParserFunction.RegisterFunction(Constants.ROUND, new RoundFunction());
      ParserFunction.RegisterFunction(Constants.RUN, new RunFunction());
      ParserFunction.RegisterFunction(Constants.SIGNAL, new SignalWaitFunction(true));
      ParserFunction.RegisterFunction(Constants.SETENV, new SetEnvFunction());
      ParserFunction.RegisterFunction(Constants.SHOW, new ShowFunction());
      ParserFunction.RegisterFunction(Constants.SIN, new SinFunction());
      ParserFunction.RegisterFunction(Constants.SIZE, new SizeFunction());
      ParserFunction.RegisterFunction(Constants.SLEEP, new SleepFunction());
      ParserFunction.RegisterFunction(Constants.SQRT, new SqrtFunction());
      ParserFunction.RegisterFunction(Constants.STARTSRV, new ServerSocket());
      ParserFunction.RegisterFunction(Constants.STOPWATCH_ELAPSED, new StopWatchFunction(StopWatchFunction.Mode.ELAPSED));
      ParserFunction.RegisterFunction(Constants.STOPWATCH_START, new StopWatchFunction(StopWatchFunction.Mode.START));
      ParserFunction.RegisterFunction(Constants.STOPWATCH_STOP, new StopWatchFunction(StopWatchFunction.Mode.STOP));
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
      ParserFunction.RegisterFunction(Constants.SUBSTR, new SubstrFunction());
      ParserFunction.RegisterFunction(Constants.TAIL, new TailFunction());
      ParserFunction.RegisterFunction(Constants.THREAD, new ThreadFunction());
      ParserFunction.RegisterFunction(Constants.THREAD_ID, new ThreadIDFunction());
      ParserFunction.RegisterFunction(Constants.TIMESTAMP, new TimestampFunction());
      ParserFunction.RegisterFunction(Constants.TOKENIZE, new TokenizeFunction());
      ParserFunction.RegisterFunction(Constants.TOKENIZE_LINES, new TokenizeLinesFunction());
      ParserFunction.RegisterFunction(Constants.TOKEN_COUNTER, new TokenCounterFunction());
      ParserFunction.RegisterFunction(Constants.TOLOWER, new ToLowerFunction());
      ParserFunction.RegisterFunction(Constants.TOUPPER, new ToUpperFunction());
      ParserFunction.RegisterFunction(Constants.TO_BOOL, new ToBoolFunction());
      ParserFunction.RegisterFunction(Constants.TO_DECIMAL, new ToDecimalFunction());
      ParserFunction.RegisterFunction(Constants.TO_DOUBLE, new ToDoubleFunction());
      ParserFunction.RegisterFunction(Constants.TO_INT, new ToIntFunction());
      ParserFunction.RegisterFunction(Constants.TO_STRING, new ToStringFunction());
      ParserFunction.RegisterFunction(Constants.TRANSLATE, new TranslateFunction());
      ParserFunction.RegisterFunction(Constants.WAIT, new SignalWaitFunction(false));
      ParserFunction.RegisterFunction(Constants.WRITE, new PrintFunction(false));
      ParserFunction.RegisterFunction(Constants.WRITELINE, new WriteLineFunction());
      ParserFunction.RegisterFunction(Constants.WRITELINES, new WriteLinesFunction());
      ParserFunction.RegisterFunction(Constants.WRITE_CONSOLE, new WriteToConsole());

      ParserFunction.AddAction(Constants.ASSIGNMENT, new AssignFunction());
      ParserFunction.AddAction(Constants.INCREMENT, new IncrementDecrementFunction());
      ParserFunction.AddAction(Constants.DECREMENT, new IncrementDecrementFunction());

      for (int i = 0; i < Constants.OPER_ACTIONS.Length; i++) {
        ParserFunction.AddAction(Constants.OPER_ACTIONS[i], new OperatorAssignFunction());
      }

      Constants.ELSE_LIST.Add(Constants.ELSE);
      Constants.ELSE_IF_LIST.Add(Constants.ELSE_IF);
      Constants.CATCH_LIST.Add(Constants.CATCH);

      ReadConfig();
    }

    private void ReadConfig()
    {
      MAX_LOOPS = ReadConfig("maxLoops", 256000);
#if !__MOBILE__ && __MOBILE__
      if (ConfigurationManager.GetSection("Languages") == null) {
        return;
      }
      var languagesSection = ConfigurationManager.GetSection("Languages") as NameValueCollection;
      if (languagesSection.Count == 0) {
        return;
      }

      string errorsPath = ConfigurationManager.AppSettings["errorsPath"];
      Translation.Language = ConfigurationManager.AppSettings["language"];
      Translation.LoadErrors(errorsPath);

      string dictPath = ConfigurationManager.AppSettings["dictionaryPath"];

      string baseLanguage = Constants.ENGLISH;
      string languages = languagesSection["languages"];
      string[] supportedLanguages = languages.Split(",".ToCharArray());

      foreach(string lang in supportedLanguages) {
        string language = Constants.Language(lang);
        Dictionary<string, string> tr1 = Translation.KeywordsDictionary(baseLanguage, language);
        Dictionary<string, string> tr2 = Translation.KeywordsDictionary(language, baseLanguage);

        Translation.TryLoadDictionary(dictPath, baseLanguage, language);

        var languageSection    = ConfigurationManager.GetSection(lang) as NameValueCollection;

        Translation.Add(languageSection, Constants.IF, tr1, tr2);
        Translation.Add(languageSection, Constants.FOR, tr1, tr2);
        Translation.Add(languageSection, Constants.WHILE, tr1, tr2);
        Translation.Add(languageSection, Constants.BREAK, tr1, tr2);
        Translation.Add(languageSection, Constants.CONTINUE, tr1, tr2);
        Translation.Add(languageSection, Constants.RETURN, tr1, tr2);
        Translation.Add(languageSection, Constants.FUNCTION, tr1, tr2);
        Translation.Add(languageSection, Constants.INCLUDE, tr1, tr2);
        Translation.Add(languageSection, Constants.THROW, tr1, tr2);
        Translation.Add(languageSection, Constants.TRY, tr1, tr2);
        Translation.Add(languageSection, Constants.TYPE, tr1, tr2);
        Translation.Add(languageSection, Constants.TRUE, tr1, tr2);
        Translation.Add(languageSection, Constants.FALSE, tr1, tr2);

        Translation.Add(languageSection, Constants.ADD, tr1, tr2);
        Translation.Add(languageSection, Constants.ADD_TO_HASH, tr1, tr2);
        Translation.Add(languageSection, Constants.ADD_ALL_TO_HASH, tr1, tr2);
        Translation.Add(languageSection, Constants.APPEND, tr1, tr2);
        Translation.Add(languageSection, Constants.APPENDLINE, tr1, tr2);
        Translation.Add(languageSection, Constants.APPENDLINES, tr1, tr2);
        Translation.Add(languageSection, Constants.CD, tr1, tr2);
        Translation.Add(languageSection, Constants.CD__, tr1, tr2);
        Translation.Add(languageSection, Constants.CEIL, tr1, tr2);
        Translation.Add(languageSection, Constants.CONSOLE_CLR, tr1, tr2);
        Translation.Add(languageSection, Constants.CONTAINS, tr1, tr2);
        Translation.Add(languageSection, Constants.COPY, tr1, tr2);
        Translation.Add(languageSection, Constants.DEEP_COPY, tr1, tr2);
        Translation.Add(languageSection, Constants.DELETE, tr1, tr2);
        Translation.Add(languageSection, Constants.DIR, tr1, tr2);
        Translation.Add(languageSection, Constants.ENV, tr1, tr2);
        Translation.Add(languageSection, Constants.EXIT, tr1, tr2);
        Translation.Add(languageSection, Constants.EXISTS, tr1, tr2);
        Translation.Add(languageSection, Constants.FINDFILES, tr1, tr2);
        Translation.Add(languageSection, Constants.FINDSTR, tr1, tr2);
        Translation.Add(languageSection, Constants.FLOOR, tr1, tr2);
        Translation.Add(languageSection, Constants.GET_COLUMN, tr1, tr2);
        Translation.Add(languageSection, Constants.GET_KEYS, tr1, tr2);
        Translation.Add(languageSection, Constants.INDEX_OF, tr1, tr2);
        Translation.Add(languageSection, Constants.KILL, tr1, tr2);
        Translation.Add(languageSection, Constants.LOCK, tr1, tr2);
        Translation.Add(languageSection, Constants.MKDIR, tr1, tr2);
        Translation.Add(languageSection, Constants.MORE, tr1, tr2);
        Translation.Add(languageSection, Constants.MOVE, tr1, tr2);
        Translation.Add(languageSection, Constants.NOW, tr1, tr2);
        Translation.Add(languageSection, Constants.PRINT, tr1, tr2);
        Translation.Add(languageSection, Constants.PRINT, tr1, tr2);
        Translation.Add(languageSection, Constants.PRINT_BLACK, tr1, tr2);
        Translation.Add(languageSection, Constants.PRINT_GRAY, tr1, tr2);
        Translation.Add(languageSection, Constants.PRINT_GREEN, tr1, tr2);
        Translation.Add(languageSection, Constants.PRINT_RED, tr1, tr2);
        Translation.Add(languageSection, Constants.PSINFO, tr1, tr2);
        Translation.Add(languageSection, Constants.PWD, tr1, tr2);
        Translation.Add(languageSection, Constants.RANDOM, tr1, tr2);
        Translation.Add(languageSection, Constants.READ, tr1, tr2);
        Translation.Add(languageSection, Constants.READFILE, tr1, tr2);
        Translation.Add(languageSection, Constants.READNUMBER, tr1, tr2);
        Translation.Add(languageSection, Constants.REMOVE, tr1, tr2);
        Translation.Add(languageSection, Constants.REMOVE_AT, tr1, tr2);
        Translation.Add(languageSection, Constants.ROUND, tr1, tr2);
        Translation.Add(languageSection, Constants.RUN, tr1, tr2);
        Translation.Add(languageSection, Constants.SET, tr1, tr2);
        Translation.Add(languageSection, Constants.SETENV, tr1, tr2);
        Translation.Add(languageSection, Constants.SHOW, tr1, tr2);
        Translation.Add(languageSection, Constants.SIGNAL, tr1, tr2);
        Translation.Add(languageSection, Constants.SIZE, tr1, tr2);
        Translation.Add(languageSection, Constants.SLEEP, tr1, tr2);
        Translation.Add(languageSection, Constants.STOPWATCH_ELAPSED, tr1, tr2);
        Translation.Add(languageSection, Constants.STOPWATCH_START, tr1, tr2);
        Translation.Add(languageSection, Constants.STOPWATCH_STOP, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_BETWEEN, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_BETWEEN_ANY, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_CONTAINS, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_ENDS_WITH, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_EQUALS, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_INDEX_OF, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_LOWER, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_REPLACE, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_STARTS_WITH, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_SUBSTR, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_TRIM, tr1, tr2);
        Translation.Add(languageSection, Constants.STR_UPPER, tr1, tr2);
        Translation.Add(languageSection, Constants.SUBSTR, tr1, tr2);
        Translation.Add(languageSection, Constants.TAIL, tr1, tr2);
        Translation.Add(languageSection, Constants.THREAD, tr1, tr2);
        Translation.Add(languageSection, Constants.THREAD_ID, tr1, tr2);
        Translation.Add(languageSection, Constants.TIMESTAMP, tr1, tr2);
        Translation.Add(languageSection, Constants.TOKENIZE, tr1, tr2);
        Translation.Add(languageSection, Constants.TOKENIZE_LINES, tr1, tr2);
        Translation.Add(languageSection, Constants.TOKEN_COUNTER, tr1, tr2);
        Translation.Add(languageSection, Constants.TOLOWER, tr1, tr2);
        Translation.Add(languageSection, Constants.TOUPPER, tr1, tr2);
        Translation.Add(languageSection, Constants.TO_BOOL, tr1, tr2);
        Translation.Add(languageSection, Constants.TO_DECIMAL, tr1, tr2);
        Translation.Add(languageSection, Constants.TO_DOUBLE, tr1, tr2);
        Translation.Add(languageSection, Constants.TO_INT, tr1, tr2);
        Translation.Add(languageSection, Constants.TO_STRING, tr1, tr2);
        Translation.Add(languageSection, Constants.TRANSLATE, tr1, tr2);
        Translation.Add(languageSection, Constants.WAIT, tr1, tr2);
        Translation.Add(languageSection, Constants.WRITE, tr1, tr2);
        Translation.Add(languageSection, Constants.WRITELINE, tr1, tr2);
        Translation.Add(languageSection, Constants.WRITELINES, tr1, tr2);
        Translation.Add(languageSection, Constants.WRITE_CONSOLE, tr1, tr2);

        // Special dealing for else, elif since they are not separate
        // functions but are part of the if statement block.
        // Same for and, or, not.
        Translation.AddSubstatement(languageSection, Constants.ELSE,    Constants.ELSE_LIST, tr1, tr2);
        Translation.AddSubstatement(languageSection, Constants.ELSE_IF, Constants.ELSE_IF_LIST, tr1, tr2);
        Translation.AddSubstatement(languageSection, Constants.CATCH,   Constants.CATCH_LIST, tr1, tr2);
      }
#endif
    }

    public int ReadConfig(string configName, int defaultValue = 0)
    {
      int value = defaultValue;
#if !__MOBILE__ && __MOBILE__
            string config = ConfigurationManager.AppSettings[configName];
            if (string.IsNullOrWhiteSpace(config) || !Int32.TryParse(config, out value))
            {
                return defaultValue;
            }
#endif
      return value;
    }

    public Variable Process(string script)
    {
      Dictionary<int, int> char2Line;
      string data = Utils.ConvertToScript(script, out char2Line);
      if (string.IsNullOrWhiteSpace(data)) {
        return null;
      }

      ParsingScript toParse = new ParsingScript(data, 0, char2Line);
      toParse.OriginalScript = script;

      Variable result = null;

      while (toParse.Pointer < data.Length) {
        result = toParse.ExecuteTo();
        toParse.GoToNextStatement();
      }

      return result;
    }

    internal Variable ProcessFor(ParsingScript script)
    {
      string forString = Utils.GetBodyBetween(script, Constants.START_ARG, Constants.END_ARG);
      script.Forward();
      if (forString.Contains(Constants.END_STATEMENT.ToString())) {
        // Looks like: "for(i = 0; i < 10; i++)".
        ProcessCanonicalFor(script, forString);
      } else {
        // Otherwise looks like: "for(item : array)"
        ProcessArrayFor(script, forString);
      }

      return Variable.EmptyInstance;
    }
    void ProcessArrayFor(ParsingScript script, string forString)
    {
      int index = forString.IndexOf(Constants.FOR_EACH);
      if (index <= 0 || index == forString.Length - 1) {
        throw new ArgumentException("Expecting: for(item : array)");
      }

      string varName = forString.Substring(0, index);

      ParsingScript forScript = new ParsingScript(forString);
      Variable arrayValue = forScript.ExecuteFrom(index + 1);

      int cycles = arrayValue.TotalElements();
      if (cycles == 0) {
        SkipBlock(script);
        return;
      }
      int startForCondition = script.Pointer;

      for (int i = 0; i < cycles; i++) {
        script.Pointer = startForCondition;
        Variable current = arrayValue.GetValue(i);
        ParserFunction.AddGlobalOrLocalVariable(varName,
                       new GetVarFunction(current));
        Variable result = ProcessBlock(script);
        if (result.IsReturn || result.Type == Variable.VarType.BREAK) {
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
      if (forTokens.Length != 3) {
        throw new ArgumentException("Expecting: for(init; condition; loopStatement)");
      }

      int startForCondition = script.Pointer;

      ParsingScript initScript = new ParsingScript(forTokens[0] + Constants.END_STATEMENT);
      ParsingScript condScript = new ParsingScript(forTokens[1] + Constants.END_STATEMENT);
      ParsingScript loopScript = new ParsingScript(forTokens[2] + Constants.END_STATEMENT);

      initScript.ExecuteFrom(0);

      int cycles = 0;
      bool stillValid = true;

      while (stillValid) {
        Variable condResult = condScript.ExecuteFrom(0);
        stillValid = Convert.ToBoolean(condResult.Value);
        if (!stillValid) {
          return;
        }

        if (MAX_LOOPS > 0 && ++cycles >= MAX_LOOPS) {
          throw new ArgumentException("Looks like an infinite loop after " +
                                        cycles + " cycles.");
        }

        script.Pointer = startForCondition;
        Variable result = ProcessBlock(script);
        if (result.IsReturn || result.Type == Variable.VarType.BREAK) {
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

    internal Variable ProcessWhile(ParsingScript script)
    {
      int startWhileCondition = script.Pointer;

      // A check against an infinite loop.
      int cycles = 0;
      bool stillValid = true;

      while (stillValid) {
        script.Pointer = startWhileCondition;

        //int startSkipOnBreakChar = from;
        Variable condResult = script.ExecuteTo(Constants.END_ARG);
        stillValid = Convert.ToBoolean(condResult.Value);
        if (!stillValid) {
          break;
        }

        // Check for an infinite loop if we are comparing same values:
        if (MAX_LOOPS > 0 && ++cycles >= MAX_LOOPS) {
          throw new ArgumentException("Looks like an infinite loop after " +
              cycles + " cycles.");
        }

        Variable result = ProcessBlock(script);
        if (result.IsReturn || result.Type == Variable.VarType.BREAK) {
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

      if (isTrue) {
        result = ProcessBlock(script);

        if (result.IsReturn ||
            result.Type == Variable.VarType.BREAK ||
            result.Type == Variable.VarType.CONTINUE) {
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

      string nextToken = Utils.GetNextToken(nextData);

      if (Constants.ELSE_IF_LIST.Contains(nextToken)) {
        script.Pointer = nextData.Pointer + 1;
        result = ProcessIf(script);
      } else if (Constants.ELSE_LIST.Contains(nextToken)) {
        script.Pointer = nextData.Pointer + 1;
        result = ProcessBlock(script);
      }

      if (result.IsReturn) {
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

      try {
        result = ProcessBlock(script);
      } catch (Exception exc) {
        exception = exc;
      }

      if (exception != null || result.IsReturn ||
          result.Type == Variable.VarType.BREAK ||
          result.Type == Variable.VarType.CONTINUE) {
        // We are here from the middle of the try-block either because
        // an exception was thrown or because of a Break/Continue. Skip it.
        script.Pointer = startTryCondition;
        SkipBlock(script);
      }

      string catchToken = Utils.GetNextToken(script);
      script.Forward(); // skip opening parenthesis
                        // The next token after the try block must be a catch.
      if (!Constants.CATCH_LIST.Contains(catchToken)) {
        throw new ArgumentException("Expecting a 'catch()' but got [" +
            catchToken + "]");
      }

      string exceptionName = Utils.GetNextToken(script);
      script.Forward(); // skip closing parenthesis

      if (exception != null) {
        string excStack = CreateExceptionStack(exceptionName, currentStackLevel);
        ParserFunction.InvalidateStacksAfterLevel(currentStackLevel);

        GetVarFunction excMsgFunc = new GetVarFunction(new Variable(exception.Message));
        ParserFunction.AddGlobalOrLocalVariable(exceptionName, excMsgFunc);
        GetVarFunction excStackFunc = new GetVarFunction(new Variable(excStack));
        ParserFunction.AddGlobalOrLocalVariable(exceptionName + ".Stack", excStackFunc);

        result = ProcessBlock(script);
        ParserFunction.PopLocalVariable(exceptionName);
      } else {
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
      foreach (ParserFunction.StackLevel stackLevel in stack) {
        if (level-- < lowestStackLevel) {
          break;
        }
        if (string.IsNullOrWhiteSpace(stackLevel.Name)) {
          continue;
        }
        result += Environment.NewLine + "  " + stackLevel.Name + "()";
      }

      if (!string.IsNullOrWhiteSpace(result)) {
        result = " --> " + exceptionName + result;
      }

      return result;
    }

    private Variable ProcessBlock(ParsingScript script)
    {
      int blockStart = script.Pointer;
      Variable result = null;

      while (script.StillValid()) {
        int endGroupRead = script.GoToNextStatement();
        if (endGroupRead > 0) {
          return result != null ? result : new Variable();
        }

        if (!script.StillValid()) {
          throw new ArgumentException("Couldn't process block [" +
          script.Substr(blockStart, Constants.MAX_CHARS_TO_SHOW) + "]");
        }
        result = script.ExecuteTo();

        if (result.IsReturn ||
            result.Type == Variable.VarType.BREAK ||
            result.Type == Variable.VarType.CONTINUE) {
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
      char previous = Constants.EMPTY;
      while (startCount == 0 || startCount > endCount) {
        if (!script.StillValid()) {
          throw new ArgumentException("Couldn't skip block [" +
          script.Substr(blockStart, Constants.MAX_CHARS_TO_SHOW) + "]");
        }
        char currentChar = script.CurrentAndForward();
        switch (currentChar) {
          case Constants.QUOTE: if (previous != '\\') inQuotes = !inQuotes; break;
          case Constants.START_GROUP: if (!inQuotes) startCount++; break;
          case Constants.END_GROUP: if (!inQuotes) endCount++; break;
        }
        previous = currentChar;
      }

      if (startCount != endCount) {
        throw new ArgumentException("Mismatched parentheses");
      }
    }

    private void SkipRestBlocks(ParsingScript script)
    {
      while (script.StillValid()) {
        int endOfToken = script.Pointer;
        ParsingScript nextData = new ParsingScript(script);
        string nextToken = Utils.GetNextToken(nextData);
        if (!Constants.ELSE_IF_LIST.Contains(nextToken) &&
              !Constants.ELSE_LIST.Contains(nextToken)) {
          return;
        }
        script.Pointer = nextData.Pointer;
        SkipBlock(script);
      }
    }
  }
}
