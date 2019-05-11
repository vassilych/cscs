using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public partial class Constants
    {
        public const string APPEND = "append";
        public const string APPENDLINE = "appendline";
        public const string APPENDLINES = "appendlines";
        public const string CALL_NATIVE = "CallNative";
        public const string CD = "cd";
        public const string CD__ = "cd..";
        public const string COPY = "copy";
        public const string CONNECTSRV = "connectsrv";
        public const string CONSOLE_CLR = "clr";
        public const string DIR = "dir";
        public const string DELETE = "delete";
        public const string EXISTS = "exists";
        public const string FINDFILES = "findfiles";
        public const string FINDSTR = "findstr";
        public const string GET_NATIVE = "GetNative";
        public const string JSON = "GetVariableFromJsonNewton";
        public const string KILL = "kill";
        public const string MKDIR = "mkdir";
        public const string MORE = "more";
        public const string MOVE = "move";
        public const string PRINT_BLACK = "printblack";
        public const string PRINT_GRAY = "printgray";
        public const string PRINT_GREEN = "printgreen";
        public const string PRINT_RED = "printred";
        public const string PSINFO = "psinfo";
        public const string PWD = "pwd";
        public const string READ = "read";
        public const string READFILE = "readfile";
        public const string READNUMBER = "readnum";
        public const string RUN = "run";
        public const string SET_NATIVE = "SetNative";
        public const string STARTSRV = "startsrv";
        public const string STOPWATCH_ELAPSED = "StopWatchElapsed";
        public const string STOPWATCH_START = "StartStopWatch";
        public const string STOPWATCH_STOP = "StopStopWatch";
        public const string TAIL = "tail";
        public const string TIMESTAMP = "Timestamp";
        public const string TRANSLATE = "translate";
        public const string WRITELINE = "writeline";
        public const string WRITELINES = "writelines";
        public const string WRITENL = "writenl";
        public const string WRITE_CONSOLE = "WriteConsole";
        public const string WRITE = "write";

        public const string ENGLISH = "en";
        public const string GERMAN = "de";
        public const string RUSSIAN = "ru";
        public const string SPANISH = "es";
        public const string SYNONYMS = "sy";

        public static string Language(string lang)
        {
            switch (lang)
            {
                case "English": return ENGLISH;
                case "German": return GERMAN;
                case "Russian": return RUSSIAN;
                case "Spanish": return SPANISH;
                case "Synonyms": return SYNONYMS;
                default: return ENGLISH;
            }
        }
    }
}
