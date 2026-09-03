# LicensingSystem — instrucciones para agentes

Sistema de licencias de un vendor: `LicensingApi` (API de activación), `LicensingAdmin` (panel
Blazor Server) y `LicensingCore` (biblioteca compartida), sobre PostgreSQL.

## Comandos

| Tarea | Comando |
|---|---|
| Restaurar | `dotnet restore LicensingSystem.sln` |
| Compilar | `dotnet build LicensingSystem.sln` |
| Compilar estricto | `dotnet build LicensingSystem.sln -warnaserror` |
| Pruebas (toda la suite) | `dotnet test` |
| Pruebas (una clase) | `dotnet test --filter <NombreClase>` |
| Ejecutar panel | `dotnet run --project LicensingAdmin` |
| Secreto local | `dotnet user-secrets set --project <proj> "<clave>" "<valor>"` |

**Gate:** `dotnet build LicensingSystem.sln && dotnet test` en verde antes de marcar nada como hecho.

## Reglas que muerden

1. Sin secretos en `appsettings.json` — cadena de conexión y PEM van por `dotnet user-secrets` / env,
   y `ConnectionStringGuard.Require` falla ruidoso si la cadena está vacía.
2. Toda acción admin que muta datos escribe un `AuditLogEntry` con el email del admin autenticado.
3. No tocar los contratos congelados: `/api/activate`, `/api/checkin`, `/health`, el enum
   `ResultCode`, el sobre de `LicenseFileService`, la regex `LicenseKeyFormat()`, ni el esquema.

Arquitectura completa, límites entre capas y convenciones: ver `CLAUDE.md` en este directorio.
