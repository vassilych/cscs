using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;

namespace SplitAndMerge
{
    public partial class Interpreter
    {
        public void InitStandalone()
        {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false
            //ParserFunction.CleanUp();
            ParserFunction.RegisterFunction(Constants.APPEND, new AppendFunction());
            ParserFunction.RegisterFunction(Constants.APPENDLINE, new AppendLineFunction());
            ParserFunction.RegisterFunction(Constants.APPENDLINES, new AppendLinesFunction());
            ParserFunction.RegisterFunction(Constants.CALL_NATIVE, new InvokeNativeFunction());
            ParserFunction.RegisterFunction(Constants.CD, new CdFunction());
            ParserFunction.RegisterFunction(Constants.CD__, new Cd__Function());
            ParserFunction.RegisterFunction(Constants.CONNECTSRV, new ClientSocket());
            ParserFunction.RegisterFunction(Constants.COPY, new CopyFunction());
            ParserFunction.RegisterFunction(Constants.DELETE, new DeleteFunction());
            ParserFunction.RegisterFunction(Constants.DIR, new DirFunction());
            ParserFunction.RegisterFunction(Constants.EXISTS, new ExistsFunction());
            ParserFunction.RegisterFunction(Constants.EXIT, new ExitFunction());
            ParserFunction.RegisterFunction(Constants.FINDFILES, new FindfilesFunction());
            ParserFunction.RegisterFunction(Constants.FINDSTR, new FindstrFunction());
            ParserFunction.RegisterFunction(Constants.GET_NATIVE, new GetNativeFunction());
            ParserFunction.RegisterFunction(Constants.KILL, new KillFunction());
            ParserFunction.RegisterFunction(Constants.MKDIR, new MkdirFunction());
            ParserFunction.RegisterFunction(Constants.MORE, new MoreFunction());
            ParserFunction.RegisterFunction(Constants.MOVE, new MoveFunction());
            ParserFunction.RegisterFunction(Constants.PSINFO, new PsInfoFunction());
            ParserFunction.RegisterFunction(Constants.PWD, new PwdFunction());
            ParserFunction.RegisterFunction(Constants.READFILE, new ReadCSCSFileFunction());
            ParserFunction.RegisterFunction(Constants.RUN, new RunFunction());
            ParserFunction.RegisterFunction(Constants.SET_NATIVE, new SetNativeFunction());
            ParserFunction.RegisterFunction(Constants.STARTSRV, new ServerSocket());
            ParserFunction.RegisterFunction(Constants.STOPWATCH_ELAPSED, new StopWatchFunction(StopWatchFunction.Mode.ELAPSED));
            ParserFunction.RegisterFunction(Constants.STOPWATCH_START, new StopWatchFunction(StopWatchFunction.Mode.START));
            ParserFunction.RegisterFunction(Constants.STOPWATCH_STOP, new StopWatchFunction(StopWatchFunction.Mode.STOP));
            ParserFunction.RegisterFunction(Constants.TAIL, new TailFunction());
            ParserFunction.RegisterFunction(Constants.TIMESTAMP, new TimestampFunction());
            ParserFunction.RegisterFunction(Constants.WRITE, new PrintFunction(false));
            ParserFunction.RegisterFunction(Constants.WRITELINE, new WriteLineFunction());
            ParserFunction.RegisterFunction(Constants.WRITELINES, new WriteLinesFunction());
            ParserFunction.RegisterFunction(Constants.WRITE_CONSOLE, new WriteToConsole());

#if __ANDROID__ == false && __IOS__ == false
            ParserFunction.RegisterFunction(Constants.ADD_COMP_DEFINITION, new EditCompiledEntry(EditCompiledEntry.EditMode.ADD_DEFINITION));
            ParserFunction.RegisterFunction(Constants.ADD_COMP_NAMESPACE, new EditCompiledEntry(EditCompiledEntry.EditMode.ADD_NAMESPACE));
            ParserFunction.RegisterFunction(Constants.CLEAR_COMP_DEFINITIONS, new EditCompiledEntry(EditCompiledEntry.EditMode.CLEAR_DEFINITIONS));
            ParserFunction.RegisterFunction(Constants.CLEAR_COMP_NAMESPACES, new EditCompiledEntry(EditCompiledEntry.EditMode.CLEAR_NAMESPACES));
            ParserFunction.RegisterFunction(Constants.CSHARP_FUNCTION, new CompiledFunctionCreator(true));

            ParserFunction.RegisterFunction(Constants.CONSOLE_CLR, new ClearConsole());
            ParserFunction.RegisterFunction(Constants.PRINT_BLACK, new PrintColorFunction(ConsoleColor.Black));
            ParserFunction.RegisterFunction(Constants.PRINT_GRAY, new PrintColorFunction(ConsoleColor.DarkGray));
            ParserFunction.RegisterFunction(Constants.PRINT_GREEN, new PrintColorFunction(ConsoleColor.Green));
            ParserFunction.RegisterFunction(Constants.PRINT_RED, new PrintColorFunction(ConsoleColor.Red));
            ParserFunction.RegisterFunction(Constants.READ, new ReadConsole());
            ParserFunction.RegisterFunction(Constants.READNUMBER, new ReadConsole(true));
            ParserFunction.RegisterFunction(Constants.TRANSLATE, new TranslateFunction());

            ParserFunction.RegisterFunction(Constants.ENCODE_FILE, new EncodeFileFunction(true));
            ParserFunction.RegisterFunction(Constants.DECODE_FILE, new EncodeFileFunction(false));

            CSCS_SQL.Init();
#endif
#endif
            //ReadConfig();
        }               

