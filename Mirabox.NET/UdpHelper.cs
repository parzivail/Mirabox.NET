using System.Net;
using System.Net.Sockets;

namespace Mirabox.NET
{
	internal static class UdpHelper
	{
		public static UdpState OpenPort(string host, int port)
		{
			var endPoint = new IPEndPoint(IPAddress.Parse(host), port);
			var udpClient = new UdpClient();
			
			udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			udpClient.Client.Ttl = 128;
			udpClient.Client.DontFragment = true;;

			udpClient.EnableBroadcast = true;
			udpClient.ExclusiveAddressUse = false;

			udpClient.Client.Bind(endPoint);
			udpClient.JoinMulticastGroup(IPAddress.Parse("226.2.2.2"), endPoint.Address);

			return new UdpState(udpClient, endPoint);
		}
	}
}