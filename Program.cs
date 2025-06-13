using H806SB;
using System.Net;
using System.Net.Sockets;

namespace LedController;

public class LedController : IDisposable
{
  private readonly UdpClient _udpClient = new();
  private readonly IPEndPoint _broadcastEndpoint = new(IPAddress.Broadcast, 4626);

  public LedController()
  {
    _udpClient.EnableBroadcast = true;
  }

  public async Task SetBrightnessAsync(byte brightness, CancellationToken cancellationToken = default)
  {
    // Ограничиваем яркость согласно наблюдаемому диапазону
    brightness = Math.Clamp(brightness, (byte)0, (byte)31);

    // Формируем пакет на основе анализа трафика
    var packet = new byte[]
    {
            0xfb, 0xc1, brightness, 0x13,    // Управляющий байт + яркость
            brightness, 0x01, 0x00, 0xae,     // Вероятно, счетчик пакетов
            0x00, 0x39, 0x0c, 0x00,           // Константные значения
            0x51, 0x39, 0x0c, 0x00            // Константные значения
    };

    await _udpClient.SendAsync(packet, _broadcastEndpoint, cancellationToken);
  }

  public async Task<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken = default)
  {
    return await _udpClient.ReceiveAsync(cancellationToken);
  }

  public void Dispose()
  {
    _udpClient.Dispose();
    GC.SuppressFinalize(this);
  }
}

public static class Program
{
  public static async Task Main()
  {
    using var controller = new LedController();
    using var cts = new CancellationTokenSource();
    using var discovery = new LedDiscoveryService();

    Console.CancelKeyPress += (_, e) =>
    {
      e.Cancel = true;
      cts.Cancel();
      Console.WriteLine("\nCompleting work...");
    };

    Console.WriteLine("LED Controller (NET 8)");
    Console.WriteLine("Commands:");
    Console.WriteLine("  discover - Finding of device");
    Console.WriteLine("  set N    - Set brightness (0-31)");
    Console.WriteLine("  exit     - Exit");
    Console.WriteLine("  Ctrl+C   - Cancel commands");

    while (!cts.IsCancellationRequested)
    {
      try
      {
        Console.Write("> ");
        var input = Console.ReadLine()?.Trim().ToLower();

        if (string.IsNullOrWhiteSpace(input))
          continue;

        switch (input)
        {
          case "exit":
            return;

          case "discover":
            Console.WriteLine("Finding of device...");
            var deviceIp = await discovery.DiscoverDeviceAsync(TimeSpan.FromSeconds(3));

            if (deviceIp != null)
            {
              Console.WriteLine($"Success! IP: {deviceIp}");
            }
            else
            {
              Console.WriteLine("The Device not found. :(");
            }
            break;

          case string s when s.StartsWith("set "):
            await HandleSetCommand(controller, s, cts.Token);
            break;

          default:
            Console.WriteLine("Unknown command");
            break;
        }
      }
      catch (OperationCanceledException)
      {
        Console.WriteLine("The Operation has been cancelled");
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error: {ex.Message}");
      }
    }
  }

  private static async Task HandleSetCommand(LedController controller, string command, CancellationToken ct)
  {
    if (byte.TryParse(command[4..], out var brightness) && brightness is >= 0 and <= 31)
    {
      await controller.SetBrightnessAsync(brightness, ct);
      Console.WriteLine($"Brightness is set to {brightness}/31");
    }
    else
    {
      Console.WriteLine("Incorrect value of brightness. Acceptable range: 0-31");
    }
  }
}