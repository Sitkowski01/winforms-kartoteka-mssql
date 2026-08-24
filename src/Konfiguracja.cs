namespace KartotekaKontrahentow;

/// <summary>
/// Connection string budowany z konfiguracji, nigdy z wartości wpisanej w kod.
///
/// Kolejność źródeł:
///   1. KARTOTEKA_CS            — gotowy connection string (produkcja, CI)
///   2. plik .env obok repo     — wygoda przy pracy lokalnej, plik jest w .gitignore
///   3. zmienne środowiskowe    — MSSQL_HOST / MSSQL_DB / MSSQL_USER / MSSQL_PASSWORD
///
/// Jeśli hasła nie ma w żadnym z nich, aplikacja mówi to wprost i kończy pracę,
/// zamiast po cichu sięgać po wartość domyślną.
/// </summary>
internal static class Konfiguracja
{
    public static string ConnectionString()
    {
        WczytajPlikEnv();

        var gotowy = Environment.GetEnvironmentVariable("KARTOTEKA_CS");
        if (!string.IsNullOrWhiteSpace(gotowy)) return gotowy;

        var host = Zmienna("MSSQL_HOST", "localhost,1433");
        var baza = Zmienna("MSSQL_DB", "Kartoteka");
        var user = Zmienna("MSSQL_USER", "sa");
        var haslo = Environment.GetEnvironmentVariable("MSSQL_PASSWORD");

        if (string.IsNullOrWhiteSpace(haslo))
            throw new InvalidOperationException(
                """
                Brak hasła do bazy.

                Ustaw MSSQL_PASSWORD (albo cały KARTOTEKA_CS) w zmiennych środowiskowych
                lub w pliku .env obok repozytorium. Wzór znajdziesz w .env.example.

                    cp .env.example .env      # i wpisz własne hasło
                """);

        return $"Server={host};Database={baza};User Id={user};Password={haslo};TrustServerCertificate=True;";
    }

    private static string Zmienna(string nazwa, string domyslna)
    {
        var v = Environment.GetEnvironmentVariable(nazwa);
        return string.IsNullOrWhiteSpace(v) ? domyslna : v;
    }

    /// <summary>Prosty czytnik .env — bez zależności, wystarczający do pracy lokalnej.</summary>
    private static void WczytajPlikEnv()
    {
        var katalog = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && katalog is not null; i++, katalog = katalog.Parent)
        {
            var plik = Path.Combine(katalog.FullName, ".env");
            if (!File.Exists(plik)) continue;

            foreach (var linia in File.ReadAllLines(plik))
            {
                var t = linia.Trim();
                if (t.Length == 0 || t.StartsWith('#')) continue;

                var i2 = t.IndexOf('=');
                if (i2 <= 0) continue;

                var klucz = t[..i2].Trim();
                var wartosc = t[(i2 + 1)..].Trim().Trim('"');

                // Zmienna ustawiona w środowisku ma pierwszeństwo przed plikiem.
                if (Environment.GetEnvironmentVariable(klucz) is null)
                    Environment.SetEnvironmentVariable(klucz, wartosc);
            }
            return;
        }
    }
}
