using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;

namespace Mirabox.NET
{
	public class MiraboxReciever
	{
		private static readonly byte[] HeartbeatResponse =
		{
			0x54, 0x46, 0x36, 0x7a, 0x60, 0x02, 0x00, 0x00, 0x28, 0x00, 0x00, 0x03, 0x03, 0x01
		};

		private readonly string _host;
		private readonly MemoryStream _frameStream = new();

		private ViewerWindow _window;
		private bool _synced;

		private int _currentFrame = -1;
		private int _lastChunk = -1;

		public MiraboxReciever(string host)
		{
			_host = host;
		}

		public void Start()
		{
			_window = new ViewerWindow();

			// using var stateAudioStream = OpenPort(2066);
			// stateAudioStream.UdpClient.BeginReceive(UdpPacketRecieved, stateAudioStream);
			//
			// using var stateControlStream = OpenPort(2067);
			// stateControlStream.UdpClient.BeginReceive(UdpPacketRecieved, stateControlStream);

			using var stateVideoPort = UdpHelper.OpenPort(_host, 2068);
			stateVideoPort.UdpClient.BeginReceive(VideoPacketRecieved, stateVideoPort);

			using var stateHeartbeatPort = UdpHelper.OpenPort(_host, 48689);
			stateHeartbeatPort.UdpClient.BeginReceive(HeartbeatPacketRecieved, stateHeartbeatPort);

			_window.Run();
		}

		private void VideoPacketRecieved(IAsyncResult ar)
		{
			if (ar.AsyncState is not UdpState state)
				return;

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
				_frameStream.Write(payload, 0, payload.Length);
			}

			if (lastChunk)
			{
				_frameStream.Seek(0, SeekOrigin.Begin);

				if (_synced)
				{
					var bmp = new Bitmap(_frameStream);
					_window.EnqueueFrame(bmp);
				}

				_frameStream.SetLength(0);
				_synced = true;
			}

			client.BeginReceive(VideoPacketRecieved, state);
		}

		private void HeartbeatPacketRecieved(IAsyncResult ar)
		{
			if (ar.AsyncState is not UdpState state)
				return;

			var client = state.UdpClient;
			IPEndPoint clientEndpoint = null;

			var data = client.EndReceive(ar, ref clientEndpoint);

			if (data.Length >= 5 && Encoding.ASCII.GetString(data, 0, 5) == "TF6zc")
			{
				client.Send(HeartbeatResponse, HeartbeatResponse.Length, clientEndpoint);
			}

			client.BeginReceive(HeartbeatPacketRecieved, state);
		}
	}
}