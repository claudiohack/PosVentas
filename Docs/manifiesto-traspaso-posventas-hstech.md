# MANIFIESTO DE TRASPASO Y DESARROLLO INTEGRAL: POSVENTAS BY HS TECH

## 1. Contexto Institucional y Visión de Marca

El presente documento constituye la directriz técnica, conceptual y estratégica para la creación del entorno de desarrollo dedicado a **PosVentas**, la nueva unidad de negocio B2B de **HS Tech**.

HS Tech (`hstech.com.ar`) es una firma de desarrollo de software orientada a construir un ecosistema de soluciones comerciales en la nube. La empresa opera bajo una arquitectura de marca multiproducto:

* **Sitio Institucional:** `hstech.com.ar` (Landing comercial estática y catálogo de productos alojado en Cloudflare Pages).
* **ContratistaSaaS:** `app.hstech.com.ar` (SaaS especializado en estimaciones y costos para contratistas en EE.UU.).
* **PosVentas:** `pos.hstech.com.ar` (Sistema de Punto de Venta, Gestión de Inventario y Cuentas Corrientes para comercios).

La landing institucional ya cuenta con la sección activa en `hstech.com.ar/posventas`, captando clientes interesados mediante un programa de Beta Privada con beneficio de 50% de descuento de por vida. El nuevo espacio de trabajo debe continuar esta visión con absoluta coherencia técnica e institucional.

---

## 2. Alcance Funcional de PosVentas (Los 4 Pilares del Producto)

El desarrollo de PosVentas debe estructurarse rigurosamente alrededor de cuatro módulos nucleares:

### 2.1. Punto de Venta (POS) Ágil y Mostrador
* **Emisión de Comprobantes:** Facturación rápida de tickets, notas de venta y comprobantes digitales.
* **Múltiples Medios de Pago:** Cobro integrado en efectivo, tarjetas de crédito/débito, transferencias y código QR.
* **Operativa de Mostrador:** Lectura por código de barras, buscador rápido de artículos y aplicación de descuentos o promociones.
* **Control de Caja:** Arqueo, apertura y cierre de caja diario automatizado con registro de desfasajes.
* **Resiliencia Operativa:** Arquitectura preparada para mantener la disponibilidad de datos y sincronización continua en tiempo real.

### 2.2. Control de Inventario y Stock en Tiempo Real
* **Monitoreo de Existencias:** Actualización automática del stock ante cada venta o devolución.
* **Catálogo Organizado:** Clasificación por categorías, marcas, proveedores y variantes (talle, color, presentación).
* **Alertas de Reposición:** Configuración de umbrales de stock mínimo crítico con notificaciones de reabastecimiento.
* **Carga Masiva de Productos:** Importación y actualización de listas de precios desde archivos CSV/Excel.
* **Valoración de Inventario:** Cálculo de costo total de depósito y margen bruto teórico en tiempo real.

### 2.3. CRM y Cuentas Corrientes de Clientes
* **Ficha Unificada de Cliente:** Historial completo de compras, datos de contacto y frecuencia de consumo.
* **Gestión de Cuentas Corrientes:** Control estricto de saldos adeudados, entregas a cuenta y recibos de pago.
* **Límites de Crédito:** Restricción de ventas a crédito según el margen máximo autorizado por cliente.
* **Segmentación Comercial:** Identificación de clientes habituales para campañas promocionales.

### 2.4. Reportes Financieros y Métricas de Negocio
* **Tablero Ejecutivo:** Indicadores clave de ventas diarias, semanales y mensuales en tiempo real.
* **Ranking de Productos:** Identificación de los artículos más vendidos y de mayor margen de contribución.
* **Rendimiento Financiero:** Métricas de margen de ganancia neto, ingresos por medio de pago y horas pico de facturación.
* **Exportación de Datos:** Descarga de reportes en formatos PDF y Excel estructurados.

---

## 3. Arquitectura Técnica e Infraestructura

El desarrollo de PosVentas compartirá la filosofía de ingeniería de HS Tech, optimizando costos operativos y garantizando escalabilidad industrial.

### 3.1. Estrategia de Despliegue e Infraestructura
* **Servidor de Aplicación (Backend):** Servidor Privado Virtual (VPS) en Linux (Hetzner o DigitalOcean) ejecutando contenedores Docker o servicios .NET / Node.js administrados por Nginx como proxy inverso.
* **Base de Datos:** Motor relacional (PostgreSQL / SQL Server) administrado con migraciones estrictas y control de esquemas.
* **Dominio & DNS:** Registro CNAME/A en Cloudflare apuntando a `pos.hstech.com.ar` (o subdominio asignado), manteniendo la protección SSL/TLS y la red CDN.

### 3.2. Aislamiento Multi-Tenant (Seguridad Obligatoria)
* **Filtrado por Tenant:** Cada tabla relacional (Ventas, Productos, Clientes, Movimientos de Caja) debe incluir una clave `TenantId` o `ComercioId`.
* **Escudo en Controladores/APIs:** Todas las consultas y mutaciones de datos deben verificar de forma estricta que la entidad pertenezca al `TenantId` del usuario autenticado en el token JWT. Queda terminantemente prohibido acceder o editar registros entre diferentes comercios.

---

## 4. Reglas de Oro de Ingeniería en HS Tech

Todo desarrollo en el nuevo chat debe alinearse con los estándares inviolables de la empresa:

1. **Cero Secretos Hardcodeados:** Claves privadas, cadenas de conexión y tokens de API deben residir exclusivamente en variables de entorno (`User-Secrets` en desarrollo local).
2. **Migraciones Estrictas:** Queda prohibida la modificación manual del esquema de base de datos. Todo cambio debe realizarse mediante migraciones code-first versionadas en Git.
3. **Validación Rígida de Entradas:** Implementar validaciones DataAnnotations / FluentValidation en todos los DTOs para impedir precios negativos, stock inconsistente o desbordamiento de tipos de datos.
4. **Respuesta Rápida y Limpia:** Controladores orientados a respuestas HTTP estandarizadas (`200 OK`, `400 BadRequest`, `401 Unauthorized`, `404 NotFound`).
5. **Evolución Modular:** Diseñar la API de PosVentas contemplando futuras integraciones con la landing institucional y otros SaaS del ecosistema HS Tech.

---

## 5. Hoja de Ruta Inicial para el Nuevo Entorno

Al iniciar el chat en el nuevo espacio de trabajo de PosVentas, los primeros pasos operativos serán:

1. **Definición del Stack Tecnológico Exacto:** Confirmar el framework backend y frontend a utilizar (ej. .NET API + Blazor / React / Vue).
2. **Modelado del Dominio:** Diseñar el esquema de entidades de la base de datos (Comercio, Usuario, Producto, Categoria, Venta, DetalleVenta, Cliente, MovimientoCaja).
3. **Configuración de Autenticación y Multi-Tenant:** Implementar el registro, login con JWT y middleware de contexto de comercio.
4. **Construcción del Core del POS:** Desarrollar el motor de facturación rápida y control de inventario.
