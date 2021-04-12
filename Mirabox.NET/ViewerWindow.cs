using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Mirabox.NET.Shader;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using PixelFormat = OpenTK.Graphics.OpenGL.PixelFormat;

namespace Mirabox.NET
{
	public class ViewerWindow : GameWindow
	{
		private static readonly DebugProc DebugCallback = OnGlMessage;

		private int _screenVao;
		private ShaderProgram _shaderScreen;

		private int _screenTexture = -1;
		private Vector2 _screenTextureSize = Vector2.Zero;

		private int _frameQueueCounter = 0;
		private int _frameDrawCounter = 0;
		private DateTime _lastFps = DateTime.Now;

		private readonly Queue<Bitmap> _imageUploadQueue = new();

		public ViewerWindow() : base(GameWindowSettings.Default, NativeWindowSettings.Default)
		{
			Load += WindowLoad;
			Resize += WindowResize;

			RenderFrame += WindowRender;
			UpdateFrame += WindowUpdate;

			KeyDown += args =>
			{
				if (args.Key == Keys.S)
					Size = new Vector2i((int) _screenTextureSize.X, (int) _screenTextureSize.Y);
			};
		}

		private void WindowLoad()
		{
			// Set up caps
			GL.Enable(EnableCap.RescaleNormal);
			GL.Enable(EnableCap.DebugOutput);
			GL.DebugMessageCallback(DebugCallback, IntPtr.Zero);
			GL.ActiveTexture(TextureUnit.Texture0);

			GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
			GL.PixelStore(PixelStoreParameter.PackAlignment, 1);

			// Set background color
			GL.ClearColor(1, 1, 1, 1);

			_shaderScreen = new ShaderProgram(
				"#version 330 core\nout vec4 FragColor;\nin vec2 TexCoords;\nuniform sampler2D img;\nvoid main(){FragColor=vec4(texture(img,TexCoords).rgb,1.0);}",
				"#version 330 core\nlayout (location=0) in vec2 aPos;\nlayout (location=1) in vec2 aTexCoords;\nuniform mat4 m;\nuniform mat4 v;\nuniform mat4 p;\nout vec2 TexCoords;\nvoid main()\n{\nmat4 mvp = p*v*m;\ngl_Position=mvp*vec4(aPos.x,aPos.y,0.0,1.0);\nTexCoords=aTexCoords;\n}"
			);
			_shaderScreen.Uniforms.SetValue("img", 0);

			_shaderScreen.Uniforms.SetValue("v", Matrix4.Identity);

			_screenTexture = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, _screenTexture);

			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
				(int) TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
				(int) TextureMagFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
				(int) TextureWrapMode.ClampToEdge);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
				(int) TextureWrapMode.ClampToEdge);
			GL.BindTexture(TextureTarget.Texture2D, 0);

			CreateScreenVao();
		}

		private static void OnGlMessage(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr message, IntPtr userparam)
		{
			if (severity == DebugSeverity.DebugSeverityNotification)
				return;

			var msg = Marshal.PtrToStringAnsi(message, length);
			Console.Error.WriteLine(msg);
		}

		private void CreateScreenVao()
		{
			var minVert = -0.5f;
			var maxVert = 0.5f;
			float[] quadVertices =
			{
				minVert, maxVert, 0, 1,
				minVert, minVert, 0, 0,
				maxVert, minVert, 1, 0,

				minVert, maxVert, 0, 1,
				maxVert, minVert, 1, 0,
				maxVert, maxVert, 1, 1
			};

			_screenVao = GL.GenVertexArray();
			var screenVbo = GL.GenBuffer();
			GL.BindVertexArray(_screenVao);
			GL.BindBuffer(BufferTarget.ArrayBuffer, screenVbo);
			GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float), quadVertices,
				BufferUsageHint.StaticDraw);
			GL.EnableVertexAttribArray(0);
			GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
			GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float), quadVertices,
				BufferUsageHint.StaticDraw);
			GL.EnableVertexAttribArray(1);
			GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
			GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
			GL.BindVertexArray(0);
		}

		private void DrawFullscreenQuad()
		{
			GL.BindVertexArray(_screenVao);
			GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
		}

		private void WindowResize(ResizeEventArgs obj)
		{
			GL.Viewport(0, 0, Size.X, Size.Y);
			
			var halfWidth = Size.X / 2;
			var halfHeight = Size.Y / 2;
			_shaderScreen.Uniforms.SetValue("p", Matrix4.CreateOrthographicOffCenter(-halfWidth, halfWidth, halfHeight, -halfHeight, -1, 1));
		}

		private void WindowUpdate(FrameEventArgs e)
		{
			if (_imageUploadQueue.TryDequeue(out var bmp))
			{
				_screenTextureSize = new Vector2(bmp.Width, bmp.Height);

				var scale = Math.Min(Size.X / _screenTextureSize.X, Size.Y / _screenTextureSize.Y);
				_shaderScreen.Uniforms.SetValue("m", Matrix4.CreateScale(_screenTextureSize.X * scale, _screenTextureSize.Y * scale, 1));

				GL.BindTexture(TextureTarget.Texture2D, _screenTexture);

				var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
					ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, data.Width, data.Height, 0,
					PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
				bmp.UnlockBits(data);

				bmp.Dispose();

				_frameDrawCounter++;

				GL.BindTexture(TextureTarget.Texture2D, 0);
			}

			var now = DateTime.Now;
			if (now - _lastFps > TimeSpan.FromSeconds(1))
			{
				_lastFps = now;

				Title = $"Queue: {_frameQueueCounter} FPS, Render: {_frameDrawCounter} FPS";

				_frameQueueCounter = 0;
				_frameDrawCounter = 0;
			}
		}

		private void WindowRender(FrameEventArgs e)
		{
			const ClearBufferMask bits = ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit;
			// Reset the view
			GL.Clear(bits);

			GL.BindTexture(TextureTarget.Texture2D, _screenTexture);

			_shaderScreen.Use();
			DrawFullscreenQuad();
			_shaderScreen.Release();

			// Swap the graphics buffer
			SwapBuffers();
		}

		public void EnqueueFrame(Bitmap frame)
		{
			_imageUploadQueue.Enqueue(frame);
			_frameQueueCounter++;
		}
	}
}