-- Kartoteka kontrahentów — schemat MS SQL
-- Uruchomienie: sqlcmd -S localhost -U sa -P <haslo> -C -i db/schema.sql

IF DB_ID('Kartoteka') IS NULL
    CREATE DATABASE Kartoteka;
GO

USE Kartoteka;
GO

-- Skrypt jest idempotentny: ponowne uruchomienie NIE kasuje danych.
-- Wczesniej bezwarunkowy DROP TABLE czyscil zapelniona kartoteke, a README
-- podawalo to polecenie jako standardowy krok instalacji.
IF OBJECT_ID('dbo.Kontrahenci', 'U') IS NOT NULL
BEGIN
    PRINT 'Tabela dbo.Kontrahenci juz istnieje — pomijam tworzenie i dane poczatkowe.';
    RETURN;
END;
GO

CREATE TABLE dbo.Kontrahenci
(
    Id             INT            IDENTITY(1,1) NOT NULL,
    Nazwa          NVARCHAR(200)  NOT NULL,
    Nip            CHAR(10)       NOT NULL,
    Miasto         NVARCHAR(100)  NOT NULL,
    LimitKredytowy DECIMAL(12, 2) NOT NULL CONSTRAINT DF_Kontrahenci_Limit DEFAULT (0),
    DataDodania    DATETIME2(0)   NOT NULL CONSTRAINT DF_Kontrahenci_Data  DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT PK_Kontrahenci PRIMARY KEY CLUSTERED (Id),

    -- NIP jednoznacznie identyfikuje kontrahenta — pilnuje tego baza, nie aplikacja.
    CONSTRAINT UQ_Kontrahenci_Nip UNIQUE (Nip),

    -- Limit ujemny nie ma sensu biznesowego; check trzyma to przy każdej ścieżce zapisu,
    -- także przy imporcie z pominięciem aplikacji.
    CONSTRAINT CK_Kontrahenci_Limit CHECK (LimitKredytowy >= 0),

    -- NIP to dokładnie dziesięć cyfr.
    CONSTRAINT CK_Kontrahenci_Nip CHECK (Nip NOT LIKE '%[^0-9]%')
);
GO

-- Wyszukiwanie po nazwie i mieście to najczęstsza operacja w kartotece.
CREATE INDEX IX_Kontrahenci_Nazwa  ON dbo.Kontrahenci (Nazwa);
CREATE INDEX IX_Kontrahenci_Miasto ON dbo.Kontrahenci (Miasto);
GO

INSERT INTO dbo.Kontrahenci (Nazwa, Nip, Miasto, LimitKredytowy) VALUES
    (N'Stocznia Szczecińska sp. z o.o.', '8522612345', N'Szczecin', 250000.00),
    (N'Bałtyk Logistics S.A.',           '8511198765', N'Świnoujście', 120000.00),
    (N'Odra Meble sp.j.',                '8522655443', N'Szczecin',  45000.00),
    (N'Pomorska Hurtownia Stali',        '8513322110', N'Police',   310500.50);
GO

SELECT COUNT(*) AS WierszyWKartotece FROM dbo.Kontrahenci;
GO
