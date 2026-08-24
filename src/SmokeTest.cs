namespace KartotekaKontrahentow;

/// <summary>
/// Przechodzi całą ścieżkę bez interfejsu: odczyt, dodanie, aktualizacja, usunięcie,
/// odrzucenie duplikatu NIP-u i wywołanie zewnętrznego WebAPI.
/// Uruchomienie: dotnet run -- --smoke
/// </summary>
internal static class SmokeTest
{
    public static async Task<int> UruchomAsync(string cs)
    {
        var repo = new KontrahentRepository(cs);
        using var nbp = new NbpClient();
        var bledy = 0;

        void Ok(string s) => Console.WriteLine($"  [OK]   {s}");
        void Fail(string s) { Console.WriteLine($"  [BLAD] {s}"); bledy++; }

        Console.WriteLine("=== Kartoteka kontrahentow — test kontrolny ===\n");

        // 1. ODCZYT
        Console.WriteLine("1. Odczyt z MS SQL");
        var wszyscy = await repo.PobierzWszystkichAsync();
        if (wszyscy.Count > 0) Ok($"wczytano {wszyscy.Count} kontrahentow");
        else Fail("kartoteka pusta — czy schema.sql zostal uruchomiony?");

        // 2. FILTR (parametryzowany LIKE)
        Console.WriteLine("\n2. Filtrowanie po nazwie i miescie");
        var szczecin = await repo.PobierzWszystkichAsync("Szczecin");
        // SQL Server porownuje pod kolacja CI/AI, wiec porownanie w C# tez musi ignorowac
        // wielkosc liter — inaczej poprawny wynik bylby raportowany jako blad.
        if (szczecin.Count > 0 && szczecin.All(k =>
                k.Miasto.Contains("Szczecin", StringComparison.OrdinalIgnoreCase) ||
                k.Nazwa.Contains("Szczecin", StringComparison.OrdinalIgnoreCase)))
            Ok($"filtr 'Szczecin' zwrocil {szczecin.Count} pozycji");
        else Fail("filtr nie zadzialal");

        // 3. SQL INJECTION — parametr, nie sklejanie stringow
        Console.WriteLine("\n3. Odpornosc na SQL injection");
        var atak = await repo.PobierzWszystkichAsync("'; DROP TABLE dbo.Kontrahenci; --");
        var poAtaku = await repo.PobierzWszystkichAsync();
        if (poAtaku.Count == wszyscy.Count)
            Ok($"zlosliwy filtr potraktowany jako tekst ({atak.Count} trafien), tabela nienaruszona");
        else Fail("tabela zmieniona po zlosliwym wejsciu");

        // 4. CREATE
        Console.WriteLine("\n4. Dodanie kontrahenta");
        var nip = $"99{DateTime.Now:HHmmssff}";
        var nowy = new Kontrahent { Nazwa = "Test Kontrolny sp. z o.o.", Nip = nip, Miasto = "Szczecin", LimitKredytowy = 12345.67m };
        var id = await repo.DodajAsync(nowy);
        if (id > 0) Ok($"dodano, Id = {id}"); else Fail("brak Id po INSERT");

        // 5. UPDATE
        Console.WriteLine("\n5. Aktualizacja");
        var zmiana = new Kontrahent { Id = id, Nazwa = "Test Kontrolny — po zmianie", Nip = nip, Miasto = "Police", LimitKredytowy = 999.99m };
        if (await repo.ZaktualizujAsync(zmiana))
        {
            var sprawdz = (await repo.PobierzWszystkichAsync("Police")).FirstOrDefault(k => k.Id == id);
            if (sprawdz?.LimitKredytowy == 999.99m && sprawdz.Miasto == "Police")
                Ok("zmiana zapisana i odczytana z powrotem");
            else Fail("zmiana nie zapisala sie poprawnie");
        }
        else Fail("UPDATE nie zmodyfikowal wiersza");

        // 6. UNIQUE na NIP — pilnuje baza, nie aplikacja
        Console.WriteLine("\n6. Ograniczenie unikalnosci NIP");
        try
        {
            await repo.DodajAsync(new Kontrahent { Nazwa = "Duplikat", Nip = nip, Miasto = "Szczecin", LimitKredytowy = 1 });
            Fail("baza przyjela duplikat NIP-u");
        }
        catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number is 2601 or 2627)
        {
            Ok($"duplikat odrzucony przez baze (blad {e.Number})");
        }

