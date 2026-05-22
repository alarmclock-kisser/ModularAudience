namespace ModularAudience.Forms
{
    internal static class Program
    {
        /// <summary>+-
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new WindowMain());

        }
    }
}