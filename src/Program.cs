namespace KartotekaKontrahentow;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string cs;
        try
        {
            cs = Konfiguracja.ConnectionString();
        }
        catch (InvalidOperationException e)
        {
            // Brak konfiguracji to nie jest powód do wyrzucania stosu wywołań
            // ani do cichego sięgania po wartość domyślną z kodu.
            if (args.Contains("--smoke")) Console.Error.WriteLine(e.Message);
            else MessageBox.Show(e.Message, "Brak konfiguracji", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        // Tryb kontrolny: sprawdza bazę i zewnętrzne API bez otwierania okna.
        // Dzięki temu da się zweryfikować logikę na maszynie bez pulpitu (CI) —
        // i dzięki temu wiadomo, że ten kod naprawdę działa, a nie tylko się kompiluje.
        if (args.Contains("--smoke"))
            return SmokeTest.UruchomAsync(cs).GetAwaiter().GetResult();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(new KontrahentRepository(cs)));
        return 0;
    }
}
