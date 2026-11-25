# Guía de Fuentes y Gobernanza para el Nuevo Cuaderno: PosVentas (HS Tech)

Esta guía establece el catálogo de fuentes recomendado y las directrices de gobernanza ejecutiva para inicializar el nuevo cuaderno en NotebookLM dedicado al desarrollo integral de **PosVentas**, la solución de punto de venta, control de inventario y cuentas corrientes de **HS Tech**.

---

## 1. Visión y Rol del CEO / Líder Técnico Senior

Al interactuar en el nuevo espacio de trabajo, el asistente debe adoptar un rol de **Socio Tecnológico y Director de Ingeniería (CTO)**, manteniendo los siguientes pilares de gobernanza:

* **Arquitectura Limpia y Desacoplada (*Clean Architecture*):** Separación estricta entre las capas de Dominio, Aplicación, Infraestructura (Persistencia EF Core) y Presentación (WebAPI / Blazor WebAssembly).
* **Aislamiento Multi-Tenant Estricto:** Validación obligatoria del identificador de comercio/organización (`TenantId`) en el 100% de las consultas y comandos, garantizando la privacidad e integridad de los datos.
* **Resiliencia Operativa en Mostrador:** Diseño centrado en alta disponibilidad, procesamiento ágil de transacciones en vivo y resiliencia ante cortes temporales de conectividad.

---

## 2. Catálogo de Fuentes Recomendadas para el Nuevo Cuaderno

Para que el nuevo modelo disponga de todo el contexto estratégico, técnico y comercial de **HS Tech**, se sugiere cargar la siguiente documentación agrupada por bloques:

### A. Contexto Institucional y Arquitectura Base
1. **`manifiesto-traspaso-posventas-hstech.md`** *(Generado en este cuaderno)*: Define el alcance del producto, su integración con la web institucional `hstech.com.ar` y la visión de ecosistema SaaS B2B.
2. **`MANIFIESTO_ARQUITECTURA_Y_REGLAS_DE_ORO.md`**: Establece las reglas de oro de ingeniería, el uso exclusivo de migraciones de Entity Framework Core, la prohibición de valores *hardcodeados* y la gestión segura de secretos.

### B. Especificaciones de Negocio y Dominio Comercial (POS)
3. **Requisitos Operativos de Punto de Venta (POS):** Documento detallado con flujos de cobranza en mostrador (efectivo, tarjetas, transferencias, QR), arqueo y cierre de caja diario, gestión de promociones y emisión de tickets.
4. **Modelo de Inventario y Gestión de Stock:** Definición de catálogo de artículos, variaciones (talles, colores, categorías), lectura de código de barras, stock mínimo, alertas de reposición y valoración de inventario en tiempo real.
5. **CRM & Cuentas Corrientes de Clientes:** Especificación de fichas de clientes, límites de crédito comercial, control de saldos adeudados, entregas a cuenta y recordatorios de cobranza.

### C. Estándares Técnicos y Calidad de Código (.NET / Blazor / EF Core)
6. **Patrones de Diseño y DTOs:** Definición de DTOs con validaciones DataAnnotations estrictas contra valores negativos, desbordamientos de datos y caracteres no válidos.
7. **Esquema de Relaciones e Integridad Referencial:** Reglas para la definición de claves foráneas con restricción de borrado (`Restrict`), índices únicos y optimización de consultas LINQ.
8. **Estrategia de Seguridad, JWT y Autenticación:** Especificaciones de control de acceso basado en roles (`RBAC`/`PBAC`), encriptación de contraseñas con BCrypt, Rate Limiting y políticas de CORS.

### D. Infraestructura, Despliegue y Periféricos
9. **Manual de Despliegue en VPS Linux (Docker / Nginx / Kestrel):** Configuración de servidores para alojar las APIs de HS Tech, proxy inverso Nginx, variables de entorno y certificados SSL/TLS vía Cloudflare.
10. **Especificaciones de Hardware y Periféricos:** Documentación de integración con impresoras térmicas de comprobantes (protocolo ESC/POS), balanzas comerciales y lectores de código de barras USB/Bluetooth.

---

## 3. Guía de Interacción y Prompts para el Nuevo Cuaderno

Al realizar solicitudes en el nuevo cuaderno, se recomienda utilizar el siguiente encuadre técnico:

> *"Actúa como CEO y Líder Técnico Senior de HS Tech. Vamos a diseñar e implementar el módulo [X] de PosVentas siguiendo los principios de Clean Architecture en .NET Core 8 / Blazor WASM. Exige validaciones estrictas en DTOs, filtrado obligatorio por TenantId, manejo explícito de excepciones y código listo para producción sin valores hardcodeados."*

---

## 4. Checklist de Inicialización

1. Crear el nuevo cuaderno con el nombre **"PosVentas - HS Tech"**.
2. Subir el documento `manifiesto-traspaso-posventas-hstech.md` como primera fuente de verdad.
3. Incorporar los documentos de requisitos de Negocio (POS, Inventario, CRM) y las especificaciones de arquitectura .NET.
4. Iniciar la primera sesión fijando el rol de liderazgo técnico para diseñar la capa de dominio y el modelo de datos inicial.
