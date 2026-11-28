using System;

namespace ConviTEC.Inventario.Dominio;

/// Oferta del contrato v1: <c>{sedeId,disponible,revision}</c>. Es la última observación conocida de una sede sobre
/// un producto; la autorización final de una reserva ocurre en la sede propietaria.
public sealed class Oferta
{
    public string SedeId { get; }
    public int Disponible { get; }
    public long Revision { get; }

    public Oferta(string sedeId, int disponible, long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sedeId, nameof(sedeId));
        if (disponible < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(disponible), disponible, "El disponible no puede ser negativo.");
        }
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), revision, "La revisión no puede ser negativa.");
        }

        SedeId = sedeId;
        Disponible = disponible;
        Revision = revision;
    }
}
