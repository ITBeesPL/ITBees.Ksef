# ITBees.Ksef

Biblioteka .NET 8 do integracji z **KSeF API 2.0** (Krajowy System e-Faktur) — wersją API obowiązującą od lutego 2026 r.

Solucja składa się z trzech pakietów:

- **ITBees.Ksef** — czysty klient KSeF API 2.0 + generator FA(3); zero zależności od EF Core i stacka FAS, użyteczny w dowolnej aplikacji .NET.
- **ITBees.Ksef.Credentials** — magazyn poświadczeń per firma: encja `KsefCredential` z zaszyfrowanym tokenem albo certyfikatem, endpointy do zarządzania nim i test połączenia. Zamienia jednofirmowego klienta w integrację wielofirmową (patrz "Poświadczenia per firma" niżej).
- **ITBees.Ksef.Fas** — gotowe wpięcie dla projektów opartych o ITBees.FAS.Payments: encja outboxu `KsefInvoiceRecord` z numeracją faktur, rejestracja modelu EF i worker tła wystawiający fakturę KSeF dla każdej opłaconej `PaymentSession` (patrz sekcja "Integracja z FAS" niżej).

## Zakres

- **Uwierzytelnianie tokenem KSeF** (pełny flow API 2.0): `POST /auth/challenge` → szyfrowanie `{token}|{timestampMs}` RSA-OAEP (SHA-256) kluczem publicznym MF → `POST /auth/ksef-token` → polling `GET /auth/{referenceNumber}` → `POST /auth/token/redeem` → JWT `accessToken`/`refreshToken` z automatycznym cache i odświeżaniem (`POST /auth/token/refresh`).
- **Uwierzytelnianie certyfikatem** (`AuthMode = Certificate`): ten sam challenge podpisywany XAdES-BES (enveloped, RSA-SHA256, exc-c14n) i wysyłany na `POST /auth/xades-signature`. Żaden token nie jest przechowywany — wystarczy `.p12`/`.pfx` z kluczem prywatnym.
- **Sesja interaktywna**: `POST /sessions/online` (formCode FA (3) / 1-0E), obowiązkowe szyfrowanie faktury AES-256-CBC (PKCS#7) kluczem sesyjnym zaszyfrowanym RSA-OAEP, `POST /sessions/online/{ref}/invoices`, polling statusu, `POST /sessions/online/{ref}/close`.
- **Pobieranie faktur z KSeF** (`IKsefInvoiceQueryService`): `POST /invoices/query/metadata` z zakresem dat i stroną transakcji (Subject1 = sprzedaż, Subject2 = koszty), automatyczne stronicowanie, oraz `GET /invoices/ksef/{numer}` po oryginalny XML faktury.
- **Generator XML FA(3)** (namespace `http://crd.gov.pl/wzor/2025/06/25/13775/`) z prostego modelu domenowego `KsefInvoice` — walidowany w testach względem oficjalnego XSD MF.
- **UPO** — pobranie poświadczenia dla faktury po nadaniu numeru KSeF.
- **Tryb wielofirmowy** (`IKsefClientFactory`) — usługi budowane z `KsefOptions` wyliczonych w czasie żądania (np. odczytanych z bazy), z cache sesji per komplet poświadczeń.
- Środowiska: TEST (`api-test.ksef.mf.gov.pl/v2`), DEMO (`api-demo.ksef.mf.gov.pl/v2`), PROD (`api.ksef.mf.gov.pl/v2`).

## Szybki start

```csharp
// Program.cs
services.AddITBeesKsef(configuration); // sekcja "Ksef"
```

```json
{
  "Ksef": {
    "Environment": "Test",            // Test | Demo | Production
    "KsefToken": "<token wygenerowany w aplikacji KSeF dla NIP sprzedawcy>",
    "Nip": "5555555555",
    "SystemInfo": "MojaAplikacja",
    "Seller": {
      "Nip": "5555555555",
      "Name": "Moja Firma Sp. z o.o.",
      "AddressLine1": "ul. Przykładowa 1",
      "AddressLine2": "00-001 Warszawa",
      "CountryCode": "PL"
    }
  }
}
```

```csharp
var result = await ksefInvoiceService.SendInvoiceAsync(new KsefInvoice
{
    Number = "FV/2026/08/001",
    IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
    Currency = "PLN",
    Buyer = new KsefParty
    {
        Nip = "1111111111",           // null => nabywca B2C (BrakID)
        Name = "Nabywca S.A.",
        AddressLine1 = "ul. Polna 2",
        AddressLine2 = "11-111 Kraków"
    },
    Lines =
    {
        new KsefInvoiceLine { Name = "Abonament", Quantity = 1, UnitNetPrice = 100m, VatRate = 23 }
    },
    IsPaid = true,
    PaymentDate = DateOnly.FromDateTime(DateTime.UtcNow)
});

// result.KsefNumber — numer KSeF faktury
// result.UpoXml    — UPO (XML), jeżeli było już dostępne
```

### Stawki VAT inne niż procent (od 8.0.17)

FA(3) nie zna w `P_12` gołego `0` — schemat rozróżnia krajowe 0%, WDT, eksport, zwolnienie,
odwrotne obciążenie i „nie podlega”, a każdy z tych przypadków ma własne pole sum w `Fa`.
Dlatego pozycja (`KsefInvoiceLine`) i część zaliczki (`KsefAdvancePayment`) mają obok `VatRate`
pole `VatRateKind`:

| `VatRateKind` | `VatRate` | `P_12` | Suma w `Fa` | Dodatkowo |
|---|---|---|---|---|
| `Standard` | 23, 22, 8, 7, 5, 4 | `23` … | `P_13_1`–`P_13_3` + `P_14_x` | |
| `Standard` | 0 | `0 KR` | `P_13_6_1` | |
| `ZeroIntraCommunity` | 0 | `0 WDT` | `P_13_6_2` | |
| `ZeroExport` | 0 | `0 EX` | `P_13_6_3` | |
| `Exempt` | 0 | `zw` | `P_13_7` | wymaga `KsefInvoice.ExemptionBasis` → `P_19A` |
| `ReverseCharge` | 0 | `oo` | `P_13_10` | `Adnotacje/P_18 = 1` |
| `NotSubjectNonEu` | 0 | `np I` | `P_13_8` | usługi/dostawy poza krajem (nabywca spoza UE) |
| `NotSubjectEu` | 0 | `np II` | `P_13_9` | usługi z art. 100 ust. 1 pkt 4 (nabywca z UE) |

Rodzaj inny niż `Standard` z `VatRate ≠ 0` generator odrzuca — to prawie na pewno błąd mapowania.
`KsefVatRates.Parse("np I")` czyta `P_12` z cudzych dokumentów z powrotem na parę (stawka, rodzaj).

Nabywca spoza Polski: zamiast `Nip` podaj `EuVatNumber` (+ `EuVatCountryCode`, domyślnie
`CountryCode`; generator wystawi `KodUE` + `NrVatUE`) albo `ForeignTaxId`
(+ `ForeignTaxIdCountryCode`; `KodKraju` + `NrID`). Lista prefiksów UE to `KsefEuVatCountries`
(uwaga: Grecja to `EL`, Irlandia Północna `XI`, Wielka Brytania nie jest w UE).

### Odbiorca inny niż nabywca — Podmiot3 (od 8.0.19)

Gdy fakturę opłaca centrala, a towar lub usługę odbiera jej oddział, zakład albo inna spółka
z grupy (typowo spółki Skarbu Państwa i jednostki budżetowe), FA(3) opisuje tę jednostkę w węźle
`Podmiot3` z rolą `2` — „Odbiorca”. `KsefInvoice.ThirdParties` przyjmuje listę takich podmiotów
(schemat dopuszcza do 100), każdy z rolą ze schematu (`KsefThirdPartyRole`) i danymi jak u nabywcy:

```csharp
invoice.ThirdParties.Add(new KsefThirdParty
{
    Role = KsefThirdPartyRole.Recipient,
    Party = new KsefParty
    {
        Nip = "2222222222",             // albo EuVatNumber / ForeignTaxId; null => BrakID
        Name = "Nabywca S.A. Oddział w Gdańsku",
        AddressLine1 = "ul. Portowa 7",
        AddressLine2 = "80-001 Gdańsk"
    }
});
```

Adres w `Podmiot3` jest w schemacie opcjonalny: podmiot bez obu linii adresu nie dostaje `Adres`,
a podmiot znany tylko z „kod miasto” ma tę linię wpisaną jako `AdresL1` (schemat wymaga pierwszej
linii). Role 7–10 (JST i grupy VAT) wymagają dodatkowo znaczników `JST`/`GV` na nabywcy i zwykle
`IDWew` — tego generator jeszcze nie wystawia (na `Podmiot2` zawsze idzie `JST=2`, `GV=2`).

Korekta samych danych odbiorcy (bez zmiany kwot) to zwykła faktura `KOR` z identycznymi wierszami
`StanPrzed` i „po”, zerowymi agregatami i `Podmiot3` z nowym odbiorcą — test
`GeneratedRecipientOnlyCorrection_IsValidAgainstOfficialFa3Xsd` pilnuje, że taki dokument przechodzi XSD.

Przy okazji od 8.0.19 pusta druga linia adresu (`AdresL2`) nie jest już wystawiana — schemat nie
przyjmuje w niej pustego ciągu.

Warstwy niższego poziomu (`IKsefApiClient`, `IKsefAuthenticationService`, `IFa3XmlGenerator`, `KsefCryptography`, `KsefXadesSigner`) są publiczne — można ich użyć bezpośrednio, np. do własnej orkiestracji wsadowej.

## Logowanie certyfikatem zamiast tokenem

```json
{
  "Ksef": {
    "Environment": "Test",
    "Nip": "5555555555",
    "AuthMode": "Certificate",
    "Certificate": {
      "Pkcs12Path": "certs/ksef.p12",
      "Password": "***",
      "VerifyCertificateChain": false
    }
  }
}
```

`Pkcs12Base64` przyjmuje ten sam materiał w Base64 — tak wygodniej, gdy certyfikat leży w bazie, a nie na dysku.
`VerifyCertificateChain` musi być `false` dla certyfikatów self-signed (akceptuje je wyłącznie środowisko TEST).
Klucz prywatny ładowany jest z flagą `EphemeralKeySet`, więc proces serwerowy nie zaśmieca magazynu kluczy użytkownika.

## Pobieranie faktur kosztowych

```csharp
var faktury = await queryService.QueryAsync(new KsefInvoiceQueryFilter
{
    From = DateTimeOffset.UtcNow.AddDays(-30),
    To = DateTimeOffset.UtcNow,
    SubjectType = InvoiceQuerySubjectType.Subject2   // dokumenty, w których jestem nabywcą
});

var xml = await queryService.DownloadInvoiceXmlAsync(faktury[0].KsefNumber);
```

## Wiele firm w jednym procesie

```csharp
services.AddITBeesKsefClientFactory();   // bez sekcji "Ksef" w konfiguracji

var options = new KsefOptions { Nip = firma.Nip, AuthMode = KsefAuthMode.Certificate, Certificate = ... };
var wysylka = ksefClientFactory.CreateInvoiceService(options);
var pobieranie = ksefClientFactory.CreateQueryService(options);
ksefClientFactory.InvalidateAuthentication(options);  // po zmianie poświadczeń firmy
```

## Poświadczenia per firma (ITBees.Ksef.Credentials)

`IKsefClientFactory` przyjmuje `KsefOptions` w czasie żądania, ale skądś trzeba je wziąć. Ten pakiet
dokłada brakujący kawałek: tabelę z poświadczeniem każdej firmy (token **albo** certyfikat), szyfrowanie
sekretów (AES-256-GCM), endpointy `/KsefCredential` i `/KsefConnectionTest` oraz `ResolveOptions()`,
które buduje `KsefOptions` dla firmy z bieżącego żądania.

```csharp
// 1. DbContext.OnModelCreating — encja nie zna Twojego typu firmy, więc klucz obcy dokładasz sam:
KsefCredentialsDbModelBuilder.RegisterDbModels(modelBuilder, entity =>
    entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyGuid).OnDelete(DeleteBehavior.Cascade));
// + DbSet<KsefCredential> KsefCredentials { get; set; }  (nazwa DbSetu = nazwa tabeli)
// + dotnet ef migrations add AddKsefCredentials

// 2. Rejestracja DI:
services.AddScoped<IKsefCompanyContext, MojAdapterFirmy>();   // wymagane
services.AddScoped<IKsefCredentialAuditSink, MojDziennik>();  // opcjonalne
services.AddKsefCredentials(o =>
{
    o.EncryptionKey = configuration["Secrets:EncryptionKey"];  // 32 bajty w Base64
    o.SystemInfo = "MojaAplikacja";
});

// 3. Endpointy leżą w tym assembly, więc skan hosta ich nie znajdzie:
services.AddControllers().AddKsefCredentialControllers();
```

Host dostarcza jeszcze `IReadOnlyRepository<KsefCredential>` i `IWriteOnlyRepository<KsefCredential>`
(`ITBees.Interfaces`). Od tego momentu:

```csharp
var options = ksefCredentialService.ResolveOptions();     // poświadczenie firmy z bieżącego żądania
var wysylka = ksefClientFactory.CreateInvoiceService(options);
```

Czego pakiet **nie** robi: nie wystawia i nie pobiera faktur — to zostaje w aplikacji, bo model faktury
jest jej własny. Pakiet odpowiada wyłącznie za „czym ta firma loguje się do KSeF”.

Uwagi:

- **Sekrety nie wychodzą z serwera.** `KsefCredentialVm` oddaje zamaskowany token i metadane certyfikatu,
  nigdy treści. Dziennik dostaje `KsefCredentialAuditView` — projekcję bez sekretów, więc token nie ma
  którędy wyciec nawet wtedy, gdy host zserializuje wszystko, co dostanie.
- **Podmiana `EncryptionKey` unieważnia wszystkie zapisane poświadczenia** — trzeba je wprowadzić od nowa.
- **Każde zapytanie jest przycinane do firmy z `IKsefCompanyContext`.** Implementacja, która ufa
  identyfikatorowi z żądania, pozwoliłaby jednej firmie odczytać poświadczenie drugiej.
- Zapis poświadczenia unieważnia sesję w cache fabryki, więc stary token przestaje działać od razu.

## Uwaga operacyjna

`SendInvoiceAsync` wykonuje pełny cykl z pollingiem statusów (auth + przetwarzanie faktury) — **nie wywołuj go synchronicznie z webhooka** (np. Stripe, timeout ~10 s). Wywołuj z zadania w tle / kolejki.

## Integracja z FAS (ITBees.Ksef.Fas)

Dla aplikacji na stacku ITBees.FAS.Payments (np. płatności Stripe) wystarczą trzy kroki:

```csharp
// 1. DbContext.OnModelCreating — rejestracja tabeli outboxu KsefInvoiceRecord:
ITBees.Ksef.Fas.Setup.KsefFasDbModelBuilder.RegisterDbModels(modelBuilder);
// + dotnet ef migrations add AddKsefInvoiceRecord

// 2. Rejestracja DI (klient KSeF + serwis + worker tła):
builder.Services.AddKsefFasInvoicing<MyAppDbContext>(configuration); // sekcja "Ksef"

// 3. Sekcja "Ksef" w konfiguracji (jak wyżej w szybkim starcie).
```

Worker co 60 s wystawia faktury FA(3) dla sesji `Finished && Success && !Refunded && !InvoiceCreated`
(pokrywa webhook, potwierdzenie z redirectu i odnowienia subskrypcji), nadaje numery `FV/{n}/{MM}/{rrrr}`
(unikalny indeks per miesiąc), archiwizuje XML + UPO w `KsefInvoiceRecord` i ponawia błędy do 10 razy.
Idempotencję gwarantują `PaymentSession.InvoiceCreated` oraz unikalny indeks na `PaymentSessionGuid`.
Plany darmowe/trialowe dostają status `Skipped`. Dopóki `Ksef:KsefToken` jest pusty, worker nic nie robi.

## Testy

`dotnet test` — testy jednostkowe kryptografii, serializacji kontraktów API 2.0, flow uwierzytelniania (mock HTTP) oraz walidacja generowanego XML względem oficjalnego `schemat_FA(3)_v1-0E.xsd`.

`TestKsefConsoleApp` — interaktywny smoke test na środowisku TEST (uzupełnij `Ksef:KsefToken` i `Ksef:Nip` w `appsettings.json`).
