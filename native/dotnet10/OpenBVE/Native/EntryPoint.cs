using System;
namespace OpenBve
{
    internal static partial class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (Environment.GetEnvironmentVariable("OPENBVE_TRACE_STARTUP") == "1")
                AppDomain.CurrentDomain.FirstChanceException += (_, e) => Console.Error.WriteLine(e.Exception);
            int code;
            try { Run(args); code = Environment.ExitCode; }
            catch (Exception error) { Console.Error.WriteLine(error); code = 1; }
            // OpenTK's Windows input driver may retain foreground threads after
            // the original launcher has closed. Run already completes shutdown;
            // terminate remaining native input workers at the process boundary.
            if (OperatingSystem.IsWindows()) Environment.Exit(code);
            return code;
        }
    }
}
