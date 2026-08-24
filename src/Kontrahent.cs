namespace KartotekaKontrahentow;

public sealed class Kontrahent
{
    /// <summary>Górna granica wynikająca z DECIMAL(12,2) w bazie.</summary>
    public const decimal MaxLimit = 9_999_999_999.99m;

    public const int MaxDlugoscNazwy = 200;
    public const int MaxDlugoscMiasta = 100;

    public int Id { get; init; }
    public string Nazwa { get; set; } = "";
    public string Nip { get; set; } = "";
    public string Miasto { get; set; } = "";
    public decimal LimitKredytowy { get; set; }
    public DateTime DataDodania { get; init; }

    /// <summary>
    /// Limit przeliczony po kursie z NBP. Nie trafia do bazy.
    /// Nullable celowo: gdy NBP nie odpowie, kolumna ma zostać PUSTA,
    /// a nie pokazywać 0,00 — bo zero jest też poprawnym limitem.
    /// </summary>
    public decimal? LimitEur { get; set; }

    public override string ToString() => $"{Nazwa} ({Nip})";
}
