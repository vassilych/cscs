//#define MAIN_THREAD_CHECK

using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SplitAndMerge
{
    public class DebuggerServer
    {
        public static bool DebuggerAttached { set; get; }

        static TcpListener s_server;
        static bool s_serverStarted;

        static CancellationTokenSource s_cancelTokenSource = new CancellationTokenSource();

        static BlockingCollection<string> m_queue = new BlockingCollection<string>();
        public static BlockingCollection<string> Queue { get { return m_queue; } }

        static List<DebuggerClient> m_clients = new List<DebuggerClient>();

        static public string AllowedClients
        {
            get;
            set;
        }
        static public string BaseDirectory
        {
            get;
            set;
        }

        public static string StartServer(int port = 13337, bool allowRemoteConnections = false)
        {
            if (s_serverStarted)
            {
                return "OK";
            }

            if (allowRemoteConnections && string.IsNullOrWhiteSpace(DebuggerServer.AllowedClients))
            {
                Console.Write("AllowedClients property is not set. Cannot allow remote connections.");
                allowRemoteConnections = false;
            }

            IPAddress localAddr = allowRemoteConnections ? IPAddress.Any : IPAddress.Parse("127.0.0.1");
            Console.Write("Starting a server on {0}:{1}... ", localAddr.ToString(), port);

            s_server = new TcpListener(localAddr, port);
            try
            {
                s_server.Start();
            }
            catch (Exception exc)
            {
                string err = string.Format("Exception starting server on port {0}: {1}", port, exc.Message);
                Console.Write(err);
                return err;
            }
            DebuggerAttached = true;
            s_cancelTokenSource = new CancellationTokenSource();

            ThreadPool.QueueUserWorkItem(StartServerBlocked);
            return "OK";
        }

        public static void StopServer()
        {
            if (s_server != null)
            {
                s_server.Stop();
            }

            s_cancelTokenSource.Cancel();
            DebuggerAttached = false;
        }

        static void StartServerBlocked(Object threadContext)
        {
            if (s_serverStarted)
            {
                return;
            }
            s_serverStarted = true;
            DebuggerAttached = true;

            Debugger.OnResult += SendBack;
            Debugger.OnSendFile += SendFile;
            ThreadPool.QueueUserWorkItem(StartProcessing, null);

            while (DebuggerAttached)
            {
                Console.Write("Waiting for a connection... ");

                try
                {
                    // Perform a blocking call to accept requests.
                    TcpClient socket = s_server.AcceptTcpClient();
                    DebuggerClient client = new DebuggerClient();
                    m_clients.Add(client);

                    ThreadPool.QueueUserWorkItem(o => client.RunClient(socket));
                }
                catch (Exception exc)
                {
                    string err = string.Format("Exception running server: {0}", exc.Message);
                    Console.Write(err);
                }

                Thread.Sleep(1000);
            }

            Console.Write("Stopped listening for requests");
            s_serverStarted = false;
        }

        static void SendBack(string str, bool sendLength = true)
        {
            int i = 0;
            while (i < m_clients.Count)
            {
                DebuggerClient client = m_clients[i];
                if (!client.Connected)
                {
                    m_clients.RemoveAt(i);
                    continue;
                }
                client.SendBack(str, sendLength);
                i++;
            }
        }
        static void SendFile(string filename, string destination)
        {
            int i = 0;
            while (i < m_clients.Count)
            {
                DebuggerClient client = m_clients[i];
                if (!client.Connected)
                {
                    m_clients.RemoveAt(i);
                    continue;
                }
                client.SendFile(filename, destination);
                i++;
            }
        }

        static void StartProcessing(Object threadContext)
        {
#if MAIN_THREAD_CHECK
            System.Timers.Timer runTimer = new System.Timers.Timer(0.1 * 1000);
            bool processing = false;
            runTimer.Elapsed += (sender, e) =>
            {
                if (processing || !DebuggerAttached)
                {
                    return;
                }
                processing = true;
                scripting.iOS.AppDelegate.GetCurrentController().InvokeOnMainThread(() =>
                {
                    ProcessQueue();
                });
                processing = false;
            };
            runTimer.Start();
#elif UNITY_EDITOR || UNITY_STANDALONE
      // Do nothing: ProcessQueue() will be called from the Unity Main Thread
#else
            try
            {
                ProcessQueue();
            }
            catch (Exception exc)
            {
                Console.Write("Exception while waiting for requests: {0}", exc);
            }
#endif
        }

        public static async Task ProcessQueue()
        {
            string data = "";
#if UNITY_EDITOR || UNITY_STANDALONE || MAIN_THREAD_CHECK
            while (m_queue.TryTake(out data))
            { // Exit as soon as done processing.
                if (!DebuggerAttached)
                {
                    return;
                }
                await Debugger.MainInstance?.ProcessClientCommands(data);
#else
            while (DebuggerAttached)
            { // A blocking call.
                try
                {
                    data = m_queue.Take(s_cancelTokenSource.Token);
                }
                catch (Exception)
                {
                    DebuggerAttached = false;
                }
                if (!DebuggerAttached)
                {
                    return;
                }

                ThreadPool.QueueUserWorkItem(RunRequestBlocked, data);
#endif
            }
        }

        public static void RunRequestBlocked(Object threadContext)
        {
            string data = (string)threadContext;
            Debugger.MainInstance?.ProcessClientCommands(data);
        }
    }

    public class DebuggerClient
    {
        public bool Connected { private set; get; }

        Debugger m_debugger;
        TcpClient m_client;
        NetworkStream m_stream;

        string m_remoteHost;

        public void RunClient(Object threadContext)
        {
            m_client = (TcpClient)threadContext;
            m_stream = m_client.GetStream();

            string remoteAddress = m_client.Client.RemoteEndPoint.ToString();
            var items = remoteAddress.Split(':');
            m_remoteHost = items[0];

            if (m_remoteHost != "127.0.0.1" && DebuggerServer.AllowedClients != "*" &&
                !DebuggerServer.AllowedClients.Contains(m_remoteHost))
            {
                Console.WriteLine("Host [" + m_remoteHost + "] is not allowed.");
                return;
            }

            Connected = true;

            Byte[] bytes = new Byte[4096];
            string data = null;
            Console.WriteLine("Starting client {0}", m_client.Client.RemoteEndPoint);

            m_debugger = new Debugger();

            int i;
            try
            {
                while ((i = m_stream.Read(bytes, 0, bytes.Length)) != 0 && Connected)
                {
                    data = System.Text.Encoding.UTF8.GetString(bytes, 0, i);
                    string rest = "";
                    DebuggerUtils.DebugAction action = DebuggerUtils.StringToAction(data, ref rest);

                    if (action == DebuggerUtils.DebugAction.BYE)
                    {
                        break;
                    }
                    else if (m_debugger.CanProcess(action))
                    {
                        ThreadPool.QueueUserWorkItem(DebuggerServer.RunRequestBlocked, data);
                    }
                    else
                    {
                        DebuggerServer.Queue.Add(data);
                    }
                }
            }
            catch (Exception exc)
            {
                Console.Write("Client disconnected: {0}", exc.Message);
            }

            // Shutdown and end connection
            Console.Write("Closed connection.");
            Disconnect();
        }

        void Disconnect()
        {
            Connected = false;
            m_stream.Close();
            m_client.Close();
            m_stream.Dispose();
        }

        public bool SendBack(string str, bool sendLength = true)
        {
            byte[] msg = System.Text.Encoding.UTF8.GetBytes(str);
            try
            {
                if (sendLength)
                {
                    byte[] lenMsg = System.Text.Encoding.UTF8.GetBytes(msg.Length + "\n");
                    m_stream.Write(lenMsg, 0, lenMsg.Length);
                }
                m_stream.Write(msg, 0, msg.Length);
                m_stream.Flush();
            }
            catch (Exception exc)
            {
                Console.Write("Client disconnected: {0}", exc.Message);
                Disconnect();
                return false;
            }

            return true;
        }

        public void SendFile(string source, string destination)
        {
            if (m_remoteHost.Contains("127.0.0.1"))
            {
                throw new ArgumentException("Cannot send files to a local host");
            }
            if (string.IsNullOrWhiteSpace(DebuggerServer.BaseDirectory))
            {
                throw new ArgumentException("Debugger Base Directory is not set.");
            }
            if (source.Contains("..") || source.Contains(":"))
            {
                throw new ArgumentException("The source file cannot have [..] or [:] characters.");
            }

            string filename = Path.Combine(DebuggerServer.BaseDirectory, source);
            if (!File.Exists(filename))
            {
                throw new ArgumentException("File [" + filename + "] not found.");
            }

            byte[] fileBytes = File.ReadAllBytes(filename);

            if (destination.EndsWith("/") || destination.EndsWith("\\"))
            {
                destination += Path.GetFileName(source);
            }

            string result = "send_file\n";
            result += new FileInfo(filename).Length + "\n";
            result += destination + "\n";

            byte[] msg = System.Text.Encoding.UTF8.GetBytes(result);
            try
            {
                m_stream.Write(msg, 0, msg.Length);
                m_stream.Flush();

                m_stream.Write(fileBytes, 0, fileBytes.Length);
                m_stream.Flush();
                Thread.Sleep(100); // Let the client get the file.
            }
            catch (Exception exc)
            {
                Console.Write("Client disconnected while sending data: " + exc.Message);
            }
        }
    }

    public class DebuggerUtils
    {
        public enum DebugAction { NONE, FILE, NEXT, CONTINUE, STEP_IN, STEP_OUT,
            SET_BP, VARS, STACK, ALL, GET_FILE, REPL, _REPL, END, BYE };

        public static DebugAction StringToAction(string str, ref string rest)
        {
            int index = str.IndexOf('|');
            if (index >= 0)
            {
                rest = index < str.Length - 1 ? str.Substring(index + 1) : "";
                str = str.Substring(0, index);
            }

            switch (str)
            {
                case "next": return DebugAction.NEXT;
                case "continue": return DebugAction.CONTINUE;
                case "stepin": return DebugAction.STEP_IN;
                case "stepout": return DebugAction.STEP_OUT;
                case "file": return DebugAction.FILE;
                case "setbp": return DebugAction.SET_BP;
                case "vars": return DebugAction.VARS;
                case "stack": return DebugAction.STACK;
                case "all": return DebugAction.ALL;
                case "get_file": return DebugAction.GET_FILE;
                case "repl": return DebugAction.REPL;
                case "_repl": return DebugAction._REPL;
                case "bye": return DebugAction.BYE;
            }

            return DebugAction.NONE;
        }
        public static string ResponseMainToken(DebugAction action)
        {
            switch (action)
            {
                case DebugAction.NEXT:
                case DebugAction.CONTINUE:
                case DebugAction.STEP_IN:
                case DebugAction.STEP_OUT: return "next\n";
                case DebugAction.REPL: return "repl\n";
                case DebugAction._REPL: return "_repl\n";
                case DebugAction.FILE: return "file\n";
                case DebugAction.SET_BP: return "set_bp\n";
                case DebugAction.END: return "end\n";
            }

            return "none\n";
        }

        public static async Task<Variable> Execute(ParsingScript script)
        {
            char[] toArray = Constants.END_PARSE_ARRAY;
            Variable result = null;
            Exception exception = null;
#if UNITY_EDITOR || UNITY_STANDALONE || MAIN_THREAD_CHECK
            // Do nothing: already on the main thread
#elif __ANDROID__
            scripting.Droid.MainActivity.TheView.RunOnUiThread(() =>
            {
#elif __IOS__
            scripting.iOS.AppDelegate.GetCurrentController().InvokeOnMainThread(() =>
            {
#else
#endif
                try
                {
#if __IOS__  || __ANDROID__
                    result = script.Execute(toArray);
#else
                    result = await script.ExecuteAsync(toArray);
#endif
                }
                catch (ParsingException exc)
                {
                    exception = exc;
                }

#if UNITY_EDITOR || UNITY_STANDALONE || MAIN_THREAD_CHECK
            // Do nothing: already on the main thread or main thread is not required
#elif __ANDROID__ || __IOS__
            });
#endif
            if (exception != null)
            {
                throw exception;
            }
            if (result.Type == Variable.VarType.QUIT)
            {
                DebuggerServer.StopServer();
            }
            return result;
        }
    }
}
