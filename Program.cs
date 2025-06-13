using H806SB;
using System.Net;
using System.Net.Sockets;

namespace LedController;

public class LedController : IDisposable
{
  private readonly UdpClient _udpClient = new();
  private readonly IPEndPoint _broadcastEndpoint = new(IPAddress.Broadcast, 4626);
  private byte[] packet =
    [
            0xfb,       // [0] Управляющий байт #1
            0xc1,       // [1] Управляющий байт #2,
            0x00,       // [2] Счетчик команд
            0x50,       // [3] Скорость
            0x00,       // [4] Яркость
            0x01,       // [5] Single file playback
            0x00,       // [6] unknown
            0xae,       // [7] unknown
            0x00, 0x00, 0x00, 0x00,           // [08] [09] [10] [11] constant values
            0x00, 0x00, 0x00, 0x00            // [12] [13] [14] [15] serial number (example: 51 39 0c 00)
    ];
  
  public byte[] Packet => packet; // Свойство для доступа к пакету

  public LedController()
  {
    _udpClient.EnableBroadcast = true;
  }

  public async Task SendCommandAsync(CancellationToken cancellationToken = default)
  {
    packet[2]++; // Увеличиваем счётчик команд перед отправкой
    await _udpClient.SendAsync(packet, _broadcastEndpoint, cancellationToken);
  }

  public async Task SetSpeedAsync(byte speed, CancellationToken cancellationToken = default)
  {
    // Ограничиваем скорость согласно наблюдаемому диапазону
    speed = Math.Clamp(speed, (byte)1, (byte)100);
    packet[3] = speed; // Устанавливаем скорость в 4-й байт пакета
    await SendCommandAsync(cancellationToken);
  }

  public async Task SetBrightnessAsync(byte brightness, CancellationToken cancellationToken = default)
  {
    // Ограничиваем яркость согласно наблюдаемому диапазону
    brightness = Math.Clamp(brightness, (byte)0, (byte)31);
    
    packet[4] = brightness; // Устанавливаем яркость в 5-й байт пакета
    await SendCommandAsync(cancellationToken);
  }

  public async Task SetSingleFileAsync(byte sf, CancellationToken cancellationToken = default)
  {
    // Ограничение возможности выбора согласно диапазона
    sf = Math.Clamp(sf, (byte)0, (byte)1);
    packet[5] = sf; // Устанавливаем single file в 6-ый байт пакета
    await SendCommandAsync(cancellationToken);
  }

  public static bool HasNonZeroData(byte[] data)
  {
    if (data == null || data.Length < 6)
      return false;

    Span<byte> lastSixBytes = data.AsSpan(data.Length - 6, 6);
    foreach (byte b in lastSixBytes)
    {
      if (b != 0)
        return true;
    }
    return false;
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
    Console.WriteLine("  discover       - Finding of device");
    Console.WriteLine("  set br 'value' - Set brightness (0-31)");
    Console.WriteLine("  set sp 'value' - Set speed (0-100)");
    Console.WriteLine("  set sf 'value' - Set single file (0-1)");
    Console.WriteLine("  exit           - Exit");
    Console.WriteLine("  Ctrl+C         - Cancel commands");

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
            var device = await discovery.DiscoverDeviceAsync(TimeSpan.FromSeconds(3));

            if (device != null)
            {
              Console.WriteLine($"Success! Device Name: {device.Value.DeviceName}, IP: {device.Value.Address}");
              for(int i = device.Value.SerialNumber.Length - 1; i >= 0;i--)
              {
                controller.Packet[14 - i] = device.Value.SerialNumber[i];
              }
              Console.WriteLine($"Packet: {BitConverter.ToString(controller.Packet[0..15]).Replace("-", " ")}");
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
    const string BrightnessPrefix = "br ";
    const string SpeedPrefix = "sp ";
    const string SingleFilePrefix = "sf ";

    if (!LedController.HasNonZeroData(controller.Packet))
    {
      Console.WriteLine("Please, discovery the device.");
      return;
    }

    if (command.Contains(BrightnessPrefix))
    {
      await HandleParameterAsync(
          valueStr: command[6..],
          minValue: 0,
          maxValue: 31,
          onSuccess: async (value) =>
          {
            await controller.SetBrightnessAsync(value, ct);
            Console.WriteLine($"Brightness is set to {value}/31");
          },
          onError: () => Console.WriteLine("Incorrect brightness. Acceptable range: 0-31")
      );
    }
    else if (command.Contains(SpeedPrefix))
    {
      await HandleParameterAsync(
          valueStr: command[6..],
          minValue: 1,
          maxValue: 100,
          onSuccess: async (value) =>
          {
            await controller.SetSpeedAsync(value, ct);
            Console.WriteLine($"Speed is set to {value}/100");
          },
          onError: () => Console.WriteLine("Incorrect speed. Acceptable range: 1-100")
      );
    }
    else if (command.Contains(SingleFilePrefix))
    {
      await HandleParameterAsync(
          valueStr: command[6..],
          minValue: 0,
          maxValue: 1,
          onSuccess: async (value) =>
          {
            await controller.SetSingleFileAsync(value, ct);
            Console.WriteLine($"Single file is set to {value}");
          },
          onError: () => Console.WriteLine("Incorrect single file settings. Acceptable range: 0-1")
      );
    }
    else
    {
      Console.WriteLine("Unknown command. Available: 'br <0-31>', 'sp <1-100>', 'sf <0-1>'");
    }
  }

  private static async Task HandleParameterAsync(
      string valueStr,
      byte minValue,
      byte maxValue,
      Func<byte, Task> onSuccess,
      Action onError)
  {
    if (byte.TryParse(valueStr, out var value) && value >= minValue && value <= maxValue)
    {
      await onSuccess(value);
    }
    else
    {
      onError();
    }
  }

 
}