using RockfaxDesk;

internal static class Program
{
    /// <summary>Startup args, also used by the screenshot/dev hooks in MainForm.OnShown.</summary>
    internal static string[] StartupArgs { get; private set; } = Array.Empty<string>();

    [STAThread]
    private static int Main(string[] args)
    {
        StartupArgs = args;
        if (args.Contains("--self-test"))
        {
            // Keep the console return path simple: run the async self-test to completion.
            return SelfTest.RunAsync().GetAwaiter().GetResult();
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
