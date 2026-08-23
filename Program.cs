namespace StreamClipStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception exception)
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StreamClipStudio");
            Directory.CreateDirectory(folder);
            var logPath = Path.Combine(folder, "startup-error.log");
            File.WriteAllText(logPath, exception.ToString());
            MessageBox.Show(
                $"Stream Clip Studio could not start. Details were saved to:\n{logPath}",
                "Stream Clip Studio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
