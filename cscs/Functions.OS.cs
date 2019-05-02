
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    // Prints passed list of arguments
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

    // Returns how much processor time has been spent on the current process
    class ProcessorTimeFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Process pr = Process.GetCurrentProcess();
            TimeSpan ts = pr.TotalProcessorTime;

            return new Variable(ts.TotalMilliseconds);
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
            ParserFunction func = ParserFunction.GetFunction(varName, script);
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

            ParserFunction.AddGlobalOrLocalVariable(varName, new GetVarFunction(newValue));

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
            Utils.CheckPosInt(sleepms);

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
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            string strFormat = Utils.GetSafeString(args, 0, "HH:mm:ss.fff");
            Utils.CheckNotEmpty(strFormat, m_name);

            string when = DateTime.Now.ToString(strFormat);
            return new Variable(when);
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
                res = DebuggerServer.StartServer(port);
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
            int timeoutMs = Utils.GetSafeInt(args, 8, 15 * 1000);
            bool justFire = Utils.GetSafeInt(args, 9) > 0;

            if (!s_allowedMethods.Contains(method))
            {
                throw new ArgumentException("Unknown web request method: " + method);
            }

            await ProcessWebRequest(uri, method, load, onSuccess, onFailure, tracking, contentType, headers, timeoutMs, justFire);

            return Variable.EmptyInstance;
        }

        static async Task ProcessWebRequest(string uri, string method, string load,
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

        protected override Variable Evaluate(ParsingScript script)
        {
            return EvaluateAsync(script).Result;
        }
    }
}
