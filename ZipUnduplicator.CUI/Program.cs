using System.Text;
using Palmtree;
using Palmtree.IO.Console;

namespace ZipUnduplicator.CUI
{
    internal sealed class Program
    {
        private static int Main(string[] args)
        {
            TinyConsole.DefaultTextWriter = ConsoleTextWriterType.StandardError;
            var application = new UnduplicatorApplication(typeof(Program).Assembly.GetAssemblyFileNameWithoutExtension(), Encoding.UTF8);
            return application.Run(args);
        }
    }
}
