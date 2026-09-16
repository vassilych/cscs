using SplitAndMerge;

namespace CscsSandbox;

/// <summary>
/// The functions an untrusted script may call. Everything the interpreter registers is removed
/// afterwards unless it is named here, so a function added to CSCS later stays unavailable until
/// someone decides it is safe and adds it.
///
/// Written against the Constants symbols, so a renamed function breaks the build instead of
/// silently dropping out of (or into) the sandbox.
///
/// Deliberately left out, and why:
///   cfunction, dllfunction, importdll   compile or load real .NET code (cfunction returns in explain
///                                       mode, where nothing compiled is loaded -- see ExplainMode)
///   include, includeSecure, import      read files and load modules
///   every file, directory, process and socket function (Interpreter.Standalone)
///   webRequest, download                network
///   env, setenv, currentPath, commandLineArgs   the host's environment
///   thread, newThread, threadResult, signal, wait, lock, sleep, scheduleRun, cancelRun
///                                       threads and timers outlive the watchdog's view of the script
///   startDebugger, stopDebugger, getFileFromDebugger   open a debugger port / read files
///   typeRef                             hands the script a .NET Type
///   marshal, unmarshal, toByteArray     serialize to .NET objects and byte arrays
///   quit, exit                          would end the worker without a result
///   show, help, resetVars, singleton, addData/collectData/getData   host-facing, no use here
/// </summary>
static class Allowlist
{
    static readonly HashSet<string> s_names = new[]
    {
        // Statements and declarations
        Constants.IF, Constants.IFF, Constants.DO, Constants.WHILE, Constants.SWITCH, Constants.CASE,
        Constants.DEFAULT, Constants.FOR, Constants.BREAK, Constants.CONTINUE, Constants.RETURN,
        Constants.FUNCTION, Constants.CLASS, Constants.ENUM, Constants.NEW, Constants.NULL,
        Constants.TRY, Constants.THROW, Constants.VAR, Constants.DEFINE_LOCAL, Constants.NAMESPACE,
        Constants.FREE, Constants.TRUE, Constants.FALSE, Constants.UNDEFINED,

        // Types, properties and objects -- CSCS ones; .NET is shut off by InterpreterSecurity
        Constants.TYPE, Constants.TYPE_OF, Constants.GET_PROPERTIES, Constants.GET_PROPERTY,
        Constants.SET_PROPERTY, Constants.OBJECT_DEFPROP, Constants.NAME_EXISTS,

        // Collections
        Constants.ADD, Constants.ADD_TO_HASH, Constants.ADD_ALL_TO_HASH, Constants.CONTAINS,
        Constants.DEEP_COPY, Constants.FIND_INDEX, Constants.GET_COLUMN, Constants.GET_KEYS,
        Constants.REMOVE, Constants.REMOVE_AT, Constants.SIZE,

        // Strings and conversion
        Constants.STR_BETWEEN, Constants.STR_BETWEEN_ANY, Constants.STR_CONTAINS, Constants.STR_LOWER,
        Constants.STR_ENDS_WITH, Constants.STR_EQUALS, Constants.STR_INDEX_OF, Constants.STR_REPLACE,
        Constants.STR_STARTS_WITH, Constants.STR_SUBSTR, Constants.STR_TRIM, Constants.STR_UPPER,
        Constants.TOKENIZE, Constants.TOKENIZE_LINES, Constants.TOKEN_COUNTER, Constants.REGEX,
        Constants.ENCODE, Constants.DECODE, Constants.JSON,
        Constants.TO_BOOL, Constants.TO_DECIMAL, Constants.TO_DOUBLE, Constants.TO_INT,
        Constants.TO_NUMBER, Constants.TO_STRING,

        // Output and time
        Constants.PRINT, Constants.CONSOLE_LOG, Constants.NOW, Constants.DATE_TIME, Constants.PSTIME,
    }.Select(Constants.ConvertName).ToHashSet();

    /// <summary>The Math module registers everything under "Math." -- pure functions, all kept.</summary>
    static readonly string s_mathPrefix = Constants.ConvertName("Math.");

    static readonly string s_cfunction = Constants.ConvertName(Constants.COMPILED_FUNCTION);

    /// <summary>
    /// Explain mode keeps cfunction, and only then: PrecompileExplainer is on for the whole process,
    /// so a definition is translated and compiled in memory for the report and the function itself
    /// runs interpreted. Nothing a script compiles is ever loaded.
    /// </summary>
    public static bool ExplainMode { get; set; }

    public static bool Keep(string name, ParserFunction function) =>
        s_names.Contains(name) ||
        (ExplainMode && name == s_cfunction) ||
        name.StartsWith(s_mathPrefix, StringComparison.Ordinal) ||
        // A registered constant: an enum value the interpreter set up. It is only a value, and
        // with .NET access off a value cannot be turned into anything else.
        function is GetVarFunction;
}
