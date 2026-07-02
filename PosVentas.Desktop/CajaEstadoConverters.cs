using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PosVentas.Desktop;

public class BooleanToStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool estaAbierta)
        {
            return estaAbierta ? "● ABIERTA" : "○ CERRADA";
        }
        return "DESCONOCIDO";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BooleanToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool estaAbierta)
        {
            // Verde suave para Abierta, Gris oscuro/azul para Cerrada
            return estaAbierta ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2ECC71"))
                               : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95A5A6"));
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}