# StudioUSB 1.1.0

Slovenská/anglická aplikácia pre Windows 11, ktorá pripraví fotografie na USB.

## Výstupy

### PRINTSIZE

- predvolene kopíruje pôvodné JPG/JPEG bez zmeny a overí ich cez SHA-256;
- voliteľné **Prevzorkovať** aktivuje vlastnú veľkosť a kvalitu JPEG;
- nastaviteľná dlhšia alebo kratšia strana;
- veľkosť v PX alebo CM;
- pri CM výber 72 dpi (screen), 240 dpi (press) alebo 300 dpi (print);
- voľba **Nezväčšovať** zachová pôvodné rozmery, ak je cieľ väčší.

### WEBSIZE

- predvolene dlhšia strana 2400 px, JPEG 80 % a prípona `-websize`;
- vlastná kvalita, prípona, dlhšia/kratšia strana, PX/CM, DPI a voľba **Nezväčšovať**.

Názvy oboch výstupných adresárov sú nastaviteľné. Aplikácia vytvorí aj prázdny TXT súbor pomenovaný podľa zdrojového adresára.

## Ďalšie funkcie

- jazyk `System`, `English` alebo `Slovensky`;
- uloženie všetkých nastavení okrem zdrojového adresára;
- voliteľné formátovanie s bezpečnostným limitom 4, 8, 16, 32, 64, 128 alebo 256 GiB;
- FAT32 pre médiá do 32 GiB, exFAT pre väčšie médiá;
- priebehový protokol a záverečná kontrola výstupu.

Nastavenia sú v `%LOCALAPPDATA%\StudioUSB\settings.json`. Formátovanie vyžaduje potvrdenie v aplikácii a následne UAC oprávnenie Windows.

## Požiadavky

- Windows 11 x64
- Microsoft .NET 8 Desktop Runtime

## Zostavenie

```powershell
dotnet restore StudioUSB.csproj --ignore-failed-sources
dotnet build StudioUSB.csproj -c Release --no-restore
```
