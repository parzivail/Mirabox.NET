using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Mirabox.NET
{
	class Program
	{
		static void Main(string[] args)
		{
			// new MiraboxReciever("192.168.168.12").Start();
			new MiraboxTransmitter("192.168.168.12").Start();
		}
	}

	[StructLayout(LayoutKind.Explicit)]
	internal struct ShortConverter
	{
		private static ShortConverter Instance;

		[FieldOffset(0)] short ShortValue;
		[FieldOffset(0)] ushort UShortValue;

		public static short Convert(ushort source)
		{
			Instance.UShortValue = source;
			return Instance.ShortValue;
		}
	}

	class MiraboxTransmitter
	{
		private static readonly IPEndPoint HeartbeatBroadcastEndpoint = new(IPAddress.Broadcast, 48689);
		private static readonly IPEndPoint VideoBroadcastEndpoint = new(IPAddress.Parse("226.2.2.2"), 2068);
		private static readonly IPEndPoint ControlBroadcastEndpoint = new(IPAddress.Parse("226.2.2.2"), 2067);

		private static readonly byte[] HeartbeatQuery =
		{
			0x54, 0x46, 0x36, 0x7a, 0x63, 0x01, 0x00, 0x00, 0x28, 0x00, 0x00, 0x03, 0x03, 0x03
		};

		private readonly string _host;
		private readonly Timer _heartbeatTimer = new(1000);
		private readonly List<IPEndPoint> _heartbeatEndPoints = new();

		private short _frameNumber;
		private short _numFramesSinceHeartbeat;

		private UdpState _stateControlPort;
		private UdpState _stateVideoPort;
		private UdpState _stateHeartbeatPort;

		public MiraboxTransmitter(string host)
		{
			_host = host;

			_heartbeatTimer.Elapsed += HeartbeatTick;
			_heartbeatTimer.AutoReset = true;
		}

		public void Start()
		{
			_stateControlPort = UdpHelper.OpenPort(_host, 2067);
			_stateVideoPort = UdpHelper.OpenPort(_host, 2068);

			_stateHeartbeatPort = UdpHelper.OpenPort(_host, 48689);
			_stateHeartbeatPort.UdpClient.BeginReceive(HeartbeatPacketRecieved, _stateHeartbeatPort);

			_heartbeatTimer.Enabled = true;

			using var controlStream = new MemoryStream();
			var controlWriter = new BinaryWriter(controlStream);
			
			using var chunkStream = new MemoryStream();
			var chunkWriter = new BinaryWriter(chunkStream);
			
			using var frameStream = File.Open(@"R:\Temp\highground.jpg", FileMode.Open);

			// using var bmp = new Bitmap(1920, 1080);
			// using var g = Graphics.FromImage(bmp);
			//
			// g.Clear(Color.White);
			// g.DrawLine(Pens.Black, 10, 10, 710, 470);
			//
			// bmp.Save(frameStream, ImageFormat.Jpeg);

			while (true)
			{
				if (_heartbeatEndPoints.Count == 0 || _numFramesSinceHeartbeat >= 30)
					continue;

				controlStream.SetLength(0);

				controlWriter.Write(new byte[4]);
				controlWriter.Write(IPAddress.HostToNetworkOrder(_frameNumber));
				controlWriter.Write(new byte[14]);

				var controlBytes = controlStream.ToArray();
				_stateControlPort.UdpClient.Send(controlBytes, controlBytes.Length, ControlBroadcastEndpoint);

				frameStream.Seek(0, SeekOrigin.Begin);

				var frameChunkBytes = new byte[1020];
				var numChunks = (frameStream.Length + frameChunkBytes.Length - 1) / frameChunkBytes.Length;
				for (short chunkNum = 0; chunkNum < numChunks; chunkNum++)
				{
					chunkStream.SetLength(0);

					var chunkData = (ushort) (chunkNum & 0b0111111111111111);
					if (chunkNum == numChunks - 1)
						chunkData |= 0b1000000000000000;

					chunkWriter.Write(IPAddress.HostToNetworkOrder(_frameNumber));
					chunkWriter.Write(IPAddress.HostToNetworkOrder(ShortConverter.Convert(chunkData)));

					for (var i = 0; i < frameChunkBytes.Length; i++)
						frameChunkBytes[i] = 0;
					
					frameStream.Read(frameChunkBytes, 0, frameChunkBytes.Length);
					chunkWriter.Write(frameChunkBytes);

					var chunkBytes = chunkStream.ToArray();
					_stateVideoPort.UdpClient.Send(chunkBytes, chunkBytes.Length, VideoBroadcastEndpoint);
				}

				_frameNumber++;
				_frameNumber %= short.MaxValue;
				_numFramesSinceHeartbeat++;
			}
		}

		private void HeartbeatTick(object sender, ElapsedEventArgs e)
		{
			var heartbeatPacket = new byte[512];
			Array.Copy(HeartbeatQuery, heartbeatPacket, HeartbeatQuery.Length);

			if (_heartbeatEndPoints.Count == 0)
			{
				Console.WriteLine("No heartbeat");
			}
			else
			{
				Console.WriteLine($"Heartbeat clients: {_heartbeatEndPoints.Count}");
			}

			_heartbeatEndPoints.Clear();
			_numFramesSinceHeartbeat = 0;
			_stateHeartbeatPort.UdpClient.Send(HeartbeatQuery, HeartbeatQuery.Length, HeartbeatBroadcastEndpoint);
		}

		private void HeartbeatPacketRecieved(IAsyncResult ar)
		{
			if (ar.AsyncState is not UdpState state)
				return;

			var client = state.UdpClient;
			IPEndPoint clientEndpoint = null;

			var data = client.EndReceive(ar, ref clientEndpoint);

			if (data.Length >= 5 && Encoding.ASCII.GetString(data, 0, 5) == "TF6z`")
			{
				_heartbeatEndPoints.Add(clientEndpoint);
			}

			client.BeginReceive(HeartbeatPacketRecieved, state);
		}
	}
}