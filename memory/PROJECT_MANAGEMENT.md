# Gestión del Proyecto (PMI)

## 1. Acta de Constitución
*   **Nombre**: Licensing System v2.0
*   **Objetivos**: Introducir "Versiones" (free/premium/enterprise), generación masiva de keys (Excel), retrocompatibilidad total.
*   **Restricciones**: El esquema de DB y API pueden modificarse (restricciones originales suspendidas).

## 2. Alcance (WBS)
*   Refactorización del modelo de datos (`LicensingCore`).
*   Actualización logística (`LicensingAdmin`/`LicensingApi`).
*   Desarrollo de herramientas administrativas (Bulk Gen, Excel).

## 3. Riesgos
*   **Impacto alta**: Rotura de compatibilidad de firmas v1.0. **Mitigación**: Pruebas unitarias de firmas existentes antes y después de cambios.
*   **Impacto moderado**: Rendimiento en emisión masiva. **Mitigación**: Uso de procesos en segundo plano para generación de keys.