        void ReadConfig()
        {
            MAX_LOOPS = ReadConfig("maxLoops", 256000);
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
            if (ConfigurationManager.GetSection("Languages") == null)
            {
                return;
            }
            var languagesSection = ConfigurationManager.GetSection("Languages") as NameValueCollection;
            if (languagesSection.Count == 0)
            {
                return;
            }

            string errorsPath = ConfigurationManager.AppSettings["errorsPath"];
            Translation.Language = ConfigurationManager.AppSettings["language"];
            Translation.LoadErrors(errorsPath);

            string dictPath = ConfigurationManager.AppSettings["dictionaryPath"];

            string baseLanguage = Constants.ENGLISH;
            string languages = languagesSection["languages"];
            string[] supportedLanguages = languages.Split(",".ToCharArray());

            foreach (string lang in supportedLanguages)
            {
                string language = Constants.Language(lang);
                Dictionary<string, string> tr1 = Translation.KeywordsDictionary(baseLanguage, language);
                Dictionary<string, string> tr2 = Translation.KeywordsDictionary(language, baseLanguage);

                Translation.TryLoadDictionary(dictPath, baseLanguage, language);

                var languageSection = ConfigurationManager.GetSection(lang) as NameValueCollection;

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
                Translation.Add(languageSection, Constants.TAIL, tr1, tr2);
                Translation.Add(languageSection, Constants.THREAD, tr1, tr2);
                Translation.Add(languageSection, Constants.THREAD_ID, tr1, tr2);
                Translation.Add(languageSection, Constants.TIMESTAMP, tr1, tr2);
                Translation.Add(languageSection, Constants.TOKENIZE, tr1, tr2);
                Translation.Add(languageSection, Constants.TOKENIZE_LINES, tr1, tr2);
                Translation.Add(languageSection, Constants.TOKEN_COUNTER, tr1, tr2);
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
            }
#endif
        }

        public int ReadConfig(string configName, int defaultValue = 0)
        {
            int value = defaultValue;
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
            string config = ConfigurationManager.AppSettings[configName];
            if (string.IsNullOrWhiteSpace(config) || !Int32.TryParse(config, out value))
            {
                return defaultValue;
            }
#endif
            return value;
        }
   }
}
