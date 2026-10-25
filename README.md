# StudioHELPER

StudioHELPER je modulárna desktopová aplikácia pre fotografické štúdio. Aktuálna verzia `0.1.0` podporuje Windows 11; architektúra oddeľuje platformovo neutrálne jadro od hostiteľskej aplikácie, aby bolo možné neskôr pridať macOS a Linux.

## Rozhranie

- nástroje sú v zvislom paneli ikon na ľavom okraji;
- po spustení sa zobrazí prázdna úvodná karta s výberom jazyka `System / English / Slovensky`;
- nástroj sa načíta až po kliknutí na jeho ikonu;
- ikona nastavení je naspodku panela a obsahuje spúšťanie so systémom, minimalizáciu do systémovej lišty a aktualizácie;
- prvým nástrojom je **StudioUSB**.

## StudioUSB

StudioUSB pripravuje webové a tlačové JPG/JPEG súbory, podporuje dlhšiu alebo kratšiu cieľovú stranu, PX/CM, 72/240/300 dpi, samostatnú JPEG kvalitu, voliteľné prevzorkovanie tlačového výstupu, ochranu pred zväčšením a vlastné názvy priečinkov aj príponu súborov. Nezmenené originály overuje cez SHA-256.

Voliteľné formátovanie povoľuje iba vymeniteľné médium do nastaveného limitu 4–256 GiB. Do 32 GiB používa FAT32, nad 32 GiB exFAT.

## Projekty

- `src/StudioHELPER.Core` – platformovo neutrálne nastavenia, lokalizácia a model nástrojov;
- `src/StudioHELPER.Windows` – WPF hostiteľ a Windows implementácia StudioUSB;
- `installer` – používateľský Windows inštalátor bez potreby administrátorských práv;
- `docs/ARCHITECTURE.md` – pravidlá rozširovania na ďalšie nástroje a platformy.

## Zostavenie

```powershell
dotnet restore StudioHELPER.sln --ignore-failed-sources
dotnet build StudioHELPER.sln -c Release --no-restore
```

## Vytvorenie inštalátora

```powershell
.\installer\build-installer.ps1
```

Výsledok: `artifacts/StudioHELPER-Setup.exe`.

Inštalátor umiestni aplikáciu do `%LOCALAPPDATA%\Programs\StudioHELPER`, pridá odkaz do ponuky Štart a zaregistruje odinštalovanie pre aktuálneho používateľa. Aplikácia vyžaduje Microsoft .NET 8 Desktop Runtime.

## Aktualizácie

Aplikácia kontroluje najnovší verejný GitHub Release repozitára `fct-sk/StudioHELPER`. Pri novšej verzii zobrazí indikátor pri nastaveniach. Používateľ môže jedným kliknutím stiahnuť SHA-256 overený `StudioHELPER-Setup.exe`, spustiť aktualizáciu a reštartovať aplikáciu.

Workflow pri tagu `v*` automaticky vytvorí GitHub Release s inštalátorom a súborom `StudioHELPER-Setup.exe.sha256`. Repozitár a Releases musia byť verejné, aby aktualizácie fungovali bez prihlásenia používateľa.
