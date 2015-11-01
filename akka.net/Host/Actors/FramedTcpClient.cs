using Akka.Actor;
using Akka.IO;
using System;
using System.Diagnostics;
using System.Net;

namespace Akka.N2J.Host.Actors
{
	/// <summary>
	///		Actor that encodes / decodes framed message data that it exchanges with a TCP client actor.
	/// </summary>
	public sealed class FramedTcpClient
		: ReceiveActor
	{
		#region Constants

		/// <summary>
		///		The hard-limit of minimum frame size (in bytes).
		/// </summary>
		public static readonly int	HardMinFrameSize = 1024; // 1 kilobyte

		/// <summary>
		///		The hard-limit of maximum frame size (in bytes).
		/// </summary>
		public static readonly int	HardMaxFrameSize = 5 * 1024 * 1024; // 5 megabytes

		/// <summary>
		///		The default maximum frame size (in bytes).
		/// </summary>
		public static readonly int	DefaultMaxFrameSize = 512 * 1024; // 512 kilobytes

		/// <summary>
		///		The size (in bytes) of the frame-length prefix.
		/// </summary>
		public static readonly int	FrameLengthPrefixSize = sizeof(Int32);

		#endregion // Constants

		#region Instance data

		/// <summary>
		///		Given that the <see cref="FramedTcpClient"/> is on a CLR that uses big-endian or little endian format, should it expect remote data to be in the other format (little-endian or big-endian)?
		/// </summary>
		readonly bool		_isOtherEndian;

		/// <summary>
		///		The actor to which received message frames should be forwarded.
		/// </summary>
		readonly IActorRef	_receiver;

		/// <summary>
		///		The Akka.NET I/O actor used as a TCP client.
		/// </summary>
		readonly IActorRef	_tcpClient;

		/// <summary>
		///		The remote end-point represented by the TCP client.
		/// </summary>
		readonly IPEndPoint	_remoteEndPoint;

		/// <summary>
		///		The maximum permitted size (in bytes) for any frame transmitted or received by the <see cref="FramedTcpClient"/>.
		/// </summary>
		readonly int		_maxFrameSize;

		/// <summary>
		///		The current buffer.
		/// </summary>
		ByteString			_buffer = ByteString.Empty;

		/// <summary>
		///		The size of the current frame (0, if no frame size has currently been read).
		/// </summary>
		int					_currentFrameLength;

		#endregion // Instance data

		#region Construction

		/// <summary>
		///		Create a new <see cref="FramedTcpClient"/> actor.
		/// </summary>
		/// <param name="receiver">
		///		The actor to which received message frames should be forwarded.
		/// </param>
		/// <param name="tcpClient">
		///		The Akka.NET I/O actor used as a TCP client.
		/// </param>
		/// <param name="remoteEndPoint">
		///		The remote end-point represented by the TCP client.
		/// </param>
		/// <param name="isOtherEndian">
		///		Given that the <see cref="FramedTcpClient"/> is on a CLR that uses big-endian or little endian format, should it expect remote data to be in the other format (little-endian or big-endian)?
		/// </param>
		/// <param name="maxFrameSize">
		///		The maximum permitted size (in bytes) for any frame transmitted or received by the <see cref="FramedTcpClient"/>.
		/// </param>
		public FramedTcpClient(IActorRef receiver, IActorRef tcpClient, IPEndPoint remoteEndPoint, bool isOtherEndian, int maxFrameSize)
		{
			if (receiver == null)
				throw new ArgumentNullException("receiver");

			if (tcpClient == null)
				throw new ArgumentNullException("tcpClient");

			if (maxFrameSize < HardMinFrameSize || maxFrameSize > HardMaxFrameSize)
				throw new ArgumentOutOfRangeException("maxFrameSize", maxFrameSize, $"Maximum frame size must be between ${HardMinFrameSize} and ${HardMaxFrameSize}.");

			_receiver = receiver;
			_tcpClient = tcpClient;
			_remoteEndPoint = remoteEndPoint;
			_isOtherEndian = isOtherEndian;
			_maxFrameSize = maxFrameSize;

			Become(WaitForFrameLength);
		}

		#endregion // Construction

		#region Properties

		/// <summary>
		///		Is any data currently available in the buffer?
		/// </summary>
		bool HaveData => !_buffer.IsEmpty;

		/// <summary>
		///		Is the length of the current frame currently known?
		/// </summary>
		bool HaveFrameLength => _currentFrameLength != 0;

		/// <summary>
		///		Is an entire frame's worth of data currently available in the buffer?
		/// </summary>
		bool HaveFrame => HaveFrameLength && _buffer.Count >= _currentFrameLength;

		#endregion // Properties

		#region States

		/// <summary>
		///		The <see cref="FramedTcpClient"/> is waiting to receive the length of the next frame.
		/// </summary>
		void WaitForFrameLength()
		{
			Receive<Tcp.Received>(received =>
			{
				_buffer += received.Data;

				if (TryReadFrameLength())
				{
					// Emit the current frame and any other frames already present in the buffer.
					while (HaveData && HaveFrame)
					{
						EmitFrame();

						if (!TryReadFrameLength())
							break; // Partial prefix enountered; we'll pick up the rest next time.
					}
				}

				if (HaveData && !HaveFrame)
					Become(WaitForRestOfFrame); // Partial frame present in the buffer.
			});
		}

		/// <summary>
		///		The <see cref="FramedTcpClient"/> is waiting for the rest of the data comprising the current frame.
		/// </summary>
		void WaitForRestOfFrame()
		{
			Receive<Tcp.Received>(received =>
			{
				_buffer += received.Data;

				// Emit any frames present in the buffer.
				while (HaveData && HaveFrame)
				{
					EmitFrame();
					if (!TryReadFrameLength())
						break;  // Partial prefix enountered; we'll pick up the rest next time.
				}

				Become(WaitForFrameLength); // Buffer is empty, or contains a partial frame prefix.
			});
		}

		#endregion // States

		#region Helpers

		/// <summary>
		///		Attempt to read the frame length prefix from the receive buffer.
		/// </summary>
		/// <returns>
		///		<c>true</c>, if the length prefix was successfully read; otherwise, <c>false</c>.
		/// </returns>
		bool TryReadFrameLength()
		{
			if (!HaveData)
				return false;
			
			if (_buffer.Count < FrameLengthPrefixSize)
			{
				// Not enough data available; we'll pick up where we left off when more data arrives.
				return false;
			}

			byte[] lengthPrefix = _buffer.Slice(0, FrameLengthPrefixSize).ToArray();
			_buffer = _buffer.Drop(FrameLengthPrefixSize);

			if (_isOtherEndian)
				Array.Reverse(lengthPrefix);

			int frameLength = BitConverter.ToInt32(lengthPrefix, 0);
			if (frameLength > _maxFrameSize)
			{
				// TODO: Consider having an option to just drop frame and alert target with special message instead of crashing.
				throw new InvalidOperationException(
					String.Format(
						"Received a frame ({0} bytes long) that exceeded the configured maximum frame size ({1} bytes).",
						frameLength,
						_maxFrameSize
					)
				);
			}

			_currentFrameLength = frameLength;

			return true;
		}

		/// <summary>
		///		Emit a frame of data from the buffer.
		/// </summary>
		/// <remarks>
		///		Also resets the current frame length to 0.
		/// </remarks>
		void EmitFrame()
		{
			Debug.Assert(HaveFrameLength, "No current frame.");
			Debug.Assert(HaveFrame, "Not enough data available for current frame.");

			ByteString currentFrame = _buffer.Slice(0, _currentFrameLength);
			_buffer = _buffer.Drop(_currentFrameLength);

			_receiver.Tell(
				new ReceivedFrame(
					currentFrame.Compact(),
					_remoteEndPoint
				)
			);

			_currentFrameLength = 0;
		}

		#endregion // Helpers

		#region Props

		/// <summary>
		///		Create the <see cref="Actor.Props"/> for a <see cref="FramedTcpClient"/> actor that expects data in little-endian format.
		/// </summary>
		/// <param name="receiver">
		///		The actor to which received message frames should be forwarded.
		/// </param>
		/// <param name="tcpClient">
		///		The Akka.NET I/O actor used as a TCP client.
		/// </param>
		/// <param name="remoteEndPoint">
		///		The remote end-point represented by the TCP client.
		/// </param>
		/// <param name="maxFrameSize">
		///		The maximum permitted size (in bytes) for any frame transmitted or received by the <see cref="FramedTcpClient"/>.
		/// </param>
		public static Props LittleEndian(IActorRef receiver, IActorRef tcpClient, IPEndPoint remoteEndPoint, int? maxFrameSize = null)
		{
			return Props(receiver, tcpClient, remoteEndPoint, Endian.LittleEndian, maxFrameSize);
		}

		/// <summary>
		///		Create the <see cref="Actor.Props"/> for a <see cref="FramedTcpClient"/> actor that expects data in big-endian format.
		/// </summary>
		/// <param name="receiver">
		///		The actor to which received message frames should be forwarded.
		/// </param>
		/// <param name="tcpClient">
		///		The Akka.NET I/O actor used as a TCP client.
		/// </param>
		/// <param name="remoteEndPoint">
		///		The remote end-point represented by the TCP client.
		/// </param>
		/// <param name="maxFrameSize">
		///		The maximum permitted size (in bytes) for any frame transmitted or received by the <see cref="FramedTcpClient"/>.
		/// </param>
		public static Props BigEndian(IActorRef receiver, IActorRef tcpClient, IPEndPoint remoteEndPoint, int? maxFrameSize = null)
		{
			return Props(receiver, tcpClient, remoteEndPoint, Endian.BigEndian, maxFrameSize);
		}

		/// <summary>
		///		Create the <see cref="Actor.Props"/> for a <see cref="FramedTcpClient"/> actor that expects data in big-endian format.
		/// </summary>
		/// <param name="receiver">
		///		The actor to which received message frames should be forwarded.
		/// </param>
		/// <param name="tcpClient">
		///		The Akka.NET I/O actor used as a TCP client.
		/// </param>
		/// <param name="remoteEndPoint">
		///		The remote end-point represented by the TCP client.
		/// </param>
		/// <param name="maxFrameSize">
		///		The maximum permitted size (in bytes) for any frame transmitted or received by the <see cref="FramedTcpClient"/>.
		/// </param>
		/// <param name="endian">
		///		Should the <see cref="FramedTcpClient"/> expect remote data to be little-endian or big-endian?
		/// </param>
		static Props Props(IActorRef receiver, IActorRef tcpClient, IPEndPoint remoteEndPoint, Endian endian, int? maxFrameSize = null)
		{
			bool isOtherEndian;
			switch (endian)
			{
				case Endian.LittleEndian:
				{
					isOtherEndian = !BitConverter.IsLittleEndian;

					break;
				}
				case Endian.BigEndian:
				{
					isOtherEndian = BitConverter.IsLittleEndian;

					break;
				}
				default:
				{
					throw new ArgumentOutOfRangeException("endian", endian, "Must be little-endian or big-endian.");
				}
			}

			return Actor.Props.Create(
				() => new FramedTcpClient(receiver, tcpClient, remoteEndPoint, isOtherEndian, maxFrameSize ?? DefaultMaxFrameSize)
			);
		}

		/// <summary>
		///		Is data in big-endian or little-endian format?
		/// </summary>
		enum Endian
		{
			/// <summary>
			///		It is not known whether the data is in big-endian or little-endian format.
			/// </summary>
			Unknown = 0,

			/// <summary>
			///		The data is in little-endian format (LSB first).
			/// </summary>
			LittleEndian = 1,

			/// <summary>
			///		The data is in big-endian format (MSB first).
			/// </summary>
			BigEndian = 2
		}

		#endregion // Props

		#region Messages

		/// <summary>
		///		A frame was received from the remote host.
		/// </summary>
		public sealed class ReceivedFrame
		{
			/// <summary>
			///		Create a new <see cref="ReceivedFrame"/> notification.
			/// </summary>
			/// <param name="frame">
			///		A <see cref="ByteString"/> representing the frame data.
			/// </param>
			/// <param name="remoteEndPoint">
			///		The remote end-point from which the data was received.
			/// </param>
			public ReceivedFrame(ByteString frame, IPEndPoint remoteEndPoint)
			{
				if (frame == null)
					throw new ArgumentNullException("frame");

				if (remoteEndPoint == null)
					throw new ArgumentNullException("remoteEndPoint");

				Frame = frame;
				remoteEndPoint = RemoteEndPoint;
			}

			/// <summary>
			///		A <see cref="ByteString"/> representing the frame data.
			/// </summary>
			public ByteString Frame
			{
				get;
			}

			/// <summary>
			///		The remote end-point from which the data was received.
			/// </summary>
			public IPEndPoint RemoteEndPoint
			{
				get;
			}
		}

		#endregion // Messages
	}
}
