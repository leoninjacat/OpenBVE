using System;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        int code = 0;
        try { OpenBve.TrafficInstallerApplication.Run(args); }
        catch (Exception ex)
        {
            code = 1;
            if (Array.Exists(args, a => a.StartsWith("--traffic-test="))) Console.Error.WriteLine(ex);
            else MessageBox.Show(ex.ToString(), "Assistente de IA — erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Environment.Exit(code);
    }
}
