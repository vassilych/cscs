using CSCS.ConsoleApp;

namespace CscsScript
{
    internal class Program
    {
        static int Main(string[] args)
        {
            var consoleApp = new CscsConsoleApp();

            return consoleApp.Run(args);
        }
    }
}
