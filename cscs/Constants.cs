using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
  public class Constants
  {
    public const char START_ARG = '(';
    public const char END_ARG = ')';
    public const char START_ARRAY = '[';
    public const char END_ARRAY = ']';
    public const char END_LINE = '\n';
    public const char NEXT_ARG = ',';
    public const char QUOTE = '"';
    public const char SPACE = ' ';
    public const char START_GROUP = '{';
    public const char END_GROUP = '}';
    public const char VAR_START = '$';
    public const char END_STATEMENT = ';';
    public const char FOR_EACH = ':';
    public const char CONTINUE_LINE = '\\';
    public const char EMPTY = '\0';
    public const char TERNARY_OPERATOR  = '?';
 
    public const string ASSIGNMENT = "=";
    public const string AND = "&&";
    public const string OR = "||";
    public const string NOT = "!";
    public const string INCREMENT = "++";
    public const string DECREMENT = "--";
    public const string EQUAL = "==";
    public const string NOT_EQUAL = "!=";
    public const string LESS = "<";
    public const string LESS_EQ = "<=";
    public const string GREATER = ">";
    public const string GREATER_EQ = ">=";
    public const string ADD_ASSIGN = "+=";
    public const string SUBT_ASSIGN = "-=";
    public const string MULT_ASSIGN = "*=";
    public const string DIV_ASSIGN = "/=";

    public const string BREAK = "break";
    public const string CATCH = "catch";
    public const string COMMENT = "//";
    public const string CONTINUE = "continue";
    public const string ELSE = "else";
    public const string ELSE_IF = "elif";
    public const string FOR = "for";
    public const string FUNCTION = "function";
    public const string COMPILED_FUNCTION = "cfunction";
    public const string IF = "if";
    public const string INCLUDE = "include";
    public const string RETURN = "return";
    public const string THROW = "throw";
    public const string TRY = "try";
    public const string TYPE = "type";
    public const string WHILE = "while";

    public const string TRUE = "true";
    public const string FALSE = "false";

    public const string ABS = "abs";
    public const string ACOS = "acos";
    public const string ADD = "add";
    public const string ADD_TO_HASH = "AddToHash";
    public const string ADD_ALL_TO_HASH = "AddAllToHash";
    public const string APPEND = "append";
    public const string APPENDLINE = "appendline";
    public const string APPENDLINES = "appendlines";
    public const string ASIN = "asin";
    public const string CD = "cd";
    public const string CD__ = "cd..";
    public const string COPY = "copy";
    public const string CEIL = "ceil";
    public const string CONNECTSRV = "connectsrv";
    public const string CONTAINS = "contains";
    public const string CONSOLE_CLR = "clr";
    public const string COS = "cos";
    public const string DEEP_COPY = "DeepCopy";
    public const string DIR = "dir";
    public const string DELETE = "del";
    public const string ENV = "env";
    public const string EXISTS = "exists";
    public const string EXIT = "exit";
    public const string EXP = "exp";
    public const string FINDFILES = "findfiles";
    public const string FINDSTR = "findstr";
    public const string FLOOR = "floor";
    public const string GET_COLUMN = "getcolumn";
    public const string GET_KEYS = "getkeys";
    public const string INDEX_OF = "indexof";
    public const string KILL = "kill";
    public const string LOCK = "lock";
    public const string LOG = "log";
    public const string MKDIR = "mkdir";
    public const string MORE = "more";
    public const string MOVE = "move";
    public const string NOW = "Now";
    public const string PI = "pi";
    public const string POW = "pow";
    public const string PRINT = "print";
    public const string PRINT_BLACK = "printblack";
    public const string PRINT_GRAY = "printgray";
    public const string PRINT_GREEN = "printgreen";
    public const string PRINT_RED = "printred";
    public const string PSINFO = "psinfo";
    public const string PSTIME = "pstime";
    public const string PWD = "pwd";
    public const string RANDOM = "GetRandom";
    public const string READ = "read";
    public const string READFILE = "readfile";
    public const string READNUMBER = "readnum";
    public const string REMOVE = "RemoveItem";
    public const string REMOVE_AT = "RemoveAt";
    public const string ROUND = "round";
    public const string RUN = "run";
    public const string SIGNAL = "signal";
    public const string SETENV = "setenv";
    public const string SET = "set";
    public const string SHOW = "show";
    public const string SIN = "sin";
    public const string SIZE = "size";
    public const string SLEEP = "sleep";
    public const string SQRT = "sqrt";
    public const string STARTSRV = "startsrv";
    public const string STOPWATCH_ELAPSED = "StopWatchElapsed";
    public const string STOPWATCH_START = "StartStopWatch";
    public const string STOPWATCH_STOP = "StopStopWatch";
    public const string STR_BETWEEN = "StrBetween";
    public const string STR_BETWEEN_ANY = "StrBetweenAny";
    public const string STR_CONTAINS = "StrContains";
    public const string STR_ENDS_WITH = "StrEndsWith";
    public const string STR_EQUALS = "StrEqual";
    public const string STR_INDEX_OF = "StrIndexOf";
    public const string STR_LOWER = "StrLower";
    public const string STR_REPLACE = "StrReplace";
    public const string STR_STARTS_WITH = "StrStartsWith";
    public const string STR_SUBSTR = "Substring";
    public const string STR_TRIM = "StrTrim";
    public const string STR_UPPER = "StrUpper";
    public const string SUBSTR = "substr";
    public const string TAIL = "tail";
    public const string THREAD = "thread";
    public const string THREAD_ID = "threadid";
    public const string TIMESTAMP = "Timestamp";
    public const string TOKENIZE = "tokenize";
    public const string TOKENIZE_LINES = "TokenizeLines";
    public const string TOKEN_COUNTER = "CountTokens";
    public const string TOLOWER = "tolower";
    public const string TOUPPER = "toupper";
    public const string TO_BOOL = "bool";
    public const string TO_DECIMAL = "decimal";
    public const string TO_DOUBLE = "double";
    public const string TO_INT = "int";
    public const string TO_STRING = "string";
    public const string TRANSLATE = "translate";
    public const string WAIT = "wait";
    public const string WRITE = "write";
    public const string WRITELINE = "writeline";
    public const string WRITELINES = "writelines";
    public const string WRITENL = "writenl";
    public const string WRITE_CONSOLE = "WriteConsole";

    public const string START_DEBUGGER = "StartDebugger";

    public static string END_ARG_STR = END_ARG.ToString();
    public static string NULL_ACTION = END_ARG.ToString();

    public static string[] OPER_ACTIONS = { "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=" };
    public static string[] MATH_ACTIONS = { "&&", "||", "==", "!=", "<=", ">=", "++", "--",
                                            "%", "*", "/", "+", "-", "^", "&", "|", "<", ">", "="};
    // Actions: always decreasing by the number of characters.
    public static string[] ACTIONS = (OPER_ACTIONS.Union(MATH_ACTIONS)).ToArray();

    public static char[] TERNARY_SEPARATOR = { ':' };
    public static char[] NEXT_ARG_ARRAY = NEXT_ARG.ToString().ToCharArray();
    public static char[] END_ARG_ARRAY = END_ARG.ToString().ToCharArray();
    public static char[] END_ARRAY_ARRAY = END_ARRAY.ToString().ToCharArray();
    public static char[] END_LINE_ARRAY = END_LINE.ToString().ToCharArray();
    public static char[] FOR_ARRAY = (END_ARG_STR + FOR_EACH).ToCharArray();
    public static char[] QUOTE_ARRAY = QUOTE.ToString().ToCharArray();

    public static char[] COMPARE_ARRAY = "<>=)".ToCharArray();
    public static char[] IF_ARG_ARRAY = "&|)".ToCharArray();
    public static char[] END_PARSE_ARRAY = { SPACE, END_STATEMENT, END_ARG, END_GROUP, '\n' };
    public static char[] NEXT_OR_END_ARRAY = { NEXT_ARG, END_ARG, END_GROUP, END_STATEMENT, SPACE };

    public static string TOKEN_SEPARATION_STR = "<>=+-*/%&|^,!()[]{}\t\n; ";
    public static char [] TOKEN_SEPARATION = TOKEN_SEPARATION_STR.ToCharArray ();

    // Functions that allow a space separator after them, on top of parentheses. The
    // function arguments may have spaces as well, e.g. copy a.txt b.txt
    public static List<string> FUNCT_WITH_SPACE = new List<string> {
      APPENDLINE, CD, CONNECTSRV, COPY, DELETE, DIR, EXISTS, FINDFILES, FINDSTR,
      FUNCTION, COMPILED_FUNCTION, MKDIR, MORE, MOVE, PRINT, READFILE, RUN, SHOW, STARTSRV, TAIL,
      TRANSLATE, WRITE, WRITELINE, WRITENL
    };

    // Functions that allow a space separator after them, on top of parentheses but
    // only once, i.e. function arguments are not allowed to have spaces
    // between them e.g. return a*b;
    public static List<string> FUNCT_WITH_SPACE_ONCE = new List<string> {
            RETURN, THROW
        };

    // The Control Flow Functions. It doesn't make sense to merge them or
    // use in calculation of a result.
    public static List<string> CONTROL_FLOW = new List<string> {
      BREAK, CONTINUE, FUNCTION, COMPILED_FUNCTION, IF, INCLUDE, FOR, WHILE, RETURN, THROW, TRY
    };

    public static List<string> RESERVED = new List<string> {
      BREAK, CONTINUE, FUNCTION, COMPILED_FUNCTION, IF, ELSE, ELSE_IF, INCLUDE, FOR, WHILE, RETURN, THROW, TRY, CATCH, COMMENT,
      ASSIGNMENT, AND, OR, EQUAL, NOT_EQUAL, LESS, LESS_EQ, GREATER, GREATER_EQ,
      ADD_ASSIGN, SUBT_ASSIGN, MULT_ASSIGN, DIV_ASSIGN,
      NEXT_ARG.ToString(), START_GROUP.ToString(), END_GROUP.ToString(), END_STATEMENT.ToString()
    };
    public static List<string> ARITHMETIC_EXPR = new List<string> {
      "*", "*=" , "-", "-=", "/", "/="
    };

    public static string STATEMENT_SEPARATOR = ";{}";
    public static string STATEMENT_TOKENS = " ";
    public static string NUMBER_OPERATORS = "+-*/%";

    public static List<string> ELSE_LIST = new List<string>();
    public static List<string> ELSE_IF_LIST = new List<string>();
    public static List<string> CATCH_LIST = new List<string>();

    public static string ALL_FILES = "*.*";

    public const int INDENT = 2;
    public const int DEFAULT_FILE_LINES = 20;
    public const int MAX_CHARS_TO_SHOW = 45;

    public const string ENGLISH  = "en";
    public const string GERMAN   = "de";
    public const string RUSSIAN  = "ru";
    public const string SPANISH  = "es";
    public const string SYNONYMS = "sy";

    public static string Language(string lang)
    {
      switch (lang) {
        case "English": return ENGLISH;
        case "German": return GERMAN;
        case "Russian": return RUSSIAN;
        case "Spanish": return SPANISH;
        case "Synonyms": return SYNONYMS;
        default: return ENGLISH;
      }
    }
    public static string TypeToString(Variable.VarType type)
    {
      switch (type) {
        case Variable.VarType.NUMBER:   return "NUMBER";
        case Variable.VarType.STRING:   return "STRING";
        case Variable.VarType.ARRAY_STR:
        case Variable.VarType.ARRAY_NUM:
        case Variable.VarType.ARRAY:    return "ARRAY";
        case Variable.VarType.MAP_STR:
        case Variable.VarType.MAP_NUM:  return "MAP";
        case Variable.VarType.BREAK:    return "BREAK";
        case Variable.VarType.CONTINUE: return "CONTINUE";
        default:                        return "NONE";
      }
    }
    public static Variable.VarType StringToType(string type)
    {
      type = type.ToUpper();
      switch (type) {
        case "BOOL":
        case "INT":
        case "FLOAT":
        case "DOUBLE":
        case "NUMBER":       return Variable.VarType.NUMBER;
        case "CHAR":
        case "STRING":       return Variable.VarType.STRING;
        case "LIST<INT>":
        case "LIST<DOUBLE>": return Variable.VarType.ARRAY_NUM;
        case "LIST<STRING>": return Variable.VarType.ARRAY_STR;
        case "MAP<INT>":
        case "MAP<STRING,INT>":
        case "MAP<DOUBLE>":
        case "MAP<STRING,DOUBLE>": return Variable.VarType.MAP_NUM;
        case "MAP<STRING>":
        case "MAP<STRING,STRING>": return Variable.VarType.MAP_STR;
        case "TUPLE":
        case "ARRAY":        return Variable.VarType.ARRAY;
        case "BREAK":        return Variable.VarType.BREAK;
        case "CONTINUE":     return Variable.VarType.CONTINUE;
        default:             return Variable.VarType.NONE;
      }
    }
  }
}
