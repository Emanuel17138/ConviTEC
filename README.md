# ConviTEC

## Inventario (Integrante 1)

Backend de productos e inventario de una sede, en C# (.NET 10). Documentación de diseño en `Inventario/docs/`.

**Requisitos:** SDK de .NET 10 y un RabbitMQ en `localhost:5672` (Erlang/OTP + RabbitMQ Server; ver `Inventario/docs/mensajeria-rabbitmq.md`).

```powershell
cd Inventario
dotnet build ConviTEC.Inventario.slnx
dotnet test ConviTEC.Inventario.slnx            # las pruebas de RabbitMQ necesitan el broker activo
dotnet run --project src/ConviTEC.Inventario.Api -- --sede LIB --datos .\datos\lib --url http://localhost:5001 --remotas SJ --rabbitmq amqp://guest:guest@localhost:5672/
```

Parámetros de la sede: `--sede`, `--datos`, `--url`, `--remotas` (lista separada por comas) y `--rabbitmq`; o las variables `CONVITEC_SEDE`, `CONVITEC_DATOS`, `CONVITEC_URL`, `CONVITEC_REMOTAS` y `CONVITEC_RABBITMQ`.
