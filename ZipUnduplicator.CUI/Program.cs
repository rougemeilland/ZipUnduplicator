using System.Text;
using Palmtree;

namespace ZipUnduplicator.CUI
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            var application = new UnduplicatorApplication(typeof(Program).Assembly.GetAssemblyFileNameWithoutExtension(), Encoding.UTF8);
            return application.Run(args);
        }
    }
}
