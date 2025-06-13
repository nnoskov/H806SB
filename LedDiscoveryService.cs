using System.Net;
using System.Net.Sockets;

namespace H806SB
{
  public class LedDiscoveryService : IDisposable
  {
    private readonly UdpClient _udpClient;
    private const int DevicePort = 4626;
    private const int ListenPort = 4882; // Порт, с которого отправляются запросы

    public LedDiscoveryService()
    {
      _udpClient = new UdpClient(ListenPort);
      _udpClient.EnableBroadcast = true;
    }

    public async Task<IPAddress?> DiscoverDeviceAsync(TimeSpan timeout)
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
          if (result.Buffer.Length == 2 &&
              result.Buffer[0] == 0xfb &&
              result.Buffer[1] == 0xc0)
          {
            return result.RemoteEndPoint.Address;
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
