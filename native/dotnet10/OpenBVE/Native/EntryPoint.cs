using System;
namespace OpenBve
{
    internal static partial class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { Run(args); return Environment.ExitCode; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
