﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
//using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    // Prints passed list of argumentsand
    class PrintFunction : ParserFunction
    {
        internal PrintFunction(bool newLine = true)
        {
            m_newLine = newLine;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            AddOutput(args, script, m_newLine);

            return Variable.EmptyInstance;
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            AddOutput(args, script, m_newLine);

            return Variable.EmptyInstance;
        }

        public void AddOutput(List<Variable> args, ParsingScript script = null,
                                     bool addLine = true, bool addSpace = true, string start = "")
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(start);
            foreach (var arg in args)
            {
                sb.Append(arg.AsString() + (addSpace ? " " : ""));
            }

            string output = sb.ToString() + (addLine ? Environment.NewLine : string.Empty);
            output = output.Replace("\\t", "\t").Replace("\\n", "\n");
            InterpreterInstance.AppendOutput(output);

            Debugger debugger = script != null && script.Debugger != null ?
                                script.Debugger : Debugger.MainInstance;
            if (debugger != null)
            {
                debugger.AddOutput(output, script);
            }
        }

        private bool m_newLine = true;
    }

    class DataFunction : ParserFunction
    {
        internal enum DataMode { ADD, SUBSCRIBE, SEND };

        DataMode m_mode;

        internal DataFunction(DataMode mode = DataMode.ADD)
        {
            m_mode = mode;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            string result = "";

            switch (m_mode)
            {
                case DataMode.ADD:
                    InterpreterInstance.DataFunctionData.Collect(args);
                    break;
                case DataMode.SUBSCRIBE:
                    InterpreterInstance.DataFunctionData.Subscribe(args);
                    break;
                case DataMode.SEND:
                    result = InterpreterInstance.DataFunctionData.Send();
                    break;
            }

            return new Variable(result);
        }
    }

    public class DataFunctionData
    {
        public string s_method;
        public string s_tracking;
        public bool s_updateImmediate = false;

        public StringBuilder s_data = new StringBuilder();
        private Interpreter _interpreterInstance;

        public DataFunctionData(Interpreter interpreterInstance)
        {
            _interpreterInstance = interpreterInstance;
        }

        public void Subscribe(List<Variable> args)
        {
            s_data.Clear();

            s_method = Utils.GetSafeString(args, 0);
            s_tracking = Utils.GetSafeString(args, 1);
            s_updateImmediate = Utils.GetSafeDouble(args, 2) > 0;
        }

        public void Collect(List<Variable> args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var arg in args)
            {
                sb.Append(arg.AsString());
            }

            if (s_updateImmediate)
            {
                SendData(sb.ToString());
            }
            else
            {
                s_data.AppendLine(sb.ToString());
            }
        }

        public string Send()
        {
            var result = SendData(s_data.ToString());
            s_data.Clear();
            return result;
        }

        private string SendData(string data)
        {
            if (!string.IsNullOrWhiteSpace(s_method))
            {
                CustomFunction.Run(_interpreterInstance, s_method,
                    new Variable(s_tracking), new Variable(data));
                return "";
            }
            return data;
        }
    }

    class CurrentPathFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            return new Variable(script.PWD);
        }
    }

    // Returns how much processor time has been spent on the current process
    class ProcessorTimeFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Process pr = Process.GetCurrentProcess();
            TimeSpan ts = pr.TotalProcessorTime;

            return new Variable(Math.Round(ts.TotalMilliseconds, 0));
        }
    }

    class TokenizeFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            Utils.CheckArgs(args.Count, 1, m_name);
            string data = Utils.GetSafeString(args, 0);

            string sep = Utils.GetSafeString(args, 1, "\t");
            var option = Utils.GetSafeString(args, 2);

            return Tokenize(data, sep, option);
        }

        static public Variable Tokenize(string data, string sep, string option = "", int max = int.MaxValue - 1)
        {
            if (sep == "\\t")
            {
                sep = "\t";
            }
            if (sep == "\\n")
            {
                sep = "\n";
            }

            string[] tokens;
            var sepArray = sep.ToCharArray();
            if (sepArray.Count() == 1)
            {
                tokens = data.Split(sepArray, max);
            }
            else
            {
                List<string> tokens_ = new List<string>();
                var rx = new System.Text.RegularExpressions.Regex(sep);
                tokens = rx.Split(data);
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(tokens[i]) || sep.Contains(tokens[i]))
                    {
                        continue;
                    }
                    tokens_.Add(tokens[i]);
                }
                tokens = tokens_.ToArray();
            }

            List<Variable> results = new List<Variable>();
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (i > 0 && string.IsNullOrWhiteSpace(token) &&
                    option.StartsWith("prev", StringComparison.OrdinalIgnoreCase))
                {
                    token = tokens[i - 1];
                }
                results.Add(new Variable(token));
            }

            return new Variable(results);
        }
    }

    class StringManipulationFunction : ParserFunction
    {
        public enum Mode
        {
            CONTAINS, STARTS_WITH, ENDS_WITH, INDEX_OF, EQUALS, REPLACE,
            UPPER, LOWER, TRIM, SUBSTRING, BEETWEEN, BEETWEEN_ANY
        };
        Mode m_mode;

        public StringManipulationFunction(Mode mode)
        {
            m_mode = mode;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            Utils.CheckArgs(args.Count, 1, m_name);
            string source = Utils.GetSafeString(args, 0);
            string argument = Utils.GetSafeString(args, 1);
            string parameter = Utils.GetSafeString(args, 2, "case");
            int startFrom = Utils.GetSafeInt(args, 3, 0);
            int length = Utils.GetSafeInt(args, 4, source.Length);

            StringComparison comp = StringComparison.Ordinal;
            if (parameter.Equals("nocase") || parameter.Equals("no_case"))
            {
                comp = StringComparison.OrdinalIgnoreCase;
            }

            source = source.Replace("\\\"", "\"");
            argument = argument.Replace("\\\"", "\"");

            switch (m_mode)
            {
                case Mode.CONTAINS:
                    return new Variable(source.IndexOf(argument, comp) >= 0);
                case Mode.STARTS_WITH:
                    return new Variable(source.StartsWith(argument, comp));
                case Mode.ENDS_WITH:
                    return new Variable(source.EndsWith(argument, comp));
                case Mode.INDEX_OF:
                    return new Variable(source.IndexOf(argument, startFrom, comp));
                case Mode.EQUALS:
                    return new Variable(source.Equals(argument, comp));
                case Mode.REPLACE:
                    return new Variable(source.Replace(argument, parameter));
                case Mode.UPPER:
                    return new Variable(source.ToUpper());
                case Mode.LOWER:
                    return new Variable(source.ToLower());
                case Mode.TRIM:
                    return new Variable(source.Trim());
                case Mode.SUBSTRING:
                    startFrom = Utils.GetSafeInt(args, 1, 0);
                    length = Utils.GetSafeInt(args, 2, source.Length);
                    length = Math.Min(length, source.Length - startFrom);
                    return new Variable(source.Substring(startFrom, length));
                case Mode.BEETWEEN:
                case Mode.BEETWEEN_ANY:
                    int index1 = source.IndexOf(argument, comp);
                    int index2 = m_mode == Mode.BEETWEEN ? source.IndexOf(parameter, index1 + 1, comp) :
                                          source.IndexOfAny(parameter.ToCharArray(), index1 + 1);
                    startFrom = index1 + argument.Length;

                    if (index1 < 0 || index2 < index1)
                    {
                        throw new ArgumentException("Couldn't extract string between [" + argument +
                                                    "] and [" + parameter + "] + from " + source);
                    }
                    string result = source.Substring(startFrom, index2 - startFrom);
                    return new Variable(result);
            }

            return new Variable(-1);
        }
    }

    // Append a string to another string
    class AppendFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Get the name of the variable.
            string varName = Utils.GetToken(script, Constants.NEXT_ARG_ARRAY);
            Utils.CheckNotEmpty(script, varName, m_name);

            // 2. Get the current value of the variable.
            ParserFunction func = InterpreterInstance.GetVariable(varName, script);
            Variable currentValue = func.GetValue(script);

            // 3. Get the value to be added or appended.
            Variable newValue = Utils.GetItem(script);

            // 4. Take either the string part if it is defined,
            // or the numerical part converted to a string otherwise.
            string arg1 = currentValue.AsString();
            string arg2 = newValue.AsString();

            // 5. The variable becomes a string after adding a string to it.
            newValue.Reset();
            newValue.String = arg1 + arg2;

            InterpreterInstance.AddGlobalOrLocalVariable(varName, new GetVarFunction(newValue), script);

            return newValue;
        }
    }

    class SignalWaitFunction : ParserFunction, INumericFunction
    {
        static AutoResetEvent waitEvent = new AutoResetEvent(false);
        bool m_isSignal;

        public SignalWaitFunction(bool isSignal)
        {
            m_isSignal = isSignal;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            bool result = m_isSignal ? waitEvent.Set() :
                                       waitEvent.WaitOne();
            return new Variable(result);
        }
    }

    public class ThreadFunction : ParserFunction, INumericFunction
    {
        bool m_poolThread;
        bool m_extractResult;
        static ConcurrentDictionary<int, Variable> ThreadResult = new ConcurrentDictionary<int, Variable>();
        static ConcurrentDictionary<int, Thread> Threads = new ConcurrentDictionary<int, Thread>();

        public ThreadFunction(bool isPoolThread = true, bool extractResult = false)
        {
            m_poolThread = isPoolThread;
            m_extractResult = extractResult;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            if (m_extractResult)
            {
                List<Variable> args = script.GetFunctionArgs();
                Utils.CheckArgs(args.Count, 1, m_name);
                var threadId = Utils.GetSafeInt(args, 0);
                return ExtractResult(threadId);
            }
            string body = script.TryPrev() == Constants.START_GROUP ?
                          Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP) :
                          Utils.GetBodyBetween(script, Constants.START_ARG, Constants.END_ARG);

            if (!m_poolThread)
            {
                return NewThread(body);
            }

            RunScript(body, script.InterpreterInstance);
            return Variable.EmptyInstance;
        }

        public Variable NewThread(string scriptBody)
        {
            Thread thread = new Thread(() =>
            {
                ParsingScript threadScript = NewParsingScript(scriptBody);
                threadScript.SetInterpreter(InterpreterInstance);
                var result = threadScript.ExecuteAll();
                ThreadResult[Thread.CurrentThread.ManagedThreadId] = result;
            });
            thread.Start();
            Threads[thread.ManagedThreadId] = thread;

            return new Variable(thread.ManagedThreadId);
        }
        public Variable ExtractResult(int threadId)
        {
            if (!Threads.TryGetValue(threadId, out Thread thread))
            {
                return Variable.EmptyInstance;
            }
            thread.Join();
            if (!ThreadResult.TryGetValue(threadId, out Variable result))
            {
                return Variable.EmptyInstance;
            }

            Threads.TryRemove(threadId, out _);
            ThreadResult.TryRemove(threadId, out _);

            return result;
        }

        public void RunScript(string scriptBody, Interpreter interpreter, bool inNewThread = true)
        {
            InterpreterInstance = interpreter;
            if (inNewThread)
            {
                ThreadPool.QueueUserWorkItem(RunScriptLocal, scriptBody);
            }
            else
            {
                RunScriptLocal(scriptBody);
            }
        }

        void RunScriptLocal(Object stateInfo)
        {
            string body = (string)stateInfo;
            ParsingScript threadScript = NewParsingScript(body);
            threadScript.SetInterpreter(InterpreterInstance);
            threadScript.ExecuteAll();
        }
    }
    class ThreadIDFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            int threadID = Thread.CurrentThread.ManagedThreadId;
            return new Variable(threadID.ToString());
        }
    }
    class SleepFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Variable sleepms = Utils.GetItem(script);
            Utils.CheckPosInt(sleepms, script);

            Thread.Sleep((int)sleepms.Value);

            return Variable.EmptyInstance;
        }
    }
    class LockFunction : ParserFunction
    {
        static Object lockObject = new Object();

        protected override Variable Evaluate(ParsingScript script)
        {
            string body = Utils.GetBodyBetween(script, Constants.START_ARG,
                                                       Constants.END_ARG);
            ParsingScript threadScript = NewParsingScript(body);

            // BUGBUG: Alfred - what is this actually locking?
            // Vassili - it's a global (static) lock. used when called from different threads
            lock (lockObject)
            {
                threadScript.ExecuteAll();
            }
            return Variable.EmptyInstance;
        }
    }

    class DateTimeFunction : ParserFunction, IStringFunction
    {
        bool m_stringVersion;

        public DateTimeFunction(bool stringVersion = true)
        {
            m_stringVersion = stringVersion;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            string strFormat = m_stringVersion ? Utils.GetSafeString(args, 0, "HH:mm:ss.fff") :
                                          Utils.GetSafeString(args, 1, "yyyy/MM/dd HH:mm:ss");
            Utils.CheckNotEmpty(strFormat, m_name);


            if (m_stringVersion)
            {
                return new Variable(DateTime.Now.ToString(strFormat));
            }

            var date = DateTime.Now;
            string when = Utils.GetSafeString(args, 0);

            if (!string.IsNullOrWhiteSpace(when) && !DateTime.TryParseExact(when, strFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            {
                throw new ArgumentException("Couldn't parse [" + when + "] using format [" +
                                            strFormat + "].");
            }
            if (!strFormat.Contains("yy") && !strFormat.Contains("MM") && !strFormat.Contains("dd"))
            {
                date = date.Subtract(new TimeSpan(date.Date.Ticks));
            }

            return new Variable(date);
        }

        public static DateTime Add(DateTime current, string delta)
        {
            int sign = 1;
            string part = "";
            int partInt;
            for (int i = 0; i < delta.Length; i++)
            {
                switch (delta[i])
                {
                    case '-':
                        sign *= -1;
                        continue;
                    case 'y':
                        partInt = string.IsNullOrWhiteSpace(part) ? 1 : !int.TryParse(part, out partInt) ? 0 : partInt;
                        current = current.AddYears(partInt * sign);
                        break;
                    case 'M':
                        partInt = string.IsNullOrWhiteSpace(part) ? 1 : !int.TryParse(part, out partInt) ? 0 : partInt;
                        current = current.AddMonths(partInt * sign);
                        break;
                    case 'd':
                        partInt = string.IsNullOrWhiteSpace(part) ? 1 : !int.TryParse(part, out partInt) ? 0 : partInt;
                        current = current.AddDays(partInt * sign);
                        break;
                    case 'H':
                    case 'h':
                        partInt = string.IsNullOrWhiteSpace(part) ? 1 : !int.TryParse(part, out partInt) ? 0 : partInt;
                        current = current.AddHours(partInt * sign);
                        break;
                    case 'm':
                        partInt = string.IsNullOrWhiteSpace(part) ? 1 : !int.TryParse(part, out partInt) ? 0 : partInt;
                        current = current.AddMinutes(partInt * sign);
                        break;
                    case 's':
                        partInt = string.IsNullOrWhiteSpace(part) ? 1 : !int.TryParse(part, out partInt) ? 0 : partInt;
                        current = current.AddSeconds(partInt * sign);
                        break;
                    case 'f':
                        partInt = string.IsNullOrWhiteSpace(part) ? 1 : !int.TryParse(part, out partInt) ? 0 : partInt;
                        current = current.AddTicks(partInt * sign);
                        break;
                    default:
                        part += delta[i];
                        continue;
                }
                part = "";
            }
            return current;
        }
    }

    class DebuggerFunction : ParserFunction
    {
        bool m_start = true;
        public DebuggerFunction(bool start = true)
        {
            m_start = start;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            string res = "OK";
            List<Variable> args = script.GetFunctionArgs();
            if (m_start)
            {
                int port = Utils.GetSafeInt(args, 0, 13337);
                bool allowRemote = Utils.GetSafeInt(args, 1, 0) == 1;

                DebuggerServer.AllowedClients = Utils.GetSafeString(args, 2);

                res = DebuggerServer.StartServer(port, allowRemote);
            }
            else
            {
                DebuggerServer.StopServer();
            }

            return new Variable(res);
        }
    }
    // Returns an environment variable
    class GetEnvFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string varName = Utils.GetToken(script, Constants.END_ARG_ARRAY);
            string res = Environment.GetEnvironmentVariable(varName);

            return new Variable(res);
        }
    }

    // Sets an environment variable
    class SetEnvFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string varName = Utils.GetToken(script, Constants.NEXT_ARG_ARRAY);
            Utils.CheckNotEmpty(script, varName, m_name);

            Variable varValue = Utils.GetItem(script);
            string strValue = varValue.AsString();
            Environment.SetEnvironmentVariable(varName, strValue);

            return new Variable(varName);
        }
    }

    class GetFileFromDebugger : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            Utils.CheckArgs(args.Count, 2, m_name);
            string filename = Utils.GetSafeString(args, 0);
            string destination = Utils.GetSafeString(args, 1);

            Variable result = new Variable(Variable.VarType.ARRAY);
            result.Tuple.Add(new Variable(Constants.GET_FILE_FROM_DEBUGGER));
            result.Tuple.Add(new Variable(filename));
            result.Tuple.Add(new Variable(destination));

            result.ParsingToken = m_name;

            return result;
        }
    }

    class RegexFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            Utils.CheckArgs(args.Count, 2, m_name);
            string pattern = Utils.GetSafeString(args, 0);
            string text = Utils.GetSafeString(args, 1);

            Variable result = new Variable(Variable.VarType.ARRAY);

            Regex rx = new Regex(pattern,
                        RegexOptions.Compiled | RegexOptions.IgnoreCase);

            MatchCollection matches = rx.Matches(text);

            foreach (Match match in matches)
            {
                result.AddVariableToHash("matches", new Variable(match.Value));

                var groups = match.Groups;
                foreach (var group in groups)
                {
                    result.AddVariableToHash("groups", new Variable(group.ToString()));
                }
            }

            return result;
        }
    }

    class EditCompiledEntry : ParserFunction
    {
        internal enum EditMode { ADD_DEFINITION, ADD_NAMESPACE, CLEAR_DEFINITIONS, CLEAR_NAMESPACES };
        EditMode m_mode;

        internal EditCompiledEntry(EditMode mode)
        {
            m_mode = mode;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            string item = Utils.GetSafeString(args, 0);

#if __ANDROID__ == false && __IOS__ == false

            switch (m_mode)
            {
                case EditMode.ADD_DEFINITION:
                    Precompiler.AddDefinition(item);
                    break;
                case EditMode.ADD_NAMESPACE:
                    Precompiler.AddNamespace(item);
                    break;
                case EditMode.CLEAR_DEFINITIONS:
                    Precompiler.ClearDefinitions();
                    break;
                case EditMode.CLEAR_NAMESPACES:
                    Precompiler.ClearNamespaces();
                    break;
            }
#endif

            return Variable.EmptyInstance;
        }
    }

    class CompiledFunctionCreator : ParserFunction
    {
        bool m_scriptInCSharp = false;

        public CompiledFunctionCreator(bool scriptInCSharp)
        {
#if UNITY_EDITOR == false && UNITY_STANDALONE == false && _ANDROID__ == false && __IOS__ == false
            m_scriptInCSharp = scriptInCSharp;
#endif
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            string funcReturn, funcName;
            Utils.GetCompiledArgs(script, out funcReturn, out funcName);

#if __ANDROID__ == false && __IOS__ == false
            Precompiler.RegisterReturnType(funcName, funcReturn);

            string[] args = Utils.GetCompiledFunctionSignature(script, out Dictionary<string, Variable> argsMap);

            script.MoveForwardIf(Constants.START_GROUP, Constants.SPACE);
            script.ParentOffset = script.Pointer;

            string body = Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP);

            Precompiler precompiler = new Precompiler(funcName, args, argsMap, body, script);
            precompiler.Compile(m_scriptInCSharp);

            CustomCompiledFunction customFunc = new CustomCompiledFunction(funcName, body, args, precompiler, argsMap, script);
            customFunc.ParentScript = script;
            customFunc.ParentOffset = script.ParentOffset;

            InterpreterInstance.RegisterFunction(funcName, customFunc, false /* not native */);
#endif
            return new Variable(funcName);
        }
    }

