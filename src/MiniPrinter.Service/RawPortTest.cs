using System.Net;
using System.Net.Sockets;
using System.Text;
using MiniPrinter.Control;

namespace MiniPrinter.Service;

/// <summary>
/// The test ticket of the RAW port: a real ESC/POS ticket sent by TCP to the port itself, so that what is proven is the whole path
/// (the listener, the detection of the content, the interpreter and the queue) and not a shortcut.
/// </summary>
public static class RawPortTest
{
    private const int Columns = 32;

    /// <summary>The ESC/POS bytes of the ticket: header, styles, two columns, a QR code and the cut (and a line of Japanese when the fonts are there).</summary>
    public static byte[] BuildTicket(bool includeJapanese)
    {
        using var ticket = new MemoryStream();

        void Bytes(params byte[] bytes) => ticket.Write(bytes);
        void Text(string text) => Bytes(Encoding.ASCII.GetBytes(text));
        void Line(string text = "")
        {
            Text(text);
            Bytes(0x0A);
        }

        Bytes(0x1B, (byte)'@');                       // initialise
        Bytes(0x1B, (byte)'a', 1);                    // centre
        Bytes(0x1D, (byte)'!', 0x11);                 // double width and height
        Line("MiniPrinter");
        Bytes(0x1D, (byte)'!', 0x00);
        Line("Prueba del puerto 9100");
        Bytes(0x1B, (byte)'a', 0);                    // left
        Line(new string('-', Columns));

        Bytes(0x1B, (byte)'E', 1);                    // bold
        Line("Negrita");
        Bytes(0x1B, (byte)'E', 0);
        Bytes(0x1B, (byte)'-', 1);                    // underline
        Line("Subrayado");
        Bytes(0x1B, (byte)'-', 0);
        Line(new string('-', Columns));

        foreach (var (name, price) in new[] { ("Cafe con leche", "1,50"), ("Tostada", "2,20"), ("Total", "3,70") })
            Line(name + new string(' ', Columns - name.Length - price.Length) + price);
        Line(new string('-', Columns));

        if (includeJapanese)
        {
            Bytes(0x1C, (byte)'&');                   // kanji mode on
            Bytes(0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA);   // 日本語 in Shift-JIS
            Bytes(0x1C, (byte)'.');                   // kanji mode off
            Line();
        }

        // QR code (model 2, size 6, correction L) of a short text.
        var data = Encoding.ASCII.GetBytes("MiniPrinter 9100");
        Bytes(0x1B, (byte)'a', 1);
        Bytes(0x1D, (byte)'(', (byte)'k', 4, 0, 0x31, 0x41, 0x32, 0x00);
        Bytes(0x1D, (byte)'(', (byte)'k', 3, 0, 0x31, 0x43, 6);
        Bytes(0x1D, (byte)'(', (byte)'k', 3, 0, 0x31, 0x45, 0x30);
        Bytes(0x1D, (byte)'(', (byte)'k', (byte)((data.Length + 3) & 0xFF), (byte)((data.Length + 3) >> 8), 0x31, 0x50, 0x30);
        Bytes(data);
        Bytes(0x1D, (byte)'(', (byte)'k', 3, 0, 0x31, 0x51, 0x30);
        Line();
        Bytes(0x1B, (byte)'a', 0);

        Line();
        Bytes(0x1D, (byte)'V', 66, 0);                // feed and cut
        return ticket.ToArray();
    }

    /// <summary>Sends the ticket to the port and waits for the job it becomes. Answers with the reason when it cannot.</summary>
    public static async Task<IResult> RunAsync(ServiceSettings settings, RawPortDto port, JobQueue queue, bool includeJapanese, CancellationToken ct)
    {
        if (!settings.RawPortEnabled)
            return Error("La impresión directa por el puerto 9100 está desactivada: actívala en Ajustes y aplica el cambio.", 409);
        if (port.Error is { } error)
            return Error($"El puerto {port.Port} no está disponible: {error}", 409);
        if (!port.Listening)
            return Error($"El puerto {port.Port} todavía no está escuchando. Espera un momento y vuelve a intentarlo.", 409);

        var lastId = queue.AllJobs().Select(j => j.Id).DefaultIfEmpty(0).Max();
        try
        {
            using var client = new TcpClient();
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connect.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(IPAddress.Loopback, port.Port, connect.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(BuildTicket(includeJapanese), ct);
            client.Client.Shutdown(SocketShutdown.Send);

            // The port closes the connection when it has taken the ticket; a port that refuses (busy) closes it at once.
            var buffer = new byte[64];
            using var closed = CancellationTokenSource.CreateLinkedTokenSource(ct);
            closed.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                while (await stream.ReadAsync(buffer, closed.Token) > 0)
                {
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // fall through: what matters is whether the job appeared
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Error($"El puerto {port.Port} no respondió. Puede estar ocupado con otras conexiones.", 504);
        }
        catch (SocketException)
        {
            return Error($"No se pudo conectar con el puerto {port.Port}: lo rechaza o está ocupado.", 503);
        }
        catch (IOException)
        {
            return Error($"El puerto {port.Port} cerró la conexión: está ocupado con otras conexiones.", 503);
        }

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (queue.AllJobs().Where(j => j.Id > lastId && j.Source == Ipp.JobSource.Raw).OrderBy(j => j.Id).FirstOrDefault() is { } job)
                return Results.Json(ControlApi.ToDto(job), ControlDefaults.Json);
            await Task.Delay(50, ct);
        }
        return Error($"El puerto {port.Port} recibió el ticket pero no llegó a crear el trabajo.", 502);
    }

    private static IResult Error(string message, int status) => Results.Json(new ApiError(message), ControlDefaults.Json, statusCode: status);
}
