using System.ComponentModel;

namespace KartotekaKontrahentow;

public sealed class MainForm : Form
{
    private readonly KontrahentRepository _repo;
    private readonly NbpClient _nbp = new();

    private readonly DataGridView _grid = new();
    private readonly TextBox _filtr = new();
    private readonly TextBox _nazwa = new();
    private readonly TextBox _nip = new();
    private readonly TextBox _miasto = new();
    private readonly NumericUpDown _limit = new();
    private readonly Label _status = new();
    private readonly Button _btnNowy = new() { Text = "Nowy" };
    private readonly Button _btnZapisz = new() { Text = "Zapisz" };
    private readonly Button _btnUsun = new() { Text = "Usuń" };
    private readonly Button _btnOdswiez = new() { Text = "Odśwież" };

    private BindingList<Kontrahent> _dane = [];
    private int? _edytowaneId;

    /// <summary>
    /// Blokuje reakcję na zdarzenie zaznaczenia, gdy sami przestawiamy grid.
    /// Bez tego przypisanie DataSource albo wyczyszczenie formularza wywoływało
    /// WczytajZaznaczony i po cichu wracało do trybu edycji — zapis nowego
    /// kontrahenta robił wtedy UPDATE na cudzym wierszu.
    /// </summary>
    private bool _wstrzymajSelekcje;

    /// <summary>Blokuje równoległe odświeżenia (Enter w wyszukiwarce wciśnięty dwa razy).</summary>
    private bool _wTrakcie;

    public MainForm(KontrahentRepository repo)
    {
        _repo = repo;
        Text = "Kartoteka kontrahentów — MS SQL + kurs NBP";
        Width = 1000;
        Height = 620;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 520);

