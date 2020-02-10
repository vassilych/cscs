using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
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

        public static void AddOutput(List<Variable> args, ParsingScript script = null,
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
            Interpreter.Instance.AppendOutput(output);

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
        internal enum DataMode { ADD, SUBSCRIBE, SEND};

        DataMode      m_mode;

        static string s_method;
        static string s_tracking;
        static bool   s_updateImmediate = false;

        static StringBuilder s_data = new StringBuilder();

        internal DataFunction(DataMode mode = DataMode.ADD)
        {
            m_mode = mode;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            string result = "";

            switch(m_mode)
            {
                case DataMode.ADD:
                    Collect(args);
                    break;
                case DataMode.SUBSCRIBE:
                    Subscribe(args);
                    break;
                case DataMode.SEND:
                    result = SendData(s_data.ToString());
                    s_data.Clear();
                    break;
            }

            return new Variable(result);
        }

        public void Subscribe(List<Variable> args)
        {
            s_data.Clear();

            s_method          = Utils.GetSafeString(args, 0);
            s_tracking        = Utils.GetSafeString(args, 1);
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

        public string SendData(string data)
        {
            if (!string.IsNullOrWhiteSpace(s_method))
            {
                CustomFunction.Run(s_method, new Variable(s_tracking),
                                   new Variable(data));
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

        static public Variable Tokenize(string data, string sep, string option = "")
        {
            if (sep == "\\t")
            {
                sep = "\t";
            }

            string[] tokens;
            var sepArray = sep.ToCharArray();
            if (sepArray.Count() == 1)
            {
                tokens = data.Split(sepArray);
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
            ParserFunction func = ParserFunction.GetVariable(varName, script);
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

            ParserFunction.AddGlobalOrLocalVariable(varName, new GetVarFunction(newValue), script);

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

    class ThreadFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string body = script.TryPrev() == Constants.START_GROUP ?
                          Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP) :
                          Utils.GetBodyBetween(script, Constants.START_ARG, Constants.END_ARG);
            ThreadPool.QueueUserWorkItem(ThreadProc, body);
            return Variable.EmptyInstance;
        }

        static void ThreadProc(Object stateInfo)
        {
            string body = (string)stateInfo;
            ParsingScript threadScript = new ParsingScript(body);
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
            ParsingScript threadScript = new ParsingScript(body);

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

            await ProcessWebRequestAsync(uri, method, load, onSuccess, onFailure, tracking, contentType, headers, timeoutMs, justFire);

            return Variable.EmptyInstance;
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

            Task.Run(() => ProcessWebRequest(uri, method, load, onSuccess, onFailure, tracking,
                                             contentType, headers));

            return Variable.EmptyInstance;
        }

        static void ProcessWebRequest(string uri, string method, string load,
                                            string onSuccess, string onFailure,
                                            string tracking, string contentType,
                                            Variable headers)
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
                HttpWebResponse resp = request.GetResponse() as HttpWebResponse;
                string result;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                {
                    result = sr.ReadToEnd();
                }
                string responseCode = resp == null ? "" : resp.StatusCode.ToString();
                CustomFunction.Run(onSuccess, new Variable(tracking),
                                   new Variable(responseCode), new Variable(result));
            }
            catch (Exception exc)
            {
                CustomFunction.Run(onFailure, new Variable(tracking),
                                   new Variable(""),  new Variable(exc.Message));
            }
        }

        static async Task ProcessWebRequestAsync(string uri, string method, string load,
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
                Task finishTask = FinishRequest(onSuccess, onFailure,
                                                tracking, task, timeout);
                if (justFire)
                {
                    return;
                }
                await finishTask;
            }
            catch (Exception exc)
            {
                await CustomFunction.RunAsync(onFailure, new Variable(tracking),
                                              new Variable(""),  new Variable(exc.Message));
            }
        }

        static async Task FinishRequest(string onSuccess, string onFailure,
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

            string responseCode = response == null ? "" : response.StatusCode.ToString();
            await CustomFunction.RunAsync(method, new Variable(tracking),
                                          new Variable(responseCode), new Variable(result));
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
            json = Utils.ConvertToScript(json, out d);

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
            var token = Utils.GetToken(script, SEP);
            return new Variable(token);
        }
    }

    class RegexFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            Utils.CheckArgs(args.Count, 2, m_name);
            string pattern = Utils.GetSafeString(args, 0);
            string text    = Utils.GetSafeString(args, 1);

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

            Dictionary<string, Variable> argsMap;
            string[] args = Utils.GetCompiledFunctionSignature(script, out argsMap);

            script.MoveForwardIf(Constants.START_GROUP, Constants.SPACE);
            int parentOffset = script.Pointer;

            string body = Utils.GetBodyBetween(script, Constants.START_GROUP, Constants.END_GROUP);

            Precompiler precompiler = new Precompiler(funcName, args, argsMap, body, script);
            precompiler.Compile(m_scriptInCSharp);

            CustomCompiledFunction customFunc = new CustomCompiledFunction(funcName, body, args, precompiler, argsMap, script);
            customFunc.ParentScript = script;
            customFunc.ParentOffset = parentOffset;

            ParserFunction.RegisterFunction(funcName, customFunc, false /* not native */);
