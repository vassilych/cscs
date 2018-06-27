using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections.Concurrent;

//using SignalR.Hubs;

namespace SplitAndMerge
{
  public class DebuggerServer
  {
    public static Action<Debugger, string> OnRequest;

    static Debugger m_debugger;
    static TcpClient m_client;
    static NetworkStream m_stream;

    static BlockingCollection<string> m_queue = new BlockingCollection<string>();

    public static void StartServer(int port = 13337)
    {
      ThreadPool.QueueUserWorkItem(StartServerBlocked, port);
    }

    public static void StartServerBlocked(Object threadContext)
    {
      int port = (int)threadContext;
      IPAddress localAddr = IPAddress.Parse("127.0.0.1");

      TcpListener server = new TcpListener(localAddr, port);
      server.Start();

      while (true) {
        Console.Write("Waiting for a connection on {0}... ", port);

        // Perform a blocking call to accept requests.
        m_client = server.AcceptTcpClient();
        m_stream = m_client.GetStream();

        RunClient();
      }
    }

    static void StartProcessing(Object threadContext)
    {

#if UNITY_EDITOR
      // Do nothing: ProcessQueue() will be called from the Unity Main Thread
# else
      try 
      {
          ProcessQueue();
      } catch(Exception exc) {
        Console.Write ("Connection is dead: {0}", exc.Message);
      }
#endif
    }

    public static void ProcessQueue()
    {
      string data;
#if UNITY_EDITOR
      while(m_queue.TryTake(out data))
      { // Exit as soon as done processing.
#else
      while (true)
      { // A blocking call.
        data = m_queue.Take();
#endif
        if (OnRequest != null) {
          OnRequest?.Invoke(m_debugger, data);
        } else {
          m_debugger.ProcessClientCommands(data);
        }

#if __ANDROID__
        MainActivity.TheView.RunOnUiThread(() => {
          m_debugger.ProcessClientCommands(data);
        });
#endif
      }
    }

    static void RunClient()
    {
      Byte [] bytes = new Byte [256];
      string data = null;
      Console.WriteLine ("Starting client {0}", m_client.Client.RemoteEndPoint);

      ThreadPool.QueueUserWorkItem(StartProcessing, null);

      #if UNITY_EDITOR == false && __ANDROID__ == false && __IOS__ == false
      Interpreter.Instance.Init();
      #endif

      m_debugger = new Debugger ();
      Debugger.OnResult += SendBack;

      int i;
      try {
        while ((i = m_stream.Read (bytes, 0, bytes.Length)) != 0) {
          data = System.Text.Encoding.UTF8.GetString (bytes, 0, i);
          m_queue.Add(data);
          //ThreadPool.QueueUserWorkItem(ThreadPoolCallback, data);
        }
      } catch (Exception exc) {
        Console.Write ("Client disconnected: {0}", exc.Message);
      }

      Debugger.OnResult -= SendBack;

      // Shutdown and end connection
      Console.Write ("Closed connection.");
      m_client.Close();
    }

    static void ThreadPoolCallback(Object threadContext)  
    {
      m_debugger.ProcessClientCommands((string)threadContext);
    }
    static void SendBack (string str)
    {
      byte [] msg = System.Text.Encoding.UTF8.GetBytes (str);
      try {
        m_stream.Write (msg, 0, msg.Length);
        m_stream.Flush ();
      } catch (Exception exc) {
        Console.Write ("Client disconnected: {0}", exc.Message);
        return;
      }
    }
  }
}
