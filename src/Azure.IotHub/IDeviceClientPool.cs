using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;

namespace ViciOne.Suite.DataPort;

internal interface IDeviceClientPool
{
    Task<VirtualDeviceClient> Register(string hostname, string deviceId, IAuthenticationMethod authenticationMethod, CancellationToken cancellationToken);
    Task Unregister(VirtualDeviceClient deviceClient);
    Task Connect(VirtualDeviceClient deviceClient, CancellationToken cancellationToken);
    Task Disconnect(VirtualDeviceClient deviceClient, CancellationToken cancellationToken);
}
