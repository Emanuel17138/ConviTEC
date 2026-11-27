using System;
using ConviTEC.Inventario.Api;

namespace ConviTEC.Inventario.Tests.Api;

public class ConfiguracionSedeTests
{
    [Fact]
    public void Leer_ConArgumentos_UsaSedeDatosYUrl()
    {
        ConfiguracionSede configuracion = ConfiguracionSede.Leer(
            new[] { "--sede", "LIB", "--datos", "datos/lib", "--url", "http://localhost:5101" });

        Assert.Equal("LIB", configuracion.SedeId);
        Assert.Equal("datos/lib", configuracion.DirectorioDatos);
        Assert.Equal("http://localhost:5101", configuracion.Url);
    }

    [Fact]
    public void Leer_SinUrl_UsaLaPorDefecto()
    {
        ConfiguracionSede configuracion = ConfiguracionSede.Leer(new[] { "--sede", "LIB", "--datos", "datos/lib" });

        Assert.Equal(ConfiguracionSede.UrlPorDefecto, configuracion.Url);
    }

    [Fact]
    public void Leer_SedeEsSensibleAMayusculas()
    {
        ConfiguracionSede configuracion = ConfiguracionSede.Leer(new[] { "--sede", "Lib", "--datos", "d" });

        Assert.Equal("Lib", configuracion.SedeId);
    }

    [Fact]
    public void Leer_SinSede_Falla()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ConfiguracionSede.Leer(new[] { "--datos", "datos/lib" }));

        Assert.Contains("sede", error.Message);
    }

    [Fact]
    public void Leer_SinDirectorioDeDatos_Falla()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ConfiguracionSede.Leer(new[] { "--sede", "LIB" }));

        Assert.Contains("datos", error.Message);
    }

    [Fact]
    public void Leer_OpcionSinValorFinal_SeIgnoraYFalta()
    {
        Assert.Throws<ArgumentException>(() => ConfiguracionSede.Leer(new[] { "--datos", "d", "--sede" }));
    }
}
