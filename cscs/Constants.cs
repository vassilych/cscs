using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public partial class Constants
    {
        public const char START_ARG = '(';
        public const char END_ARG = ')';
        public const char START_ARRAY = '[';
        public const char END_ARRAY = ']';
        public const char END_LINE = '\n';
        public const char NEXT_ARG = ',';
        public const char QUOTE = '"';
        public const char QUOTE1 = '\'';
        public const char SPACE = ' ';
        public const char START_GROUP = '{';
        public const char END_GROUP = '}';
        public const char VAR_START = '$';
        public const char END_STATEMENT = ';';
        public const char CONTINUE_LINE = '\\';
        public const char EMPTY = '\0';
        public const char TERNARY_OPERATOR = '?';
        public const string FOR_EACH = ":";

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
        public const string CANCEL = "cancel_operation";
        public const string COMMENT = "//";
        public const string CONTINUE = "continue";
        public const string ELSE = "else";
        public const string ELSE_IF = "elif";
        public const string FOR = "for";
        public const string FUNCTION = "function";
        public const string CLASS = "class";
        public const string ENUM = "enum";
        public const string IF = "if";
        public const string INCLUDE = "include";
        public const string NEW = "new";
        public const string RETURN = "return";
        public const string THROW = "throw";
        public const string TRY = "try";
        public const string TYPE = "type";
        public const string WHILE = "while";

        public const string ADD_COMP_DEFINITION = "add_comp_definition";
        public const string ADD_COMP_NAMESPACE = "add_comp_namespace";
        public const string CLEAR_COMP_DEFINITIONS = "clear_comp_definitions";
        public const string CLEAR_COMP_NAMESPACES = "clear_comp_namespaces";
        public const string COMPILED_FUNCTION = "cfunction";
        public const string CSHARP_FUNCTION = "csfunction";

        public const string TRUE = "true";
        public const string FALSE = "false";

        public const string ABS = "abs";
        public const string ACOS = "acos";
        public const string ADD = "add";
        public const string ADD_TO_HASH = "AddToHash";
        public const string ADD_ALL_TO_HASH = "AddAllToHash";
        public const string ASIN = "asin";
        public const string CANCEL_RUN = "CancelRun";
        public const string CEIL = "ceil";
        public const string CHECK_LOADER_MAIN = "LoaderMain";
        public const string CONTAINS = "contains";
        public const string COS = "cos";
        public const string CURRENT_PATH = "CurrentPath";
        public const string DATE_TIME = "DateTime";
        public const string DEEP_COPY = "DeepCopy";
        public const string DEFINE_LOCAL = "DefineLocal";
        public const string ENV = "env";
        public const string EXIT = "exit";
        public const string EXP = "exp";
        public const string FIND_INDEX = "find_index";
        public const string FLOOR = "floor";
        public const string GET_COLUMN = "GetColumn";
        public const string GET_PROPERTIES = "GetPropertyStrings";
        public const string GET_PROPERTY = "GetProperty";
        public const string GET_KEYS = "GetKeys";
        public const string HELP = "help";
        public const string LOCK = "lock";
        public const string LOG = "log";
        public const string NAME_EXISTS = "NameExists";
        public const string NAMESPACE = "Namespace";
        public const string NOW = "Now";
        public const string OBJECT_PROPERTIES = "Properties";
        public const string OBJECT_TYPE = "Type";
        public const string PI = "pi";
        public const string POW = "pow";
        public const string PRINT = "print";
        public const string PSTIME = "pstime";
        public const string RANDOM = "GetRandom";
        public const string REGEX = "Regex";
        public const string REMOVE = "RemoveItem";
        public const string REMOVE_AT = "RemoveAt";
        public const string RESET_VARS = "ResetVariables";
        public const string ROUND = "round";
        public const string SCHEDULE_RUN = "ScheduleRun";
        public const string SETENV = "setenv";
        public const string SET_PROPERTY = "SetProperty";
        public const string SHOW = "show";
        public const string SIGNAL = "signal";
        public const string SIN = "sin";
        public const string SINGLETON = "singleton";
        public const string SIZE = "Size";
        public const string SLEEP = "sleep";
        public const string SQRT = "sqrt";
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
        public const string THREAD = "thread";
        public const string THREAD_ID = "threadid";
        public const string TOKENIZE_LINES = "TokenizeLines";
        public const string TOKEN_COUNTER = "CountTokens";
        public const string TO_BOOL = "bool";
        public const string TO_DECIMAL = "decimal";
        public const string TO_DOUBLE = "double";
        public const string TO_INT = "int";
        public const string TO_STRING = "string";
        public const string VARIABLE_TYPE = "VariableType";
        public const string WAIT = "wait";
        public const string WEB_REQUEST = "WebRequest";
        public const string JSON = "GetVariableFromJson";

        public const string START_DEBUGGER = "StartDebugger";
        public const string STOP_DEBUGGER  = "StopDebugger";
        public const string GET_FILE_FROM_DEBUGGER = "GetFileFromDebugger";

        public const string ADD_DATA     = "AddDataToCollection";
        public const string COLLECT_DATA = "StartCollectingData";
        public const string GET_DATA     = "GetCollectedData";

        // Properties, returned after the variable dot:
        public const string AT            = "At";
        public const string EMPTY_WHITE   = "EmptyOrWhite";
        public const string ENDS_WITH     = "EndsWith";
        public const string EQUALS        = "Equals";
        public const string FIRST         = "First";
        public const string INDEX_OF      = "IndexOf";
        public const string JOIN          = "Join";
        public const string KEYS          = "Keys";
        public const string LAST          = "Last";
        public const string LOWER         = "Lower";
        public const string REMOVE_ITEM   = "Remove";
        public const string REPLACE       = "Replace";
        public const string REPLACE_TRIM  = "ReplaceAndTrim";
        public const string REVERSE       = "Reverse";
        public const string SORT          = "Sort";
        public const string SPLIT         = "Split";
        public const string STRING        = "String";
        public const string STARTS_WITH   = "StartsWith";
        public const string SUBSTRING     = "Substring";
        public const string TOKENIZE      = "Tokenize";
        public const string TRIM          = "Trim";
        public const string UPPER         = "Upper";

        // Special property for converting an object to a string:
        public const string PROP_TO_STRING    = "ToString";

        public static string END_ARG_STR = END_ARG.ToString();
        public static string NULL_ACTION = END_ARG.ToString();

        public static string[] OPER_ACTIONS = { "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", ":" };
        public static string[] MATH_ACTIONS = { "&&", "||", "==", "!=", "<=", ">=", "++", "--", "**",
                                                "%", "*", "/", "+", "-", "^", "&", "|", "<", ">", "="};
        // Actions: always decreasing by the number of characters.
        public static string[] ACTIONS = (OPER_ACTIONS.Union(MATH_ACTIONS)).ToArray();

        public static string[] CORE_OPERATORS = (new List<string> { TRY, FOR, WHILE }).ToArray();

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

        public static string TOKEN_SEPARATION_STR = "<>=+-*/%&|^,!()[]{}\t\n;: ";
        public static char[] TOKEN_SEPARATION = TOKEN_SEPARATION_STR.ToCharArray();

        // Functions that allow a space separator after them, on top of parentheses. The
        // function arguments may have spaces as well, e.g. copy a.txt b.txt
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && __ANDROID__ == false && __IOS__ == false
        public static List<string> FUNCT_WITH_SPACE = new List<string>
        {
            APPENDLINE, CD, CLASS, CONNECTSRV, COPY, DELETE, DIR, EXISTS, FINDFILES, FINDSTR,
            FUNCTION, COMPILED_FUNCTION, CSHARP_FUNCTION, HELP, MKDIR, MORE, MOVE, NAMESPACE, NEW, PRINT, READFILE, RUN, SHOW, STARTSRV,
            TAIL, THREAD, TRANSLATE, WRITE, WRITELINE, WRITENL
        };
#else
        public static List<string> FUNCT_WITH_SPACE = new List<string> {
            CLASS, FUNCTION, COMPILED_FUNCTION, CSHARP_FUNCTION, HELP, NEW, NAMESPACE, SHOW, THREAD
        };
#endif
        // Functions that allow a space separator after them, on top of parentheses but
        // only once, i.e. function arguments are not allowed to have spaces
        // between them e.g. return a*b;
        public static List<string> FUNCT_WITH_SPACE_ONCE = new List<string>
        {
            RETURN, THROW
        };

        // The Control Flow Functions. It doesn't make sense to merge them or
        // use in calculation of a result.
        public static List<string> CONTROL_FLOW = new List<string>
        {
            BREAK, CATCH, CLASS, COMPILED_FUNCTION, CSHARP_FUNCTION, CONTINUE, ELSE, ELSE_IF, ELSE, FOR, FUNCTION, IF, INCLUDE, NEW,
            RETURN, THROW, TRY, WHILE
        };

        public static List<string> RESERVED = new List<string>
        {
            BREAK, CONTINUE, CLASS, NEW, FUNCTION, COMPILED_FUNCTION, CSHARP_FUNCTION, IF, ELSE, ELSE_IF, INCLUDE, FOR, WHILE,
            RETURN, THROW, TRY, CATCH, COMMENT, TRUE, FALSE, TYPE,
            ASSIGNMENT, AND, OR, EQUAL, NOT_EQUAL, LESS, LESS_EQ, GREATER, GREATER_EQ,
            ADD_ASSIGN, SUBT_ASSIGN, MULT_ASSIGN, DIV_ASSIGN,
            NEXT_ARG.ToString(), START_GROUP.ToString(), END_GROUP.ToString(), END_STATEMENT.ToString()
        };

        public static List<string> ARITHMETIC_EXPR = new List<string>
        {
            "*", "*=" , "+", "+=" , "-", "-=", "/", "/=", "%", "%=", ">", "<", ">=", "<="
        };

        public static string STATEMENT_SEPARATOR = ";{}";
        public static string STATEMENT_TOKENS = " ";
        public static string NUMBER_OPERATORS = "+-*/%";

        public static string ALL_FILES = "*.*";

        public const int INDENT = 2;
        public const int DEFAULT_FILE_LINES = 20;
        public const int MAX_CHARS_TO_SHOW = 45;

        static Dictionary<string, string> s_realNames = new Dictionary<string, string>();

        public static string ConvertName(string name)
        {
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name) || name[0] == QUOTE || name[0] == QUOTE1)
            {
                return name;
            }

            string lower = name.ToLower(System.Globalization.CultureInfo.CurrentCulture);
            if (name == lower || CONTROL_FLOW.Contains(lower))
            { // Do not permit using key words with no case, like IF, For
                return name;
            }

            s_realNames[lower] = name;
            return lower;
        }

        public static string GetRealName(string name)
        {
            name = name.Trim();
            string realName;
            if (!s_realNames.TryGetValue(name, out realName))
            {
                return name;
            }
            return realName;
        }

        public static string TypeToString(Variable.VarType type)
        {
            switch (type)
            {
                case Variable.VarType.NUMBER:   return "NUMBER";
                case Variable.VarType.STRING:   return "STRING";
                case Variable.VarType.ARRAY_STR:
                case Variable.VarType.ARRAY_NUM:
                case Variable.VarType.ARRAY:    return "ARRAY";
                case Variable.VarType.MAP_STR:
                case Variable.VarType.MAP_NUM:  return "MAP";
                case Variable.VarType.OBJECT:   return "OBJECT";
                case Variable.VarType.BREAK:    return "BREAK";
                case Variable.VarType.CONTINUE: return "CONTINUE";
                default: return "NONE";
            }
        }
        public static Variable.VarType StringToType(string type)
        {
            type = type.ToUpper();
            switch (type)
            {
                case "BOOL":
                case "INT":
                case "FLOAT":
                case "DOUBLE":
                case "NUMBER": return Variable.VarType.NUMBER;
                case "CHAR":
                case "STRING": return Variable.VarType.STRING;
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
                case "ARRAY": return Variable.VarType.ARRAY;
                case "BREAK": return Variable.VarType.BREAK;
                case "CONTINUE": return Variable.VarType.CONTINUE;
                case "VARIABLE": return Variable.VarType.VARIABLE;
                default: return Variable.VarType.NONE;
            }
        }
    }
}