        BudujUklad();
        Load += async (_, _) => await OdswiezAsync();
    }

    private void BudujUklad()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var pasek = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        pasek.Controls.Add(new Label { Text = "Szukaj:", AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        _filtr.Width = 260;
        _filtr.PlaceholderText = "nazwa albo miasto";
        _filtr.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await OdswiezAsync();
        };
        pasek.Controls.Add(_filtr);
        _btnOdswiez.Click += async (_, _) => await OdswiezAsync();
        pasek.Controls.Add(_btnOdswiez);
        root.Controls.Add(pasek, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Nazwa", DataPropertyName = nameof(Kontrahent.Nazwa), FillWeight = 34 },
            new DataGridViewTextBoxColumn { HeaderText = "NIP", DataPropertyName = nameof(Kontrahent.Nip), FillWeight = 14 },
            new DataGridViewTextBoxColumn { HeaderText = "Miasto", DataPropertyName = nameof(Kontrahent.Miasto), FillWeight = 16 },
            new DataGridViewTextBoxColumn
            {
                HeaderText = "Limit (PLN)",
                DataPropertyName = nameof(Kontrahent.LimitKredytowy),
                FillWeight = 18,
                DefaultCellStyle = { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight },
            },
            new DataGridViewTextBoxColumn
            {
                HeaderText = "Limit (EUR)",
                DataPropertyName = nameof(Kontrahent.LimitEur),
                FillWeight = 18,
                // NullValue = "" — gdy NBP nie odpowie, komórka ma być pusta, a nie 0,00.
                DefaultCellStyle = { Format = "N2", NullValue = "", Alignment = DataGridViewContentAlignment.MiddleRight },
            });
        _grid.SelectionChanged += (_, _) => WczytajZaznaczony();
        root.Controls.Add(_grid, 0, 1);

        var edycja = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 2 };
        for (var i = 0; i < 8; i++)
            edycja.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, i % 2 == 0 ? 8 : 17));

        edycja.Controls.Add(new Label { Text = "Nazwa", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        _nazwa.Dock = DockStyle.Fill;
        _nazwa.MaxLength = Kontrahent.MaxDlugoscNazwy;   // inaczej SqlParameter ucinał tekst po cichu
        edycja.Controls.Add(_nazwa, 1, 0);
        edycja.Controls.Add(new Label { Text = "NIP", Anchor = AnchorStyles.Left, AutoSize = true }, 2, 0);
        _nip.Dock = DockStyle.Fill; _nip.MaxLength = 10; edycja.Controls.Add(_nip, 3, 0);
        edycja.Controls.Add(new Label { Text = "Miasto", Anchor = AnchorStyles.Left, AutoSize = true }, 4, 0);
        _miasto.Dock = DockStyle.Fill;
        _miasto.MaxLength = Kontrahent.MaxDlugoscMiasta;
        edycja.Controls.Add(_miasto, 5, 0);
        edycja.Controls.Add(new Label { Text = "Limit PLN", Anchor = AnchorStyles.Left, AutoSize = true }, 6, 0);
        _limit.Dock = DockStyle.Fill;
        // Zakres taki sam jak DECIMAL(12,2) w bazie. Wcześniej 9 999 999 po cichu
        // obcinało limit przy edycji kontrahenta z większą kwotą.
        _limit.Maximum = Kontrahent.MaxLimit;
        _limit.DecimalPlaces = 2;
        _limit.ThousandsSeparator = true;
        edycja.Controls.Add(_limit, 7, 0);

        var przyciski = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _btnNowy.Click += (_, _) => WyczyscFormularz();
        _btnZapisz.Click += async (_, _) => await ZapiszAsync();
        _btnUsun.Click += async (_, _) => await UsunAsync();
        przyciski.Controls.AddRange([_btnNowy, _btnZapisz, _btnUsun]);
        edycja.Controls.Add(przyciski, 1, 1);
        edycja.SetColumnSpan(przyciski, 7);
        root.Controls.Add(edycja, 0, 2);

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = SystemColors.GrayText;
        root.Controls.Add(_status, 0, 3);

        Controls.Add(root);
    }

    private async Task OdswiezAsync()
    {
        if (_wTrakcie) return;
        _wTrakcie = true;
        try
        {
            UstawStan(false, "wczytuję z bazy…");
            var lista = await _repo.PobierzWszystkichAsync(_filtr.Text);

            // Integracja z WebAPI: kurs jest dodatkiem, więc jego brak nie może
            // zablokować listy — kolumna EUR zostaje wtedy pusta.
            var kurs = await _nbp.PobierzKursEurAsync();
            if (kurs is > 0)
                foreach (var k in lista)
                    k.LimitEur = Math.Round(k.LimitKredytowy / kurs.Value, 2);

            if (IsDisposed) return;   // okno mogło zostać zamknięte w trakcie zapytania

            var zapamietanyId = _edytowaneId;
            _wstrzymajSelekcje = true;
            _dane = new BindingList<Kontrahent>(lista);
            _grid.DataSource = _dane;
            _grid.ClearSelection();
            _grid.CurrentCell = null;
            _wstrzymajSelekcje = false;
            _edytowaneId = zapamietanyId;   // odświeżenie listy nie wybija z edycji

            var opisKursu = kurs is > 0
                ? $"kurs NBP: 1 EUR = {kurs:N4} PLN"
                : $"kurs niedostępny ({_nbp.OstatniBlad})";
            UstawStan(true, $"{lista.Count} kontrahentów · {opisKursu}");
        }
        catch (Exception e)
        {
            if (IsDisposed) return;
            UstawStan(true, "");
            Blad("Nie udało się pobrać danych z bazy", e);
        }
        finally
        {
            _wTrakcie = false;
        }
    }

    private void WczytajZaznaczony()
    {
        if (_wstrzymajSelekcje) return;
        if (_grid.CurrentRow?.DataBoundItem is not Kontrahent k) return;
        _edytowaneId = k.Id;
        _nazwa.Text = k.Nazwa;
        _nip.Text = k.Nip;
        _miasto.Text = k.Miasto;
        _limit.Value = Math.Clamp(k.LimitKredytowy, _limit.Minimum, _limit.Maximum);
    }

    private void WyczyscFormularz()
    {
        // Kolejność ma znaczenie: najpierw odcinamy zdarzenie, potem zdejmujemy
        // bieżącą komórkę (ClearSelection samo NIE rusza CurrentCell), a Id kasujemy
        // na końcu — inaczej handler zdąży je ustawić z powrotem.
        _wstrzymajSelekcje = true;
        _grid.ClearSelection();
        _grid.CurrentCell = null;
        _wstrzymajSelekcje = false;

        _edytowaneId = null;
        _nazwa.Clear();
        _nip.Clear();
        _miasto.Clear();
        _limit.Value = 0;
        _nazwa.Focus();
    }

    private async Task ZapiszAsync()
    {
        if (!Waliduj(out var blad))
        {
            MessageBox.Show(this, blad, "Uzupełnij dane", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var k = new Kontrahent
        {
            Id = _edytowaneId ?? 0,
            Nazwa = _nazwa.Text.Trim(),
            Nip = _nip.Text.Trim(),
            Miasto = _miasto.Text.Trim(),
            LimitKredytowy = _limit.Value,
        };

        try
        {
            UstawStan(false, "zapisuję…");
            if (_edytowaneId is null)
            {
                var id = await _repo.DodajAsync(k);
                UstawStan(true, $"dodano kontrahenta (Id {id})");
            }
            else if (await _repo.ZaktualizujAsync(k))
            {
                UstawStan(true, $"zapisano zmiany (Id {_edytowaneId})");
            }
            else
            {
                // UPDATE nie trafił w żaden wiersz — ktoś usunął go w międzyczasie.
                UstawStan(true, "");
                MessageBox.Show(this,
                    "Ten kontrahent już nie istnieje w kartotece — ktoś go w międzyczasie usunął.\n" +
                    "Zmiany nie zostały zapisane.",
                    "Rekord nieaktualny", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            WyczyscFormularz();
            await OdswiezAsync();
        }
        catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number is 2601 or 2627)
        {
            // Unikalność NIP-u pilnuje baza — aplikacja tylko tłumaczy to na język człowieka.
            UstawStan(true, "");
            MessageBox.Show(this, "Kontrahent z tym NIP-em już istnieje w kartotece.",
                "Duplikat NIP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception e)
        {
            UstawStan(true, "");
            Blad("Nie udało się zapisać kontrahenta", e);
        }
    }

    private async Task UsunAsync()
    {
        if (_edytowaneId is null)
        {
            MessageBox.Show(this, "Najpierw zaznacz kontrahenta na liście.", "Nic nie zaznaczono",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var potwierdzenie = MessageBox.Show(this,
            $"Usunąć kontrahenta „{_nazwa.Text}” z kartoteki?", "Potwierdzenie",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (potwierdzenie != DialogResult.Yes) return;

        try
        {
            UstawStan(false, "usuwam…");
            if (!await _repo.UsunAsync(_edytowaneId.Value))
            {
                UstawStan(true, "");
                MessageBox.Show(this, "Ten kontrahent już nie istniał w kartotece.",
                    "Rekord nieaktualny", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            WyczyscFormularz();
            await OdswiezAsync();
        }
        catch (Exception e)
        {
            UstawStan(true, "");
            Blad("Nie udało się usunąć kontrahenta", e);
        }
    }

    private bool Waliduj(out string blad)
    {
        var nazwa = _nazwa.Text.Trim();
        var miasto = _miasto.Text.Trim();
        var nip = _nip.Text.Trim();

        if (nazwa.Length == 0) { blad = "Nazwa jest wymagana."; return false; }
        if (nazwa.Length > Kontrahent.MaxDlugoscNazwy)
        { blad = $"Nazwa może mieć najwyżej {Kontrahent.MaxDlugoscNazwy} znaków."; return false; }
        if (nip.Length != 10 || !nip.All(char.IsAsciiDigit)) { blad = "NIP musi mieć dokładnie 10 cyfr."; return false; }
        if (miasto.Length == 0) { blad = "Miasto jest wymagane."; return false; }
        if (miasto.Length > Kontrahent.MaxDlugoscMiasta)
        { blad = $"Miasto może mieć najwyżej {Kontrahent.MaxDlugoscMiasta} znaków."; return false; }

        blad = "";
        return true;
    }

    /// <summary>
    /// Blokuje także pole filtra — inaczej Enter w trakcie zapytania startował drugie.
    /// Bez Application.DoEvents(): przy await i tak pętla komunikatów działa,
    /// a DoEvents wpuszczał komunikat zamknięcia okna w środek operacji.
    /// </summary>
    private void UstawStan(bool wlaczone, string tekst)
    {
        if (IsDisposed) return;
        _status.Text = tekst;
        _grid.Enabled = _filtr.Enabled = _btnNowy.Enabled = _btnZapisz.Enabled =
            _btnUsun.Enabled = _btnOdswiez.Enabled = wlaczone;
        Cursor = wlaczone ? Cursors.Default : Cursors.WaitCursor;
    }

    private void Blad(string co, Exception e)
    {
        if (IsDisposed) return;
        MessageBox.Show(this, $"{co}.\n\n{e.Message}", "Błąd", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _nbp.Dispose();
        base.Dispose(disposing);
    }
}