#if __ANDROID__ == false && __IOS__ == false

    class CustomCompiledFunction : CustomFunction
    {
        public bool ScriptInCSharp { get; set; }

        internal CustomCompiledFunction(string funcName,
                                        string body, string[] args,
                                        Precompiler precompiler,
                                        Dictionary<string, Variable> argsMap,
                                        ParsingScript script)
          : base(funcName, body, args, script)
        {
            m_precompiler = precompiler;
            m_argsMap = argsMap;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            script.MoveBackIf(Constants.START_GROUP);

            if (args.Count != m_args.Length)
            {
                throw new ArgumentException("Function [" + m_name + "] arguments mismatch: " +
                                    m_args.Length + " declared, " + args.Count + " supplied");
            }

            Variable result = Run(args);
            return result;
        }

        protected override Task<Variable> EvaluateAsync(ParsingScript script)
        {
            return Task.FromResult(Evaluate(script));
        }

        public Variable Run(List<Variable> args)
        {
            RegisterArguments(args);

            PrepareArgs(args, m_args, null, m_argsMap, out List<string> argsStr, out List<double> argsNum, out List<int> argsInt,
            out List<List<string>> argsArrStr, out List<List<double>> argsArrNum, out List<List<int>> argsArrInt,
            out List<Dictionary<string, string>> argsMapStr, out List<Dictionary<string, double>> argsMapNum, out List<Variable> argsVar);

            Variable result = Precompiler.AsyncMode ?
                m_precompiler.RunAsync(InterpreterInstance, argsStr, argsNum, argsInt, argsArrStr, argsArrNum, argsArrInt, argsMapStr, argsMapNum, argsVar, false) :
                m_precompiler.Run(InterpreterInstance, argsStr, argsNum, argsInt, argsArrStr, argsArrNum, argsArrInt, argsMapStr, argsMapNum, argsVar, false);

            InterpreterInstance.PopLocalVariables(m_stackLevel.Id);

            return result;
        }

        public static void PrepareArgs(List<Variable> args, string[] definedArgs, Variable[] defaultArgs, Dictionary<string, Variable> argsMap,
            out List<string> argsStr, out List<double> argsNum, out List<int> argsInt,
            out List<List<string>> argsArrStr, out List<List<double>> argsArrNum, out List<List<int>> argsArrInt,
            out List<Dictionary<string, string>> argsMapStr, out List<Dictionary<string, double>> argsMapNum, out List<Variable> argsVar)
        {
            argsStr = new List<string>();
            argsNum = new List<double>();
            argsInt = new List<int>();
            argsArrStr = new List<List<string>>();
            argsArrNum = new List<List<double>>();
            argsArrInt = new List<List<int>>();
            argsMapStr = new List<Dictionary<string, string>>();
            argsMapNum = new List<Dictionary<string, double>>();
            argsVar = new List<Variable>();

            for (int i = 0; i < definedArgs.Length; i++)
            {
                Variable typeVar = argsMap[definedArgs[i]];
                Variable arg = i < args.Count ? args[i] : defaultArgs[i];
                if (typeVar.Type == Variable.VarType.STRING)
                {
                    argsStr.Add(arg == null ? null : arg.AsString());
                }
                else if (typeVar.Type == Variable.VarType.NUMBER)
                {
                    argsNum.Add(arg.AsDouble());
                }
                else if (typeVar.Type == Variable.VarType.INT)
                {
                    argsInt.Add(arg.AsInt());
                }
                else if (typeVar.Type == Variable.VarType.ARRAY_STR)
                {
                    List<string> subArrayStr = new List<string>();
                    var tuple = arg.Tuple;
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subArrayStr.Add(tuple[j].AsString());
                    }
                    argsArrStr.Add(subArrayStr);
                }
                else if (typeVar.Type == Variable.VarType.ARRAY_NUM)
                {
                    List<double> subArrayNum = new List<double>();
                    var tuple = arg.Tuple;
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subArrayNum.Add(tuple[j].AsDouble());
                    }
                    argsArrNum.Add(subArrayNum);
                }
                else if (typeVar.Type == Variable.VarType.ARRAY_INT)
                {
                    List<int> subArrayInt = new List<int>();
                    var tuple = arg.Tuple;
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subArrayInt.Add(tuple[j].AsInt());
                    }
                    argsArrInt.Add(subArrayInt);
                }
                else if (typeVar.Type == Variable.VarType.MAP_STR)
                {
                    Dictionary<string, string> subMapStr = new Dictionary<string, string>();
                    var tuple = arg.Tuple;
                    var keys = arg.GetKeys();
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subMapStr.Add(keys[j], tuple[j].AsString());
                    }
                    argsMapStr.Add(subMapStr);
                }
                else if (typeVar.Type == Variable.VarType.MAP_NUM)
                {
                    Dictionary<string, double> subMapNum = new Dictionary<string, double>();
                    var tuple = arg.Tuple;
                    var keys = arg.GetKeys();
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subMapNum.Add(keys[j], tuple[j].AsDouble());
                    }
                    argsMapNum.Add(subMapNum);
                }
                else if (typeVar.Type == Variable.VarType.VARIABLE)
                {
                    argsVar.Add(arg);
                }
            }
        }

        Precompiler m_precompiler;
        Dictionary<string, Variable> m_argsMap;

        public Precompiler Precompiler { get { return m_precompiler; } }
    }
