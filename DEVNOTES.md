# Notas de desarrollo

Decisión: mantener la verificación y el flujo de apertura de "caja" (CajaSesion) dentro de la vista de Punto de Venta (VentasView) y su ViewModel (VentasViewModel).

Razonamiento:
- Evita mostrar diálogos modales de apertura durante OnStartup, lo que puede entrar en conflicto con el ciclo de vida de la aplicación WPF y provocar excepciones (p. ej. "El objeto Application se va a cerrar").
- Centraliza la responsabilidad de control de caja en el módulo de ventas donde el usuario interactúa con la funcionalidad.

Implementación actual:
- VentasView / VentasViewModel: al cargar la vista se comprueba si existe una sesión de caja activa. Si no, se propone al usuario seleccionar una caja y abrirla mediante AperturaCajaWindow.
- El flujo exige interacción explícita del usuario (selección de caja y entrada de monto). Si el usuario cancela, la UI de ventas sigue accesible pero el comando COBRAR queda deshabilitado hasta que se abra una caja.

Cómo cambiar esta política:
- Si se desea ejecutar la comprobación justo después del login, implementar una ventana/modal independiente (no en OnStartup) que gestione solo la selección/apertura de caja antes de mostrar MainWindow.
- Alternativamente, mover la lógica a MainWindow pero sin lanzar diálogos modales durante OnStartup; mostrar un banner o modal tras MainWindow.Show() para evitar problemas de ciclo de vida.

Ubicación de código relevante:
- PosVentas.Desktop/ViewModels/VentasViewModel.cs
- PosVentas.Desktop/VentasView.xaml.cs
- PosVentas.Desktop/AperturaCajaWindow.xaml.cs
