namespace ConviTEC.Inventario.Api.Http;

/// Cuerpo de error común de la convención v1: {codigo, mensaje}.
public sealed record ErrorRespuesta(string Codigo, string Mensaje);