#endif
            return new Variable(funcName);
        }
    }

#if __ANDROID__ == false && __IOS__ == false
    class CustomCompiledFunction : CustomFunction
    {
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

            List<string> argsStr = new List<string>();
            List<double> argsNum = new List<double>();
            List<List<string>> argsArrStr = new List<List<string>>();
            List<List<double>> argsArrNum = new List<List<double>>();
            List<Dictionary<string, string>> argsMapStr = new List<Dictionary<string, string>>();
            List<Dictionary<string, double>> argsMapNum = new List<Dictionary<string, double>>();
            List<Variable> argsVar = new List<Variable>();

            for (int i = 0; i < m_args.Length; i++)
            {
                Variable typeVar = m_argsMap[m_args[i]];
                if (typeVar.Type == Variable.VarType.STRING)
                {
                    argsStr.Add(args[i].AsString());
                }
                else if (typeVar.Type == Variable.VarType.NUMBER)
                {
                    argsNum.Add(args[i].AsDouble());
                }
                else if (typeVar.Type == Variable.VarType.ARRAY_STR)
                {
                    List<string> subArrayStr = new List<string>();
                    var tuple = args[i].Tuple;
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subArrayStr.Add(tuple[j].AsString());
                    }
                    argsArrStr.Add(subArrayStr);
                }
                else if (typeVar.Type == Variable.VarType.ARRAY_NUM)
                {
                    List<double> subArrayNum = new List<double>();
                    var tuple = args[i].Tuple;
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subArrayNum.Add(tuple[j].AsDouble());
                    }
                    argsArrNum.Add(subArrayNum);
                }
                else if (typeVar.Type == Variable.VarType.MAP_STR)
                {
                    Dictionary<string, string> subMapStr = new Dictionary<string, string>();
                    var tuple = args[i].Tuple;
                    var keys = args[i].GetKeys();
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subMapStr.Add(keys[j], tuple[j].AsString());
                    }
                    argsMapStr.Add(subMapStr);
                }
                else if (typeVar.Type == Variable.VarType.MAP_NUM)
                {
                    Dictionary<string, double> subMapNum = new Dictionary<string, double>();
                    var tuple = args[i].Tuple;
                    var keys = args[i].GetKeys();
                    for (int j = 0; j < tuple.Count; j++)
                    {
                        subMapNum.Add(keys[j], tuple[j].AsDouble());
                    }
                    argsMapNum.Add(subMapNum);
                }
                else if (typeVar.Type == Variable.VarType.VARIABLE)
                {
                    argsVar.Add(args[i]);
                }
            }

            Variable result = Precompiler.AsyncMode ?
                m_precompiler.RunAsync(argsStr, argsNum, argsArrStr, argsArrNum, argsMapStr, argsMapNum, argsVar, false) :
                m_precompiler.Run(argsStr, argsNum, argsArrStr, argsArrNum, argsMapStr, argsMapNum, argsVar, false);

            ParserFunction.PopLocalVariables(m_stackLevel.Id);

            return result;
        }

        Precompiler m_precompiler;
        Dictionary<string, Variable> m_argsMap;

        public Precompiler Precompiler { get { return m_precompiler; } }
    }
#endif
}
