
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SplitAndMerge
{
    // Returns process info
    class PsInfoFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);
            string pattern = args[0].AsString();

            int MAX_PROC_NAME = 26;
            Interpreter.Instance.AppendOutput(Utils.GetLine(), true);
            Interpreter.Instance.AppendOutput(String.Format("{0} {1} {2} {3} {4} {5}",
              "Process Id".PadRight(15), "Process Name".PadRight(MAX_PROC_NAME),
              "Working Set".PadRight(15), "Virt Mem".PadRight(15),
              "Start Time".PadRight(15), "CPU Time".PadRight(25)), true);

            Process[] processes = Process.GetProcessesByName(pattern);
            List<Variable> results = new List<Variable>(processes.Length);
            for (int i = 0; i < processes.Length; i++)
            {
                Process pr = processes[i];
                int workingSet = (int)(((double)pr.WorkingSet64) / 1000000.0);
                int virtMemory = (int)(((double)pr.VirtualMemorySize64) / 1000000.0);
                string procTitle = pr.ProcessName + " " + pr.MainWindowTitle.Split(null)[0];
                string startTime = pr.StartTime.ToString();
                if (procTitle.Length > MAX_PROC_NAME)
                {
                    procTitle = procTitle.Substring(0, MAX_PROC_NAME);
                }
                string procTime = string.Empty;
                try
                {
                    procTime = pr.TotalProcessorTime.ToString().Substring(0, 11);
                }
                catch (Exception) { }

                results.Add(new Variable(
                  string.Format("{0,15} {1," + MAX_PROC_NAME + "} {2,15} {3,15} {4,15} {5,25}",
                    pr.Id, procTitle,
                    workingSet, virtMemory, startTime, procTime)));
                Interpreter.Instance.AppendOutput(results.Last().String, true);
            }
            Interpreter.Instance.AppendOutput(Utils.GetLine(), true);

            return new Variable(results);
        }
    }

    // Kills a process with specified process id
    class KillFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            Utils.CheckArgs(args.Count, 1, m_name, true);

            Variable id = args[0];
            Utils.CheckPosInt(id);

            int processId = (int)id.Value;
            try
            {
                Process process = Process.GetProcessById(processId);
                process.Kill();
                Interpreter.Instance.AppendOutput("Process " + processId + " killed", true);
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't kill process " + processId +
                  " (" + exc.Message + ")");
            }

            return Variable.EmptyInstance;
        }
    }

    // Starts running a new process, returning its ID
    class RunFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string processName = Utils.GetItem(script).String;
            Utils.CheckNotEmpty(processName, "processName");

            List<string> args = Utils.GetFunctionArgs(script);
            int processId = -1;

            try
            {
                Process pr = Process.Start(processName, string.Join("", args.ToArray()));
                processId = pr.Id;
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't start [" + processName + "]: " + exc.Message);
            }

            Interpreter.Instance.AppendOutput("Process " + processName + " started, id: " + processId, true);
            return new Variable(processId);
        }
    }

    // Starts running an "echo" server
    class ServerSocket : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Variable portRes = Utils.GetItem(script);
            Utils.CheckPosInt(portRes);
            int port = (int)portRes.Value;

            try
            {
                IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, port);

                Socket listener = new Socket(AddressFamily.InterNetwork,
                                    SocketType.Stream, ProtocolType.Tcp);

                listener.Bind(localEndPoint);
                listener.Listen(10);

                Socket handler = null;
                while (true)
                {
                    Interpreter.Instance.AppendOutput("Waiting for connections on " + port + " ...", true);
                    handler = listener.Accept();

                    // Data buffer for incoming data.
                    byte[] bytes = new byte[1024];
                    int bytesRec = handler.Receive(bytes);
                    string received = Encoding.UTF8.GetString(bytes, 0, bytesRec);

                    Interpreter.Instance.AppendOutput("Received from " + handler.RemoteEndPoint.ToString() +
                      ": [" + received + "]", true);

                    byte[] msg = Encoding.UTF8.GetBytes(received);
                    handler.Send(msg);

                    if (received.Contains("<EOF>"))
                    {
                        break;
                    }
                }

                if (handler != null)
                {
                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();
                }
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't start server: (" + exc.Message + ")");
            }

            return Variable.EmptyInstance;
        }
    }

    // Starts running an "echo" client
    class ClientSocket : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // Data buffer for incoming data.
            byte[] bytes = new byte[1024];

            List<Variable> args = script.GetFunctionArgs();

            Utils.CheckArgs(args.Count, 3, Constants.CONNECTSRV);
            Utils.CheckPosInt(args[1]);

            string hostname = args[0].String;
            int port = (int)args[1].Value;
            string msgToSend = args[2].String;

            if (string.IsNullOrWhiteSpace(hostname) || hostname.Equals("localhost"))
            {
                hostname = Dns.GetHostName();
            }

            try
            {
                IPHostEntry ipHostInfo = Dns.GetHostEntry(hostname);
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint remoteEP = new IPEndPoint(ipAddress, port);

                // Create a TCP/IP  socket.
                Socket sender = new Socket(AddressFamily.InterNetwork,
                        SocketType.Stream, ProtocolType.Tcp);

                sender.Connect(remoteEP);

                Interpreter.Instance.AppendOutput("Connected to [" + sender.RemoteEndPoint.ToString() + "]", true);

                byte[] msg = Encoding.UTF8.GetBytes(msgToSend);
                sender.Send(msg);

                // Receive the response from the remote device.
                int bytesRec = sender.Receive(bytes);
                string received = Encoding.UTF8.GetString(bytes, 0, bytesRec);
                Interpreter.Instance.AppendOutput("Received [" + received + "]", true);

                sender.Shutdown(SocketShutdown.Both);
                sender.Close();

            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't connect to server: (" + exc.Message + ")");
            }

            return Variable.EmptyInstance;
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

    // Prints passed list of arguments
    class PrintFunction : ParserFunction
    {
        internal PrintFunction(bool newLine = true)
        {
            m_newLine = newLine;
        }

        internal PrintFunction(ConsoleColor fgcolor)
        {
            m_fgcolor = fgcolor;
            m_changeColor = true;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            string output = string.Empty;
            for (int i = 0; i < args.Count; i++)
            {
                output += args[i].AsString();
            }

            output += (m_newLine ? Environment.NewLine : string.Empty);
            if (m_changeColor)
            {
                Utils.PrintColor(output, m_fgcolor);
            }
            else
            {
                Interpreter.Instance.AppendOutput(output);
            }

            if (script.Debugger != null)
            {
                script.Debugger.AddOutput(output, script);
            }

            return Variable.EmptyInstance;
        }

        private bool m_newLine = true;
        private bool m_changeColor = false;
        private ConsoleColor m_fgcolor;
    }

    class WriteToConsole : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            for (int i = 0; i < args.Count; i++)
            {
                Console.Write(args[i].AsString());
            }
            Console.WriteLine();

            return Variable.EmptyInstance;
        }
    }

    // Reads either a string or a number from the Console.
    class ReadConsole : ParserFunction
    {
        internal ReadConsole(bool isNumber = false)
        {
            m_isNumber = isNumber;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            script.Forward(); // Skip opening parenthesis.
            string line = Console.ReadLine();

            if (!m_isNumber)
            {
                return new Variable(line);
            }

            double number = Double.NaN;
            if (!Double.TryParse(line, out number))
            {
                throw new ArgumentException("Couldn't parse number [" + line + "]");
            }

            return new Variable(number);
        }

        private bool m_isNumber;
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

    // Returns current directory name
    class PwdFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string path = Directory.GetCurrentDirectory();
            Interpreter.Instance.AppendOutput(path, true);

            return new Variable(path);
        }
    }

    // Equivalent to cd.. on Windows: one directory up
    class Cd__Function : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string newDir = null;

            try
            {
                string pwd = Directory.GetCurrentDirectory();
                DirectoryInfo parent = Directory.GetParent(pwd);
                if (parent == null)
                {
                    throw new ArgumentException("No parent exists.");
                }
                newDir = parent.FullName;
                Directory.SetCurrentDirectory(newDir);
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't change directory: " + exc.Message);
            }

            return new Variable(newDir);
        }
    }

    // Changes directory to the passed one
    class CdFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            if (script.Substr().StartsWith(" .."))
            {
                script.Forward();
            }
            string newDir = Utils.GetStringOrVarValue(script);

            try
            {
                if (newDir == "..")
                {
                    string pwd = Directory.GetCurrentDirectory();
                    DirectoryInfo parent = Directory.GetParent(pwd);
                    if (parent == null)
                    {
                        throw new ArgumentException("No parent exists.");
                    }
                    newDir = parent.FullName;
                }
                if (newDir.Length == 0)
                {
                    newDir = Environment.GetEnvironmentVariable("HOME");
                }
                Directory.SetCurrentDirectory(newDir);

                newDir = Directory.GetCurrentDirectory();
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't change directory: " + exc.Message);
            }

            return new Variable(newDir);
        }
    }

    // Reads a file and returns all lines of that file as a "tuple" (list)
    class ReadCSCSFileFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string filename = Utils.GetStringOrVarValue(script);
            string[] lines = Utils.GetFileLines(filename);

            List<Variable> results = Utils.ConvertToResults(lines);
            Interpreter.Instance.AppendOutput("Read " + lines.Length + " line(s).", true);

            return new Variable(results);
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
            if (sep == "\\t")
            {
                sep = "\t";
            }
            var sepArray = sep.ToCharArray();
            string[] tokens = data.Split(sepArray);

            var option = Utils.GetSafeString(args, 2);

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
                    int index1 = source.IndexOf(argument);
                    int index2 = m_mode == Mode.BEETWEEN ? source.IndexOf(parameter, index1 + 1) :
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

    // View the contents of a text file
    class MoreFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string filename = Utils.GetStringOrVarValue(script);
            int size = Constants.DEFAULT_FILE_LINES;

            bool sizeAvailable = Utils.SeparatorExists(script);
            if (sizeAvailable)
            {
                Variable length = Utils.GetItem(script);
                Utils.CheckPosInt(length);
                size = (int)length.Value;
            }

            string[] lines = Utils.GetFileLines(filename, 0, size);
            List<Variable> results = Utils.ConvertToResults(lines);

            return new Variable(results);
        }
    }

    // View the last Constants.DEFAULT_FILE_LINES lines of a file
    class TailFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string filename = Utils.GetStringOrVarValue(script);
            int size = Constants.DEFAULT_FILE_LINES;

            bool sizeAvailable = Utils.SeparatorExists(script);
            if (sizeAvailable)
            {
                Variable length = Utils.GetItem(script);
                Utils.CheckPosInt(length);
                size = (int)length.Value;
            }

            string[] lines = Utils.GetFileLines(filename, -1, size);
            List<Variable> results = Utils.ConvertToResults(lines);

            return new Variable(results);
        }
    }

    // Append a line to a file
    class AppendLineFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string filename = Utils.GetStringOrVarValue(script);
            Variable line = Utils.GetItem(script);
            Utils.AppendFileText(filename, line.AsString() + Environment.NewLine);

            return Variable.EmptyInstance;
        }
    }

    // Apend a list of lines to a file
    class AppendLinesFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string filename = Utils.GetStringOrVarValue(script);
            string lines = Utils.GetLinesFromList(script);
            Utils.AppendFileText(filename, lines);

            return Variable.EmptyInstance;
        }
    }

    // Write a line to a file
    class WriteLineFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string filename = Utils.GetStringOrVarValue(script);
            Variable line = Utils.GetItem(script);
            Utils.WriteFileText(filename, line.AsString() + Environment.NewLine);

            return Variable.EmptyInstance;
        }
    }

    // Write a list of lines to a file
    class WriteLinesFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            //string filename = Utils.ResultToString(Utils.GetItem(script));
            string filename = Utils.GetStringOrVarValue(script);
            string lines = Utils.GetLinesFromList(script);
            Utils.WriteFileText(filename, lines);

            return Variable.EmptyInstance;
        }
    }

    // Find a string in files
    class FindstrFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string search = Utils.GetStringOrVarValue(script);
            List<string> patterns = Utils.GetFunctionArgs(script);

            bool ignoreCase = true;
            if (patterns.Count > 0 && patterns.Last().Equals("case"))
            {
                ignoreCase = false;
                patterns.RemoveAt(patterns.Count - 1);
            }
            if (patterns.Count == 0)
            {
                patterns.Add("*.*");
            }

            List<Variable> results = null;
            try
            {
                string pwd = Directory.GetCurrentDirectory();
                List<string> files = Utils.GetStringInFiles(pwd, search, patterns.ToArray(), ignoreCase);

                results = Utils.ConvertToResults(files.ToArray(), true);
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't find pattern: " + exc.Message);
            }

            return new Variable(results);
        }
    }

    // Find files having a given pattern
    class FindfilesFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<string> patterns = Utils.GetFunctionArgs(script);
            if (patterns.Count == 0)
            {
                patterns.Add("*.*");
            }

            List<Variable> results = null;
            try
            {
                string pwd = Directory.GetCurrentDirectory();
                List<string> files = Utils.GetFiles(pwd, patterns.ToArray());

                results = Utils.ConvertToResults(files.ToArray(), true);
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't list directory: " + exc.Message);
            }

            return new Variable(results);
        }
    }

    // Copy a file or a directiry
    class CopyFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string source = Utils.GetStringOrVarValue(script);
            script.MoveForwardIf(Constants.NEXT_ARG, Constants.SPACE);
            string dest = Utils.GetStringOrVarValue(script);

            string src = Path.GetFullPath(source);
            string dst = Path.GetFullPath(dest);

            List<Variable> srcPaths = Utils.GetPathnames(src);
            bool multipleFiles = srcPaths.Count > 1;
            if (dst.EndsWith("*"))
            {
                dst = dst.Remove(dst.Count() - 1);
            }
            if ((multipleFiles || Directory.Exists(src)) &&
                !Directory.Exists(dst))
            {
                try
                {
                    Directory.CreateDirectory(dst);
                }
                catch (Exception exc)
                {
                    throw new ArgumentException("Couldn't create [" + dst + "] :" + exc.Message);
                }

            }

            foreach (Variable srcPath in srcPaths)
            {
                string filename = Path.GetFileName(srcPath.String);
                //string dstPath  = Path.Combine(dst, filename);
                Utils.Copy(srcPath.String, dst);
            }

            return Variable.EmptyInstance;
        }
    }

    // Move a file or a directiry
    class MoveFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string source = Utils.GetStringOrVarValue(script);
            script.MoveForwardIf(Constants.NEXT_ARG, Constants.SPACE);
            string dest = Utils.GetStringOrVarValue(script);

            string src = Path.GetFullPath(source);
            string dst = Path.GetFullPath(dest);

            bool isFile = File.Exists(src);
            bool isDir = Directory.Exists(src);
            if (!isFile && !isDir)
            {
                throw new ArgumentException("[" + src + "] doesn't exist");
            }

            if (isFile && Directory.Exists(dst))
            {
                // If filename is missing in the destination file,
                // add it from the source.
                dst = Path.Combine(dst, Path.GetFileName(src));
            }

            try
            {
                if (isFile)
                {
                    File.Move(src, dst);
                }
                else
                {
                    Directory.Move(src, dst);
                }
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't copy: " + exc.Message);
            }

            return Variable.EmptyInstance;
        }
    }

    // Make a directory
    class MkdirFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string dirname = Utils.GetStringOrVarValue(script);
            try
            {
                Directory.CreateDirectory(dirname);
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't create [" + dirname + "] :" + exc.Message);
            }

            return Variable.EmptyInstance;
        }
    }

    // Delete a file or a directory
    class DeleteFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string pathname = Utils.GetStringOrVarValue(script);

            bool isFile = File.Exists(pathname);
            bool isDir = Directory.Exists(pathname);
            if (!isFile && !isDir)
            {
                throw new ArgumentException("[" + pathname + "] doesn't exist");
            }
            try
            {
                if (isFile)
                {
                    File.Delete(pathname);
                }
                else
                {
                    Directory.Delete(pathname, true);
                }
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't delete [" + pathname + "] :" + exc.Message);
            }

            return Variable.EmptyInstance;
        }
    }

    // Checks if a directory or a file exists
    class ExistsFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string pathname = Utils.GetStringOrVarValue(script);

            bool isFile = File.Exists(pathname);
            bool isDir = Directory.Exists(pathname);
            if (!isFile && !isDir)
            {
                throw new ArgumentException("[" + pathname + "] doesn't exist");
            }
            bool exists = false;
            try
            {
                if (isFile)
                {
                    exists = File.Exists(pathname);
                }
                else
                {
                    exists = Directory.Exists(pathname);
                }
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't delete [" + pathname + "] :" + exc.Message);
            }

            return new Variable(Convert.ToDouble(exists));
        }
    }

    // List files in a directory
    class DirFunction : ParserFunction, IArrayFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string dirname = (!script.StillValid() || script.Current == Constants.END_STATEMENT) ?
              Directory.GetCurrentDirectory() :
              Utils.GetToken(script, Constants.NEXT_OR_END_ARRAY);

            //List<Variable> results = Utils.GetPathnames(dirname);
            List<Variable> results = new List<Variable>();

            int index = dirname.IndexOf('*');
            if (index < 0 && !Directory.Exists(dirname) && !File.Exists(dirname))
            {
                throw new ArgumentException("Directory [" + dirname + "] doesn't exist");
            }

            string pattern = Constants.ALL_FILES;

            try
            {
                string dir = index < 0 ? Path.GetFullPath(dirname) : dirname;
                if (File.Exists(dir))
                {
                    FileInfo fi = new FileInfo(dir);
                    Interpreter.Instance.AppendOutput(Utils.GetPathDetails(fi, fi.Name), true);
                    results.Add(new Variable(fi.Name));
                    return new Variable(results);
                }
                // Special dealing if there is a pattern (only * is supported at the moment)
                if (index >= 0)
                {
                    pattern = Path.GetFileName(dirname);

                    if (index > 0)
                    {
                        string prefix = dirname.Substring(0, index);
                        DirectoryInfo di = Directory.GetParent(prefix);
                        dirname = di.FullName;
                    }
                    else
                    {
                        dirname = ".";
                    }
                }
                dir = Path.GetFullPath(dirname);
                // First get contents of the directory (unless there is a pattern)
                DirectoryInfo dirInfo = new DirectoryInfo(dir);

                if (pattern == Constants.ALL_FILES)
                {
                    Interpreter.Instance.AppendOutput(Utils.GetPathDetails(dirInfo, "."), true);
                    if (dirInfo.Parent != null)
                    {
                        Interpreter.Instance.AppendOutput(Utils.GetPathDetails(dirInfo.Parent, ".."), true);
                    }
                }

                // Then get contents of all of the files in the directory
                FileInfo[] fileNames = dirInfo.GetFiles(pattern);
                foreach (FileInfo fi in fileNames)
                {
                    try
                    {
                        Interpreter.Instance.AppendOutput(Utils.GetPathDetails(fi, fi.Name), true);
                        results.Add(new Variable(fi.Name));
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }

                // Then get contents of all of the subdirs in the directory
                DirectoryInfo[] dirInfos = dirInfo.GetDirectories(pattern);
                foreach (DirectoryInfo di in dirInfos)
                {
                    try
                    {
                        Interpreter.Instance.AppendOutput(Utils.GetPathDetails(di, di.Name), true);
                        results.Add(new Variable(di.Name));
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
            catch (Exception exc)
            {
                throw new ArgumentException("Couldn't list directory: " + exc.Message);
            }

            return new Variable(results);
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
            ParserFunction func = ParserFunction.GetFunction(varName);
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

    // Convert a string to the upper case
    class ToUpperFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Get the name of the variable.
            string varName = Utils.GetToken(script, Constants.END_ARG_ARRAY);
            Utils.CheckNotEmpty(script, varName, m_name);

            // 2. Get the current value of the variable.
            ParserFunction func = ParserFunction.GetFunction(varName);
            Variable currentValue = func.GetValue(script);

            // 3. Take either the string part if it is defined,
            // or the numerical part converted to a string otherwise.
            string arg = currentValue.AsString();

            Variable newValue = new Variable(arg.ToUpper());
            return newValue;
        }
    }

    // Convert a string to the lower case
    class ToLowerFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Get the name of the variable.
            string varName = Utils.GetToken(script, Constants.END_ARG_ARRAY);
            Utils.CheckNotEmpty(script, varName, m_name);

            // 2. Get the current value of the variable.
            ParserFunction func = ParserFunction.GetFunction(varName);
            Variable currentValue = func.GetValue(script);

            // 3. Take either the string part if it is defined,
            // or the numerical part converted to a string otherwise.
            string arg = currentValue.AsString();

            Variable newValue = new Variable(arg.ToLower());
            return newValue;
        }
    }

    // Get a substring of a string
    class SubstrFunction : ParserFunction, IStringFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string substring;

            // 1. Get the name of the variable.
            string varName = Utils.GetToken(script, Constants.NEXT_ARG_ARRAY);
            Utils.CheckNotEmpty(script, varName, m_name);

            // 2. Get the current value of the variable.
            ParserFunction func = ParserFunction.GetFunction(varName);
            Variable currentValue = func.GetValue(script);

            // 3. Take either the string part if it is defined,
            // or the numerical part converted to a string otherwise.
            string arg = currentValue.AsString();
            // 4. Get the initial index of the substring.
            Variable init = Utils.GetItem(script);
            Utils.CheckNonNegativeInt(init);

            // 5. Get the length of the substring if available.
            bool lengthAvailable = Utils.SeparatorExists(script);
            if (lengthAvailable)
            {
                Variable length = Utils.GetItem(script);
                Utils.CheckPosInt(length);
                if (init.Value + length.Value > arg.Length)
                {
                    throw new ArgumentException("The total substring length is larger than [" +
                      arg + "]");
                }
                substring = arg.Substring((int)init.Value, (int)length.Value);
            }
            else
            {
                substring = arg.Substring((int)init.Value);
            }
            Variable newValue = new Variable(substring);

            return newValue;
        }
    }

    // Get an index of a substring in a string. Return -1 if not found.
    class IndexOfFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            // 1. Get the name of the variable.
            string varName = Utils.GetToken(script, Constants.NEXT_ARG_ARRAY);
            Utils.CheckNotEmpty(script, varName, m_name);

            // 2. Get the current value of the variable.
            ParserFunction func = ParserFunction.GetFunction(varName);
            Variable currentValue = func.GetValue(script);

            // 3. Get the value to be looked for.
            Variable searchValue = Utils.GetItem(script);

            // 4. Apply the corresponding C# function.
            string basePart = currentValue.AsString();
            string search = searchValue.AsString();

            int result = basePart.IndexOf(search);
            return new Variable(result);
        }
    }

    class ThreadFunction : ParserFunction, INumericFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            string body = Utils.GetBodyBetween(script, Constants.START_ARG, Constants.END_ARG);

            Thread newThread = new Thread(ThreadFunction.ThreadProc);
            newThread.Start(body);

            int threadID = newThread.ManagedThreadId;
            return new Variable(threadID);
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

            lock (lockObject)
            {
                threadScript.ExecuteAll();
            }
            return Variable.EmptyInstance;
        }
    }

    class TimestampFunction : ParserFunction, IStringFunction
    {
        bool m_millis = false;
        public TimestampFunction(bool millis = false)
        {
            m_millis = millis;
        }
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            double timestamp = Utils.GetSafeDouble(args, 0);
            string strFormat = Utils.GetSafeString(args, 1, "yyyy/MM/dd HH:mm:ss.fff");
            Utils.CheckNotEmpty(strFormat, m_name);

            var dt = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            if (m_millis)
            {
                dt = dt.AddMilliseconds(timestamp);
            }
            else
            {
                dt = dt.AddSeconds(timestamp);
            }

            DateTime runtimeKnowsThisIsUtc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            DateTime localVersion = runtimeKnowsThisIsUtc.ToLocalTime();
            string when = localVersion.ToString(strFormat);
            return new Variable(when);
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
    class StopWatchFunction : ParserFunction, IStringFunction
    {
        static System.Diagnostics.Stopwatch m_stopwatch = new System.Diagnostics.Stopwatch();
        public enum Mode { START, STOP, ELAPSED, TOTAL_SECS, TOTAL_MS };

        Mode m_mode;
        public StopWatchFunction(Mode mode)
        {
            m_mode = mode;
        }

        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();

            if (m_mode == Mode.START)
            {
                m_stopwatch.Restart();
                return Variable.EmptyInstance;
            }

            string strFormat = Utils.GetSafeString(args, 0, "secs");
            string elapsedStr = "";
            double elapsed = -1.0;
            if (strFormat == "hh::mm:ss.fff")
            {
                elapsedStr = string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3}",
                  m_stopwatch.Elapsed.Hours, m_stopwatch.Elapsed.Minutes,
                  m_stopwatch.Elapsed.Seconds, m_stopwatch.Elapsed.Milliseconds);
            }
            else if (strFormat == "mm:ss.fff")
            {
                elapsedStr = string.Format("{0:D2}:{1:D2}.{2:D3}",
                    m_stopwatch.Elapsed.Minutes,
                    m_stopwatch.Elapsed.Seconds, m_stopwatch.Elapsed.Milliseconds);
            }
            else if (strFormat == "ss.fff")
            {
                elapsedStr = string.Format("{0:D2}.{1:D3}",
                    m_stopwatch.Elapsed.Seconds, m_stopwatch.Elapsed.Milliseconds);
            }
            else if (strFormat == "secs")
            {
                elapsed = Math.Round(m_stopwatch.Elapsed.TotalSeconds);
            }
            else if (strFormat == "ms")
            {
                elapsed = Math.Round(m_stopwatch.Elapsed.TotalMilliseconds);
            }

            if (m_mode == Mode.STOP)
            {
                m_stopwatch.Stop();
            }

            return elapsed >= 0 ? new Variable(elapsed) : new Variable(elapsedStr);
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
    class ClearConsole : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            Console.Clear();
            return Variable.EmptyInstance;
        }
    }

    class DebuggerFunction : ParserFunction
    {
        protected override Variable Evaluate(ParsingScript script)
        {
            List<Variable> args = script.GetFunctionArgs();
            int port = Utils.GetSafeInt(args, 0, 13337);
            DebuggerServer.StartServer(port);

            DebuggerServer.OnRequest += ProcessRequest;
            return Variable.EmptyInstance;
        }
        public void ProcessRequest(Debugger debugger, string request)
        {
            debugger.ProcessClientCommands(request);
        }
    }
}
