# Kartoteka kontrahentów — WinForms + MS SQL + zewnętrzne WebAPI

[![CI](https://github.com/Sitkowski01/winforms-kartoteka-mssql/actions/workflows/ci.yml/badge.svg)](https://github.com/Sitkowski01/winforms-kartoteka-mssql/actions/workflows/ci.yml)

Mała aplikacja okienkowa w C#: **CRUD na bazie MS SQL** plus **integracja z zewnętrznym
WebAPI** (kursy walut Narodowego Banku Polskiego). Limit kredytowy kontrahenta widać
i w złotówkach, i w euro po kursie pobranym z NBP.

Baza stoi w Dockerze, więc uruchomienie nie wymaga instalowania SQL Servera.

---

## Uruchomienie

```bash
cp .env.example .env        # uzupełnij MSSQL_PASSWORD (plik wzorcowy jest pusty)
docker compose up -d        # MS SQL 2022 — hasło bierze z .env

docker exec -e P="$MSSQL_PASSWORD" mssql-kartoteka \
    /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$P" -C -i /db/schema.sql

cd src
dotnet run                  # okno aplikacji
dotnet run -- --smoke       # test kontrolny bez interfejsu
```

### Konfiguracja — bez sekretów w kodzie

**W repozytorium nie ma żadnego hasła.** Connection string powstaje w `Konfiguracja.cs`
z trzech źródeł, w tej kolejności:

1. `KARTOTEKA_CS` — gotowy connection string (produkcja, CI),
2. plik `.env` obok repozytorium — wygoda przy pracy lokalnej, jest w `.gitignore`,
3. `MSSQL_HOST` / `MSSQL_DB` / `MSSQL_USER` / `MSSQL_PASSWORD`.

`.env.example` **nie zawiera żadnych wartości** — pola z wpisanymi „przykładowymi"
hasłami zostają w historii repozytorium i podnoszą alarm w skanerach sekretów,
nawet gdy nic nie znaczą.

Gdy hasła nie ma w żadnym ze źródeł, aplikacja **odmawia startu** i mówi, czego brakuje —
zamiast po cichu sięgnąć po wartość domyślną wpisaną w kod:

```
Brak hasła do bazy.

Ustaw MSSQL_PASSWORD (albo cały KARTOTEKA_CS) w zmiennych środowiskowych
lub w pliku .env obok repozytorium. Wzór znajdziesz w .env.example.
```

---

## Co jest w środku

```
db/schema.sql                 tabela, klucz główny, UNIQUE na NIP, dwa CHECK-i, dwa indeksy
src/Konfiguracja.cs           connection string z .env / zmiennych — zero sekretów w kodzie
src/Kontrahent.cs             model
src/KontrahentRepository.cs   ADO.NET: SELECT / INSERT / UPDATE / DELETE, wyłącznie parametryzowane
src/NbpClient.cs              integracja z api.nbp.pl — timeout, cache, obsługa braku odpowiedzi
src/MainForm.cs               interfejs: DataGridView, formularz edycji, walidacja, obsługa błędów
src/SmokeTest.cs              jedenaście sprawdzeń całej ścieżki bez otwierania okna
```

### Baza pilnuje swoich reguł sama

Ograniczenia nie siedzą wyłącznie w kodzie aplikacji, bo do bazy da się wejść też z boku —
importem albo ręcznie:

```sql
CONSTRAINT UQ_Kontrahenci_Nip   UNIQUE (Nip)                        -- NIP identyfikuje kontrahenta
CONSTRAINT CK_Kontrahenci_Limit CHECK (LimitKredytowy >= 0)         -- ujemny limit nie ma sensu
CONSTRAINT CK_Kontrahenci_Nip   CHECK (Nip NOT LIKE '%[^0-9]%')     -- dokładnie dziesięć cyfr
```

Aplikacja łapie `SqlException` o numerze 2601/2627 i tłumaczy go na komunikat po polsku,
zamiast pokazywać użytkownikowi surowy błąd bazy.

### Cały SQL parametryzowany

Nigdzie nie sklejam zapytania ze stringów. Test kontrolny sprawdza to wprost: wpisuje
w pole wyszukiwania `'; DROP TABLE dbo.Kontrahenci; --` i weryfikuje, że tabela nadal
stoi, a ciąg został potraktowany jako zwykły tekst.

### Integracja z WebAPI — trzy rzeczy poza samym wywołaniem

**Timeout 8 sekund.** Obce API potrafi nie odpowiedzieć, a formularz nie może przez to wisieć.

**Cache na dzień.** NBP publikuje kurs raz dziennie, więc odpytywanie przy każdym odświeżeniu
listy byłoby marnowaniem cudzego serwera. Drugie wywołanie schodzi z ~200 ms do 0,1 ms.

**Brak kursu nie blokuje aplikacji.** Kurs jest dodatkiem do listy kontrahentów, nie jej
warunkiem — gdy NBP milczy, kolumna EUR zostaje pusta, a na pasku stanu pojawia się powód.

---

## Test kontrolny

`dotnet run -- --smoke` przechodzi jedenaście sprawdzeń na prawdziwej bazie i prawdziwym API,
bez otwierania okna — dzięki temu da się to sprawdzić także tam, gdzie nie ma pulpitu.

```
=== Kartoteka kontrahentow — test kontrolny ===

1. Odczyt z MS SQL
  [OK]   wczytano 4 kontrahentow
2. Filtrowanie po nazwie i miescie
  [OK]   filtr 'Szczecin' zwrocil 2 pozycji
3. Odpornosc na SQL injection
  [OK]   zlosliwy filtr potraktowany jako tekst (0 trafien), tabela nienaruszona
4. Dodanie kontrahenta
  [OK]   dodano, Id = 8
5. Aktualizacja
  [OK]   zmiana zapisana i odczytana z powrotem
6. Ograniczenie unikalnosci NIP
  [OK]   duplikat odrzucony przez baze (blad 2627)
7. Ograniczenie na ujemny limit
  [OK]   ujemny limit odrzucony przez CHECK
8. Usuniecie
  [OK]   wiersz usuniety
9. Integracja z zewnetrznym WebAPI (api.nbp.pl)
  [OK]   kurs NBP: 1 EUR = 4,3124 PLN
         przyklad: limit 250 000,00 PLN = 57 972,36 EUR
10. Cache kursu
  [OK]   drugie wywolanie z pamieci (0,2 ms)
11. Gorna granica limitu (DECIMAL(12,2))
  [OK]   limit 9 999 999 999,99 zapisany i odczytany bez obciecia

=== WSZYSTKO PRZESZLO ===
```

CI kompiluje projekt i sprawdza formatowanie, ale tego testu **nie uruchamia** —
potrzebuje żywej instancji MS SQL i odpowiedzi z `api.nbp.pl`. Automat pilnuje tego,
co da się sprawdzić bez zależności zewnętrznych; reszta jest do odpalenia u siebie.

---

## Co poprawiłem po przeglądzie kodu

Pierwszą wersję, która przechodziła wszystkie testy, przepuściłem przez przegląd
kodu (Claude Code). Wyszło jedenaście rzeczy, dwie powodowały **utratę danych**:

- **Przycisk „Nowy" nie wychodził z trybu edycji.** `ClearSelection()` nie rusza
  `CurrentCell`, więc zdarzenie `SelectionChanged` natychmiast przywracało `_edytowaneId`
  poprzedniego wiersza. Scenariusz: zaznaczasz kontrahenta, klikasz **Nowy**, wpisujesz
  nowe dane, klikasz **Zapisz** — i zamiast INSERT-a leci UPDATE, który **nadpisuje
  zaznaczonego wcześniej kontrahenta**.
- **Ten sam mechanizm ustawiał tryb edycji zaraz po starcie.** Przypisanie `DataSource`
  zaznacza pierwszy wiersz i odpala to samo zdarzenie, więc aplikacja otwierała się już
  „w edycji" alfabetycznie pierwszego kontrahenta.

  Jedno i drugie naprawia flaga wstrzymująca obsługę zaznaczenia na czas programowych
  zmian gridu, zdjęcie `CurrentCell` i kasowanie `_edytowaneId` **po** zdarzeniu.

- **Limit był po cichu obcinany.** `NumericUpDown.Maximum` wynosił 9 999 999, a kolumna
  to `DECIMAL(12,2)`. Edycja samego miasta u kontrahenta z limitem 15 mln zapisywała
  z powrotem obciętą wartość. Teraz zakres kontrolki równa się zakresowi kolumny,
  a test kontrolny sprawdza granicę 9 999 999 999,99.

Pozostałe osiem:

- **`NbpClient` łapie też `JsonException` i `NotSupportedException`.** Proxy potrafi
  odpowiedzieć HTML-em ze statusem 200 — wcześniej wywracało to całe odświeżanie listy.
- **Zniknął `Application.DoEvents()`, a pole filtra jest blokowane na czas zapytania.**
  Dwa Entery pod rząd startowały dwa równoległe odświeżenia, a zamknięcie okna w trakcie
  kończyło się wyjątkiem w bloku `catch`.
- **Wynik `ZaktualizujAsync` i `UsunAsync` jest sprawdzany.** Wcześniej „zapisano zmiany"
  pojawiało się także wtedy, gdy UPDATE nie trafił w żaden wiersz.
- **`LimitEur` jest nullowalne**, więc przy braku kursu kolumna zostaje **pusta**
  zamiast pokazywać `0,00` — co czytałoby się jak limit równy zeru.
- **Wieloznaczniki `LIKE` są ekranowane** (`ESCAPE '\'`). Parametryzacja chroni przed
  wstrzyknięciem SQL, ale nie odbiera znakom `%` i `_` ich specjalnego znaczenia.
- **Pola tekstowe mają `MaxLength` zgodne z szerokością kolumn**, więc obcięcie nie
  następuje dopiero przy zapisie.
- **Test filtra porównuje bez uwzględniania wielkości liter** — kolacja serwera jest CI/AI.
- **Test cache'u jest pomijany, gdy kursu nie udało się pobrać**, zamiast oblewać
  z powodu niedostępności cudzego API.

**Osobno, po zgłoszeniu ze skanera sekretów:** pierwsza wersja miała hasło do bazy
wpisane jako wartość domyślna w `Program.cs`. Było zmyślone i dotyczyło lokalnego
kontenera, ale skaner zadziałał słusznie — hasło w kodzie zostaje w historii repozytorium
i uczy złego nawyku. Cała konfiguracja przeniosła się do `Konfiguracja.cs`, a brak hasła
zatrzymuje aplikację zamiast uruchamiać ją na wartości z kodu.

Poza tym `db/schema.sql` jest teraz idempotentny — wcześniej bezwarunkowy `DROP TABLE`
kasował zapełnioną kartotekę przy ponownym uruchomieniu polecenia z README — a baza
dostała trwały wolumen, żeby `docker compose down` nie zabierał danych.

## Stos i zakres

**To jest .NET 9, nie .NET Framework 4.8.** WinForms, ADO.NET, `SqlConnection`,
`SqlCommand` i `SqlParameter` działają tak samo w obu, a układ projektu i sposób pracy
z bazą przenoszą się jeden do jednego. Różnice zaczynają się przy projektancie formularzy,
konfiguracji (`App.config` kontra zmienne środowiskowe) i sposobie publikacji.
Interfejs jest tu budowany kodem, a nie designerem, żeby repo dało się przejrzeć
bez otwierania Visual Studio.

**C# nie jest moim głównym językiem.** Statycznie typowane języki kompilowane mam
z C/C++ ze stażu embedded, a wzorce, które widać w tym repozytorium — warstwa
repozytorium nad bazą, wyłącznie parametryzowany dostęp do danych, ograniczenia
trzymane po stronie serwera, obsługa awarii zewnętrznego API — przenoszą się między
językami. Ta aplikacja pokazuje, że potrafię je odtworzyć w nowym stosie i znaleźć
w tym własne błędy, łącznie z dwoma powodującymi utratę danych.
