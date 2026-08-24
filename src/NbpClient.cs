using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace KartotekaKontrahentow;

/// <summary>
/// Integracja z zewnętrznym WebAPI: publiczne API Narodowego Banku Polskiego
/// (api.nbp.pl, tabela A, bez klucza i bez limitu rejestracyjnego).
///
/// Kurs służy do pokazania limitu kredytowego kontrahenta również w euro.
/// Dwie rzeczy, które w takiej integracji są ważniejsze od samego wywołania:
///   1. timeout — obce API potrafi nie odpowiedzieć, a formularz nie może wisieć,
///   2. cache — NBP publikuje kurs raz dziennie, więc odpytywanie przy każdym
///      odświeżeniu listy byłoby marnowaniem cudzego serwera i własnego czasu.
/// </summary>
public sealed class NbpClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private decimal _kursEur;
    private DateOnly _kursZDnia;

    public string OstatniBlad { get; private set; } = "";

    /// <returns>Kurs EUR/PLN albo null, jeśli API nie odpowiedziało.</returns>
    public async Task<decimal?> PobierzKursEurAsync(CancellationToken ct = default)
    {
        // Kurs z dzisiaj już mamy — nie ruszamy sieci.
        if (_kursEur > 0 && _kursZDnia == DateOnly.FromDateTime(DateTime.Today))
            return _kursEur;

        try
        {
            const string url = "https://api.nbp.pl/api/exchangerates/rates/a/eur/?format=json";
            var odp = await _http.GetFromJsonAsync<NbpOdpowiedz>(url, ct);
            var kurs = odp?.Rates?.FirstOrDefault()?.Mid;

            if (kurs is null or <= 0)
            {
                OstatniBlad = "NBP zwróciło odpowiedź bez kursu";
                return null;
            }

            _kursEur = kurs.Value;
            _kursZDnia = DateOnly.FromDateTime(DateTime.Today);
            OstatniBlad = "";
            return _kursEur;
        }
        catch (TaskCanceledException)
        {
            // Timeout to nie jest błąd krytyczny — aplikacja ma dalej działać na samych złotówkach.
            OstatniBlad = "NBP nie odpowiedziało w 8 sekund";
            return null;
        }
        catch (HttpRequestException e)
        {
            OstatniBlad = $"Brak połączenia z NBP: {e.Message}";
            return null;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException)
        {
            // Proxy albo portal przechwytujący potrafi odpowiedzieć HTML-em ze statusem 200.
            // Bez tego GetFromJsonAsync rzucał NotSupportedException, który wychodził
            // poza tę metodę i wywracał całe odświeżanie listy — mimo że baza była sprawna,
            // a kurs jest tylko dodatkiem.
            OstatniBlad = "NBP zwróciło odpowiedź, której nie da się odczytać";
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed record NbpOdpowiedz(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("rates")] List<NbpKurs> Rates);

    private sealed record NbpKurs(
        [property: JsonPropertyName("no")] string No,
        [property: JsonPropertyName("effectiveDate")] string EffectiveDate,
        [property: JsonPropertyName("mid")] decimal Mid);
}
