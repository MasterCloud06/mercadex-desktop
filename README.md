# Mercadex Desktop

Aplicación de escritorio (WPF, .NET 10) para la caja del mercado: cobros, recibos,
egresos, arqueo, padrón, mapa del mercado e importación desde Excel.

Se conecta a la API de [mercadex-backend](https://github.com/MasterCloud06/mercadex-backend).

## Ramas

- `develop`: trabajo diario.
- `main`: lo que se publica como instalador.

## Entornos

| Entorno | API |
|---|---|
| Producción | `appsettings.json` → URL de Render |
| Desarrollo | variable `MERCADEX_API_URL=http://127.0.0.1:5189/` |

```powershell
# Ejecutar contra la API local
$env:MERCADEX_API_URL = "http://127.0.0.1:5189/"
dotnet run --project Mercadex.Desktop.App
```

## Pruebas

```powershell
dotnet test tests/Mercadex.Desktop.Tests
```

## Instalador

1. `dotnet publish Mercadex.Desktop.App -c Release -r win-x64 --self-contained -o Mercadex.Desktop.App/publish`
2. Compilar `Mercadex.Desktop.App/MercadexInstaller.iss` con Inno Setup (sale en `Mercadex.Desktop.App/installer`).
3. Subir el instalador como release en `mercadex-updates` y actualizar su `version.json` (URL y sha256).

La clave de la app (`X-Mercadex-Key`) nunca se sube al repositorio.
