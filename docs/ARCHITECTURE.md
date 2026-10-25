# Architektúra StudioHELPER

## Vrstvy

```text
StudioHELPER.Core
    nastavenia, lokalizácia, opis nástrojov
             ↑
StudioHELPER.Windows
    Windows shell + platformové moduly
             ↑
Windows installer
```

`StudioHELPER.Core` cieli na čisté `net8.0` a nesmie odkazovať na WPF, WinForms ani Windows API. Budúci macOS a Linux hostiteľ použije rovnaké modely nastavení a lokalizačné kľúče.

## Nástroje

Každý nástroj má vlastný adresár v platformovom hostiteľovi. StudioUSB je v `Tools/StudioUsb` a vytvára sa len pri prvom otvorení. Ďalší nástroj sa pridá novou položkou ľavého navigačného panelu a samostatným view/modulom bez zásahu do existujúcich nástrojov.

## Platformy

- **Windows:** WPF a Windows špecifické spracovanie diskov.
- **macOS:** budúci hostiteľ nahradí iba UI a implementáciu práce s médiami/obrázkami.
- **Linux:** rovnaký princíp ako macOS.

Obrazové a diskové operácie majú zostať mimo platformovo neutrálneho jadra. V ďalšej iterácii je vhodné vyčleniť ich z code-behind StudioUSB do rozhrania procesora a platformových adaptérov.

## Aktualizácie

`GitHubUpdateService` je Windows adaptér pre GitHub Releases. Kontroluje verzie, sťahuje `StudioHELPER-Setup.exe`, overuje sprievodný SHA-256 súbor a spúšťa setup v režime aktualizácie. `UpdateInfo` je v platformovo neutrálnom jadre, aby budúce hostiteľské aplikácie mohli použiť vlastný transport a inštalátor.

## Nastavenia

Spoločný súbor je v platformovom používateľskom priečinku `StudioHELPER/settings.json`. Zdrojový adresár StudioUSB sa zámerne neukladá. Pri prvom spustení sa podporuje migrácia starých nastavení z `StudioUSB/settings.json`.
