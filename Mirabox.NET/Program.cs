using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

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

	class Program
	{
		private static readonly MemoryStream JpegMemStream = new();
		
		private static ViewerWindow _window;
		private static bool _synced;
		
		private static int _currentFrame = -1;
		private static int _lastChunk = -1;
		
		private static UdpState OpenPort(int port)
		{
			var endPoint = new IPEndPoint(IPAddress.Parse("192.168.168.12"), port);
			var udpClient = new UdpClient();
			udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

			udpClient.EnableBroadcast = true;
			udpClient.ExclusiveAddressUse = false;

			udpClient.Client.Bind(endPoint);
			udpClient.JoinMulticastGroup(IPAddress.Parse("226.2.2.2"), endPoint.Address);

			return new UdpState(udpClient, endPoint);
		}

		static void Main(string[] args)
		{
			// using var stateAudioStream = OpenPort(2066);
			// stateAudioStream.UdpClient.BeginReceive(UdpPacketRecieved, stateAudioStream);
			//
			// using var stateControlStream = OpenPort(2067);
			// stateControlStream.UdpClient.BeginReceive(UdpPacketRecieved, stateControlStream);

			_window = new ViewerWindow();
			
			using var stateVideoPort = OpenPort(2068);
			stateVideoPort.UdpClient.BeginReceive(VideoPacketRecieved, stateVideoPort);

			using var stateHeartbeatPort = OpenPort(48689);
			stateHeartbeatPort.UdpClient.BeginReceive(HeartbeatPacketRecieved, stateHeartbeatPort);

			_window.Run();
		}

		private static void VideoPacketRecieved(IAsyncResult ar)
		{
			var state = (UdpState) ar.AsyncState;
			var client = state.UdpClient;
			IPEndPoint clientEndpoint = null;

			var data = client.EndReceive(ar, ref clientEndpoint);
			
			using var br = new BinaryReader(new MemoryStream(data));

			var frameNum = IPAddress.NetworkToHostOrder(br.ReadInt16());
			var chunkData = IPAddress.NetworkToHostOrder(br.ReadInt16());

			var lastChunk = (chunkData & 0b1000000000000000) != 0;
			var chunkNum = chunkData & 0b0111111111111111;

			if (chunkNum == 0)
				_currentFrame = frameNum;
			else if (frameNum != _currentFrame || chunkNum != _lastChunk + 1)
				_synced = false;
			
			_currentFrame = frameNum;
			_lastChunk = chunkNum;

			if (_synced)
			{
				var payload = br.ReadBytes(1020);
				JpegMemStream.Write(payload, 0, payload.Length);				
			}

			if (lastChunk)
			{
				if (_synced)
				{
					JpegMemStream.Seek(0, SeekOrigin.Begin);

					var bmp = new Bitmap(JpegMemStream);
					_window.EnqueueFrame(bmp);
					
					JpegMemStream.SetLength(0);
				}
				
				_synced = true;
			}

			client.BeginReceive(VideoPacketRecieved, state);
		}

		private static void HeartbeatPacketRecieved(IAsyncResult ar)
		{
			var state = (UdpState) ar.AsyncState;
			var client = state.UdpClient;
			IPEndPoint clientEndpoint = null;

			var data = client.EndReceive(ar, ref clientEndpoint);

			if (data.Length == 512 && Encoding.ASCII.GetString(data, 0, 5) == "TF6zc")
			{
				var outgoingData = new byte[]
				{
					0x54, 0x46, 0x36, 0x7a, 0x60, 0x02, 0x00, 0x00, 0x28, 0x00, 0x00, 0x03, 0x03, 0x01
				};
				client.Send(outgoingData, outgoingData.Length, clientEndpoint);
			}

			client.BeginReceive(HeartbeatPacketRecieved, state);
		}
	}
}