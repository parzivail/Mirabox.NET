using System;
using System.Net;
using System.Net.Sockets;

namespace Mirabox.NET
{
	internal class UdpState : IDisposable
	{
		public UdpClient UdpClient { get; }
		public IPEndPoint ListenEndPoint { get; }

		public UdpState(UdpClient udpClient, IPEndPoint listenEndPoint)
		{
			UdpClient = udpClient;
			ListenEndPoint = listenEndPoint;
		}

		/// <inheritdoc />
		public void Dispose()
		{
			UdpClient?.Dispose();
		}
	}
}