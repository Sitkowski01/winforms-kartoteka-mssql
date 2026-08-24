using Microsoft.Data.SqlClient;

namespace KartotekaKontrahentow;

/// <summary>
/// Dostęp do MS SQL przez ADO.NET. Cały SQL jest w jednym miejscu, a każdy parametr
/// idzie przez SqlParameter — nigdzie nie sklejam zapytania ze stringów, bo to jedyny
/// sposób, żeby nazwa kontrahenta z apostrofem nie zamieniła się w SQL injection.
/// </summary>
public sealed class KontrahentRepository(string connectionString)
{
    private readonly string _cs = connectionString;

    public async Task<List<Kontrahent>> PobierzWszystkichAsync(string? filtr = null)
    {
        // ESCAPE '\' bo parametryzacja chroni przed wstrzyknięciem SQL, ale NIE odbiera
        // znakom %, _ i [ znaczenia wieloznaczników wewnątrz LIKE. Bez tego wpisanie
        // „_" w wyszukiwarkę zwracało praktycznie całą kartotekę, a szukanie nazwy
        // zawierającej „100%" nie znajdowało niczego.
        const string sql = """
            SELECT Id, Nazwa, Nip, Miasto, LimitKredytowy, DataDodania
            FROM dbo.Kontrahenci
            WHERE @filtr IS NULL
               OR Nazwa  LIKE '%' + @filtr + '%' ESCAPE '\'
               OR Miasto LIKE '%' + @filtr + '%' ESCAPE '\'
            ORDER BY Nazwa;
            """;

        await using var conn = new SqlConnection(_cs);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@filtr", System.Data.SqlDbType.NVarChar, 400).Value =
            string.IsNullOrWhiteSpace(filtr) ? DBNull.Value : EscapeLike(filtr);

        await conn.OpenAsync();
        await using var r = await cmd.ExecuteReaderAsync();

        var lista = new List<Kontrahent>();
        while (await r.ReadAsync())
        {
            lista.Add(new Kontrahent
            {
                Id = r.GetInt32(0),
                Nazwa = r.GetString(1),
                Nip = r.GetString(2),
                Miasto = r.GetString(3),
                LimitKredytowy = r.GetDecimal(4),
                DataDodania = r.GetDateTime(5),
            });
        }
        return lista;
    }

    /// <summary>Neutralizuje wieloznaczniki LIKE, żeby użytkownik szukał tekstu, a nie wzorca.</summary>
    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public async Task<int> DodajAsync(Kontrahent k)
    {
        const string sql = """
            INSERT INTO dbo.Kontrahenci (Nazwa, Nip, Miasto, LimitKredytowy)
            OUTPUT INSERTED.Id
            VALUES (@nazwa, @nip, @miasto, @limit);
            """;

        await using var conn = new SqlConnection(_cs);
        await using var cmd = new SqlCommand(sql, conn);
        Zwiaz(cmd, k);

        await conn.OpenAsync();
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    /// <returns>false, gdy wiersz nie istnieje — np. ktoś usunął go w międzyczasie.</returns>
    public async Task<bool> ZaktualizujAsync(Kontrahent k)
    {
        const string sql = """
            UPDATE dbo.Kontrahenci
            SET Nazwa = @nazwa, Nip = @nip, Miasto = @miasto, LimitKredytowy = @limit
            WHERE Id = @id;
            """;

        await using var conn = new SqlConnection(_cs);
        await using var cmd = new SqlCommand(sql, conn);
        Zwiaz(cmd, k);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = k.Id;

        await conn.OpenAsync();
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    /// <returns>false, gdy wiersz już nie istniał.</returns>
    public async Task<bool> UsunAsync(int id)
    {
        const string sql = "DELETE FROM dbo.Kontrahenci WHERE Id = @id;";

        await using var conn = new SqlConnection(_cs);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = id;

        await conn.OpenAsync();
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    private static void Zwiaz(SqlCommand cmd, Kontrahent k)
    {
        cmd.Parameters.Add("@nazwa", System.Data.SqlDbType.NVarChar, Kontrahent.MaxDlugoscNazwy).Value = k.Nazwa;
        cmd.Parameters.Add("@nip", System.Data.SqlDbType.Char, 10).Value = k.Nip;
        cmd.Parameters.Add("@miasto", System.Data.SqlDbType.NVarChar, Kontrahent.MaxDlugoscMiasta).Value = k.Miasto;
        var limit = cmd.Parameters.Add("@limit", System.Data.SqlDbType.Decimal);
        limit.Precision = 12;
        limit.Scale = 2;
        limit.Value = k.LimitKredytowy;
    }
}
