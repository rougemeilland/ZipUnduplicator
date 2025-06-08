using System;
using System.Text;
using Palmtree.Application;
using Palmtree.IO.Console;

namespace ZipUnduplicator.WindowsDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            TinyConsole.DefaultTextWriter = ConsoleTextWriterType.StandardError;
            var launcher = new ConsoleApplicationLauncher("zipundup", Encoding.UTF8);
            launcher.Launch(args);
        }
    }
}