#endif
    public class WebRequestFunction : ParserFunction
    {
        static string[] s_allowedMethods = { "GET", "POST", "PUT", "DELETE", "HEAD", "OPTIONS", "TRACE" };

        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);
            string method = args[0].AsString().ToUpper();
            string uri = args[1].AsString();
            string load = Utils.GetSafeString(args, 2);
            string tracking = Utils.GetSafeString(args, 3);
            string onSuccess = Utils.GetSafeString(args, 4);
            string onFailure = Utils.GetSafeString(args, 5, onSuccess);
            string contentType = Utils.GetSafeString(args, 6, "application/x-www-form-urlencoded");
            Variable headers = Utils.GetSafeVariable(args, 7);
            int timeoutMs = Utils.GetSafeInt(args, 8, 10 * 1000);
            bool justFire = Utils.GetSafeInt(args, 9) > 0;

            if (!s_allowedMethods.Contains(method))
            {
                throw new ArgumentException("Unknown web request method: " + method);
            }

            var result = await ProcessWebRequestAsync(uri, method, load, onSuccess, onFailure,
                               tracking, contentType, headers, timeoutMs, justFire);
            return result;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 2, m_name);
            string method = args[0].AsString().ToUpper();
            string uri = args[1].AsString();
            string load = Utils.GetSafeString(args, 2);
            string tracking = Utils.GetSafeString(args, 3);
            string onSuccess = Utils.GetSafeString(args, 4);
            string onFailure = Utils.GetSafeString(args, 5, onSuccess);
            string contentType = Utils.GetSafeString(args, 6, "application/x-www-form-urlencoded");
            Variable headers = Utils.GetSafeVariable(args, 7);
            int timeoutMs = Utils.GetSafeInt(args, 8, 10 * 1000);
            bool justFire = Utils.GetSafeInt(args, 9) > 0;

            if (!s_allowedMethods.Contains(method))
            {
                throw new ArgumentException("Unknown web request method: " + method);
            }

            if (justFire)
            {
                Task.Run(() => ProcessWebRequest(uri, method, load, onSuccess, onFailure, tracking,
                                                 contentType, headers));
                return Variable.EmptyInstance;
            }
            var res = ProcessWebRequest(uri, method, load, onSuccess, onFailure, tracking,
                                                 contentType, headers);
            return res;
        }

        Variable ProcessWebRequest(string uri, string method, string load,
                                            string onSuccess, string onFailure,
                                            string tracking, string contentType,
                                            Variable headers)
        {
            Variable res = Variable.EmptyInstance;
            try
            {
                WebRequest request = WebRequest.CreateHttp(uri);
                request.Method = method;
                request.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(load))
                {
                    var bytes = Encoding.UTF8.GetBytes(load);
                    request.ContentLength = bytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                }

                if (headers != null && headers.Tuple != null)
                {
                    var keys = headers.GetKeys();
                    foreach (var header in keys)
                    {
                        var headerValue = headers.GetVariable(header).AsString();
                        request.Headers.Add(header, headerValue);
                    }
                }
                HttpWebResponse resp = request.GetResponse() as HttpWebResponse;
                string result;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    result = sr.ReadToEnd();
                }
                string responseCode = resp == null ? "" : resp.StatusCode.ToString();
                res = new Variable(result);
                if (!string.IsNullOrWhiteSpace(onSuccess))
                {
                    CustomFunction.Run(InterpreterInstance, onSuccess, new Variable(tracking), new Variable(responseCode), res);
                }
            }
            catch (Exception exc)
            {
                res = new Variable(exc.Message);
                if (!string.IsNullOrWhiteSpace(onFailure))
                {
                    CustomFunction.Run(InterpreterInstance, onFailure, new Variable(tracking),
                                       new Variable(""), res);
                }
            }
            return res;
        }

        async Task<Variable> ProcessWebRequestAsync(string uri, string method, string load,
                                            string onSuccess, string onFailure,
                                            string tracking, string contentType,
                                            Variable headers, int timeout,
                                            bool justFire = false)
        {
            try
            {
                WebRequest request = WebRequest.CreateHttp(uri);
                request.Method = method;
                request.ContentType = contentType;

                if (!string.IsNullOrWhiteSpace(load))
                {
                    var bytes = Encoding.UTF8.GetBytes(load);
                    request.ContentLength = bytes.Length;

                    using (var requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }
                }

                if (headers != null && headers.Tuple != null)
                {
                    var keys = headers.GetKeys();
                    foreach (var header in keys)
                    {
                        var headerValue = headers.GetVariable(header).AsString();
                        request.Headers.Add(header, headerValue);
                    }
                }

                Task<WebResponse> task = request.GetResponseAsync();
                var finishTask = FinishRequest(onSuccess, onFailure,
                                                tracking, task, timeout);
                if (justFire)
                {
                    return Variable.EmptyInstance;
                }
                var result = await finishTask;
                return result;
            }
            catch (Exception exc)
            {
                if (!string.IsNullOrWhiteSpace(onFailure))
                {
                    await CustomFunction.RunAsync(InterpreterInstance, onFailure, new Variable(tracking),
                                                  new Variable(""), new Variable(exc.Message));
                }
                return new Variable(exc.Message);
            }
        }

        async Task<Variable> FinishRequest(string onSuccess, string onFailure,
                                        string tracking, Task<WebResponse> responseTask,
                                        int timeoutMs)
        {
            string result = "";
            string method = onSuccess;
            HttpWebResponse response = null;
            Task timeoutTask = Task.Delay(timeoutMs);

            try
            {
                Task first = await Task.WhenAny(timeoutTask, responseTask);
                if (first == timeoutTask)
                {
                    await timeoutTask;
                    throw new Exception("Timeout waiting for response.");
                }

                response = await responseTask as HttpWebResponse;
                if ((int)response.StatusCode >= 400)
                {
                    throw new Exception(response.StatusDescription);
                }

                using (StreamReader sr = new StreamReader(response.GetResponseStream()))
                {
                    result = sr.ReadToEnd();
                }
            }
            catch (Exception exc)
            {
                result = exc.Message;
                method = onFailure;
            }

            var res = new Variable(result);

            string responseCode = response == null ? "" : response.StatusCode.ToString();
            if (!string.IsNullOrWhiteSpace(method))
            {
                await CustomFunction.RunAsync(InterpreterInstance, method, new Variable(tracking),
                                              new Variable(responseCode), res);
            }
            return res;
        }
    }

    class GetVariableFromJSONFunction : ParserFunction
    {
        static char[] SEP = "\",:]}".ToCharArray();

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);

            string json = args[0].AsString();

            Dictionary<int, int> d;
            json = Utils.ConvertToScript(InterpreterInstance, json, out d);

            var tempScript = script.GetTempScript(json);
            Variable result = ExtractValue(tempScript);
            return result;
        }

        static Variable ExtractObject(ParsingScript script)
        {
            Variable newValue = new Variable(Variable.VarType.ARRAY);

            while (script.StillValid() && (newValue.Count == 0 || script.Current == ','))
            {
                script.Forward();
                string key = Utils.GetToken(script, SEP);
                script.MoveForwardIf(':');

                Variable valueVar = ExtractValue(script);
                newValue.SetHashVariable(key, valueVar);
            }
            script.MoveForwardIf('}');

            return newValue;
        }

        static Variable ExtractArray(ParsingScript script)
        {
            Variable newValue = new Variable(Variable.VarType.ARRAY);

            while (script.StillValid() && (newValue.Count == 0 || script.Current == ','))
            {
                script.Forward();
                Variable addVariable = ExtractValue(script);
                newValue.AddVariable(addVariable);
            }
            script.MoveForwardIf(']');

            return newValue;
        }

        static Variable ExtractValue(ParsingScript script)
        {
            if (script.TryCurrent() == '{')
            {
                return ExtractObject(script);
            }
            if (script.TryCurrent() == '[')
            {
                return ExtractArray(script);
            }

            bool canBeNumeric = script.Current != '"';
            var token = Utils.GetToken(script, SEP);

            if (canBeNumeric && Utils.CanConvertToDouble(token, out double num))
            {
                return new Variable(num);
            }
            return new Variable(token);
        }
    }

    class IncludeFileSecure : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);

            string filename = args[0].AsString();
            string pathname = script.GetFilePath(filename);

            EncodeFileFunction.EncodeDecode(pathname, false);
            ParsingScript tempScript = script.GetIncludeFileScript(filename);
            string includeScript = tempScript.String;
            EncodeFileFunction.EncodeDecode(pathname, true);

            Variable result = null;
            if (script.Debugger != null)
            {
                result = script.Debugger.StepInIncludeIfNeeded(tempScript).Result;
            }

            while (tempScript.Pointer < includeScript.Length)
            {
                result = tempScript.Execute();
                tempScript.GoToNextStatement();
            }
            return result == null ? Variable.EmptyInstance : result;
        }
    }

    class CommandLineArgsFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            var cmdArgs = Environment.GetCommandLineArgs();

            List<Variable> results = new List<Variable>();
            for (int i = 0; i < cmdArgs.Length; i++)
            {
                string token = cmdArgs[i];
                results.Add(new Variable(token));
            }

            return new Variable(results);
        }
    }

    public class DownloadFileFunction : ParserFunction
    {
        static int s_timeout = 15 * 1000;
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name);
            string url = Utils.GetSafeString(args, 0);

            return Download(url);
        }
        protected override async Task<Variable> EvaluateAsync(ParsingScript script)
        {
            List<Variable> args = await script.GetFunctionArgsAsync();
            Utils.CheckArgs(args.Count, 1, m_name);
            string url = Utils.GetSafeString(args, 0);

            var result = await DownloadAsync(url);
            return result;
        }
        public static Variable Download(string requestUrl)
        {
            var awaiter = DownloadAsync(requestUrl).GetAwaiter();
            var result = awaiter.GetResult();

            return result;
        }
        public static async Task<Variable> DownloadAsync(string requestUrl)
        {
            var ext = Path.GetExtension(requestUrl);
            var localFilePath = Path.GetTempFileName() + ext;
            var httpClient = new HttpClient();
            var responseStream = await httpClient.GetStreamAsync(requestUrl).ConfigureAwait(false);
            var fileStream = new FileStream(localFilePath, FileMode.Create);
            responseStream.CopyTo(fileStream);
            fileStream.Close();
            responseStream.Close();
            httpClient.Dispose();

            return new Variable(localFilePath);
        }
    }
}