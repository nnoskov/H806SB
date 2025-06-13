using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace H806SB
{
  public class LedDiscoveryService : IDisposable
  {
    private readonly UdpClient _udpClient;
    private string _deviceName;
    private byte[] _serialNumber = new byte[4]; // 00 0C 39 51
    private const int DevicePort = 4626;
    private const int ListenPort = 4882; // Порт, с которого отправляются запросы

    public LedDiscoveryService()
    {
      _udpClient = new UdpClient(ListenPort);
      _udpClient.EnableBroadcast = true;
    }

    public async Task<(IPAddress Address, byte[] SerialNumber, string DeviceName)?> DiscoverDeviceAsync(TimeSpan timeout)
    {
      // Формируем discovery-пакеты по образцу из трафика
      var packet12 = new byte[]
      {
            0xfb, 0xc1, 0x01, 0x13, 0x00, 0x01, 0x00, 0xae, 0x00, 0x00, 0x00, 0x00
      };

      var packet8 = new byte[]
      {
            0xab, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00
      };

      // Отправляем пакеты в правильной последовательности
      var broadcastEp = new IPEndPoint(IPAddress.Broadcast, DevicePort);

      await _udpClient.SendAsync(packet8, packet8.Length, broadcastEp);
      await Task.Delay(50);
      await _udpClient.SendAsync(packet12, packet12.Length, broadcastEp);

      // Ожидаем ответ в течение заданного времени
      var cts = new CancellationTokenSource(timeout);

      try
      {
        while (!cts.Token.IsCancellationRequested)
        {
          var result = await _udpClient.ReceiveAsync(cts.Token);

          // Проверяем, что это ответ устройства (2 байта: 0xfb 0xc0)
          if (result.Buffer[0] == 0xab &&
              result.Buffer[1] == 0x02)
          {
            // Извлекаем имя устройства из ответа
            int nameLength = 0;
            while (2 + nameLength < result.Buffer.Length && result.Buffer[2 + nameLength] != 0)
            {
              nameLength++;
            }
            _deviceName = Encoding.ASCII.GetString(result.Buffer, 2, nameLength);
            var parts = _deviceName.Split('_');
            if (parts.Length < 2) throw new FormatException($"Invalid device name format. Expected 'HCX_XXXXXX', got '{_deviceName}'");
            if (_deviceName.Length >= 5)
            {
              try
              {
                var hexPart = _deviceName.Split('_')[1];
                _serialNumber = Convert.FromHexString(hexPart);
              }
              catch (FormatException ex)
              {
                // Логируем ошибку
                Console.WriteLine($"Error parsing device name: {ex.Message}");
                throw; // Перебрасываем исключение дальше
              }
              catch (Exception ex) when (ex is ArgumentOutOfRangeException || ex is OverflowException)
              {
                // Специфичная обработка для ошибок конвертации
                throw new FormatException($"Invalid hex format in device name '{_deviceName}'", ex);
              }
            }
            return (result.RemoteEndPoint.Address, _serialNumber, _deviceName);
          }
        }
      }
      catch (OperationCanceledException)
      {
        // Время ожидания истекло
      }

      return null;
    }

    public void Dispose()
    {
      _udpClient?.Dispose();
    }
  }
}
