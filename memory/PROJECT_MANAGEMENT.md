# Gestión del Proyecto (PMI)

## 1. Acta de Constitución
*   **Nombre**: Licensing System v2.0
*   **Objetivos**: Introducir "Versiones" (free/premium/enterprise), generación masiva de keys (Excel), retrocompatibilidad total.
*   **Restricciones**: El esquema de DB y API pueden modificarse (restricciones originales suspendidas durante el desarrollo).

## 2. Alcance (WBS)
*   Refactorización del modelo de datos (`LicensingCore`). [COMPLETADO]
*   Actualización logística (`LicensingAdmin`/`LicensingApi`). [COMPLETADO]
*   Desarrollo de herramientas administrativas (Bulk Gen, Excel). [COMPLETADO]
*   Migración .NET 8 a .NET 10 LTS. [COMPLETADO]

## 3. Riesgos
*   **Impacto alta**: Rotura de compatibilidad de firmas v1.0. (Mitigado exitosamente vía tests).
*   **Impacto moderado**: Rendimiento en emisión masiva. (Mitigado con lógica asíncrona).

## 4. Estado Final
*   Proyecto v2.0 desplegado y conmutado exitosamente a .NET 10.
