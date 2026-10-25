# StudioUSB

Slovenská aplikácia pre Windows 11, ktorá pripraví fotografie na USB.

## Funkcie

- nastaviteľná kvalita JPEG (predvolene 80 %);
- zmenšenie na dlhšiu stranu 2400 px so zachovaním proporcií a EXIF orientácie;
- nastaviteľná prípona názvu (predvolene `-websize`);
- nastaviteľné názvy webového a tlačového adresára (predvolene `WEBSIZE` a `PRINTSIZE`);
- nezmenené originály v tlačovom adresári s overením SHA-256;
- prázdny TXT súbor pomenovaný podľa zdrojového adresára;
- voliteľné formátovanie s nastaviteľným bezpečnostným limitom 4, 8, 16, 32, 64, 128 alebo 256 GiB;
- FAT32 pre médiá do 32 GiB, exFAT pre väčšie médiá;
- priebehový protokol a záverečná kontrola výstupu.

## Použitie

1. Spustite `StudioUSB.exe`.
2. Vyberte zdrojový adresár s JPG/JPEG fotografiami.
3. Vyberte cieľové vymeniteľné USB médium.
4. Upravte kvalitu, príponu a názvy výstupných adresárov.
5. Voliteľne zapnite formátovanie, zadajte názov média a bezpečnostný limit.
6. Kliknite na **Spustiť**.

Nastavenia sa ukladajú do `%LOCALAPPDATA%\StudioUSB\settings.json`. Zdrojový adresár sa neukladá. Formátovanie vyžaduje potvrdenie v aplikácii a následne UAC oprávnenie Windows.

## Požiadavky

- Windows 11 x64
- Microsoft .NET 8 Desktop Runtime

## Zostavenie

```powershell
dotnet restore StudioUSB.csproj --ignore-failed-sources
dotnet build StudioUSB.csproj -c Release --no-restore
```
