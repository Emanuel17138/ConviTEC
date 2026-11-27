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
    public void Leer_SinRemotas_ListaVacia()
    {
        ConfiguracionSede configuracion = ConfiguracionSede.Leer(new[] { "--sede", "LIB", "--datos", "d" });

        Assert.Empty(configuracion.SedesRemotas);
    }

    [Fact]
    public void Leer_ConRemotas_LasSeparaPorComasYRecortaEspacios()
    {
        ConfiguracionSede configuracion = ConfiguracionSede.Leer(
            new[] { "--sede", "LIB", "--datos", "d", "--remotas", "SJ, ALA ,LIM" });

        Assert.Equal(new[] { "SJ", "ALA", "LIM" }, configuracion.SedesRemotas);
    }

    [Fact]
    public void Leer_RemotasVacias_ListaVacia()
    {
        ConfiguracionSede configuracion = ConfiguracionSede.Leer(
            new[] { "--sede", "LIB", "--datos", "d", "--remotas", "  " });

        Assert.Empty(configuracion.SedesRemotas);
    }

    [Theory]
    [InlineData("SJ,SJ")]
    [InlineData("SJ,,ALA")]
    [InlineData("LIB,SJ")]
    public void Leer_RemotasRepetidasVaciasOIgualesALaPropia_Fallan(string remotas)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ConfiguracionSede.Leer(new[] { "--sede", "LIB", "--datos", "d", "--remotas", remotas }));

        Assert.Contains("remota", error.Message);
    }

    [Fact]
    public void Leer_RemotasDistinguenMayusculas()
    {
        ConfiguracionSede configuracion = ConfiguracionSede.Leer(
            new[] { "--sede", "LIB", "--datos", "d", "--remotas", "Lib,LIB2" });

        Assert.Equal(new[] { "Lib", "LIB2" }, configuracion.SedesRemotas);
    }

    [Fact]
    public void Leer_OpcionSinValorFinal_SeIgnoraYFalta()
    {
        Assert.Throws<ArgumentException>(() => ConfiguracionSede.Leer(new[] { "--datos", "d", "--sede" }));
    }
}
