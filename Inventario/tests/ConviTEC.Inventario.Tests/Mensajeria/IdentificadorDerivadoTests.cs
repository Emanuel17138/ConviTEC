using System;
using ConviTEC.Inventario.Mensajeria;

namespace ConviTEC.Inventario.Tests.Mensajeria;

public class IdentificadorDerivadoTests
{
    [Fact]
    public void MismoComando_MismoIdentificador()
    {
        Assert.Equal(IdentificadorDerivado.DeComando("m-1"), IdentificadorDerivado.DeComando("m-1"));
    }

    [Fact]
    public void ComandosDistintos_IdentificadoresDistintos()
    {
        Assert.NotEqual(IdentificadorDerivado.DeComando("m-1"), IdentificadorDerivado.DeComando("m-2"));
        Assert.NotEqual(IdentificadorDerivado.DeComando("m-1"), IdentificadorDerivado.DeComando("M-1"));
    }

    [Fact]
    public void Resultado_EsUuidVersion5ConVarianteRfc()
    {
        string id = IdentificadorDerivado.DeComando("20000000-0000-4000-8000-000000000001");

        Assert.True(Guid.TryParseExact(id, "D", out _), id);
        Assert.Equal('5', id[14]);
        Assert.Contains(id[19], "89ab");
        Assert.Equal(id.ToLowerInvariant(), id);
    }

    [Fact]
    public void ComandoVacio_Lanza()
    {
        Assert.Throws<ArgumentException>(() => IdentificadorDerivado.DeComando(""));
        Assert.Throws<ArgumentNullException>(() => IdentificadorDerivado.DeComando(null!));
    }
}