        // 7. CHECK na ujemny limit
        Console.WriteLine("\n7. Ograniczenie na ujemny limit");
        try
        {
            await repo.DodajAsync(new Kontrahent { Nazwa = "Ujemny", Nip = $"88{DateTime.Now:HHmmssff}", Miasto = "Szczecin", LimitKredytowy = -5 });
            Fail("baza przyjela ujemny limit");
        }
        catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number == 547)
        {
            Ok("ujemny limit odrzucony przez CHECK");
        }

        // 8. DELETE
        Console.WriteLine("\n8. Usuniecie");
        if (await repo.UsunAsync(id))
        {
            var zostal = (await repo.PobierzWszystkichAsync()).Any(k => k.Id == id);
            if (!zostal) Ok("wiersz usuniety"); else Fail("wiersz nadal w bazie");
        }
        else Fail("DELETE nie usunal wiersza");

        // 9. ZEWNETRZNE WEBAPI
        Console.WriteLine("\n9. Integracja z zewnetrznym WebAPI (api.nbp.pl)");
        var kurs = await nbp.PobierzKursEurAsync();
        if (kurs is > 0)
        {
            Ok($"kurs NBP: 1 EUR = {kurs:N4} PLN");
            var limitPln = 250_000m;
            Console.WriteLine($"         przyklad: limit {limitPln:N2} PLN = {limitPln / kurs.Value:N2} EUR");
        }
        else Fail($"nie udalo sie pobrac kursu: {nbp.OstatniBlad}");

        // 10. CACHE — drugie wywolanie nie rusza sieci
        Console.WriteLine("\n10. Cache kursu");
        if (kurs is null or <= 0)
        {
            // Bez pobranego kursu nie ma czego cache'owac. Wczesniej ten krok wykonywal
            // drugie prawdziwe zadanie, czekal pelne 8 s na timeout i obwinial cache
            // za problem z siecia.
            Console.WriteLine("  [--]   pominiete — kurs nie zostal pobrany w kroku 9");
        }
        else
        {
            var start = DateTime.UtcNow;
            await nbp.PobierzKursEurAsync();
            var ms = (DateTime.UtcNow - start).TotalMilliseconds;
            if (ms < 50) Ok($"drugie wywolanie z pamieci ({ms:N1} ms)");
            else Fail($"cache nie zadzialal ({ms:N0} ms)");
        }

        // 11. GORNA GRANICA LIMITU — DECIMAL(12,2) przyjmuje do 9 999 999 999,99
        Console.WriteLine("\n11. Gorna granica limitu (DECIMAL(12,2))");
        try
        {
            var duzyId = await repo.DodajAsync(new Kontrahent
            {
                Nazwa = "Limit graniczny",
                Nip = $"77{DateTime.Now:HHmmssff}",
                Miasto = "Szczecin",
                LimitKredytowy = Kontrahent.MaxLimit,
            });
            var odczyt = (await repo.PobierzWszystkichAsync("Limit graniczny")).FirstOrDefault(k => k.Id == duzyId);
            if (odczyt?.LimitKredytowy == Kontrahent.MaxLimit)
                Ok($"limit {Kontrahent.MaxLimit:N2} zapisany i odczytany bez obciecia");
            else
                Fail($"limit zmieniony przy zapisie: {odczyt?.LimitKredytowy:N2}");
            await repo.UsunAsync(duzyId);
        }
        catch (Exception e) { Fail($"nie udalo sie zapisac limitu granicznego: {e.Message}"); }

        Console.WriteLine(bledy == 0
            ? "\n=== WSZYSTKO PRZESZLO ==="
            : $"\n=== BLEDOW: {bledy} ===");
        return bledy == 0 ? 0 : 1;
    }
}
