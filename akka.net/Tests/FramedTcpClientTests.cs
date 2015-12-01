using Akka.Actor;
using Akka.IO;
using Akka.N2J.Host.Actors;
using Akka.TestKit;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Akka.N2J.Tests
{
	using AkkaTestKit = TestKit.VsTest.TestKit;

	/// <summary>
	///		Test suite for <see cref="FramedTcpClient"/> actor.
	/// </summary>
	[TestClass]
	public class FramedTcpClientTests
		: AkkaTestKit
	{
		/// <summary>
		///		The test execution context.
		/// </summary>
		public TestContext TestContext
		{
			get;
			set;
		}

		/// <summary>
		///		When supplied with data that contains exactly 1 frame (in little-endian format), the <see cref="FramedTcpClient"/> actor should emit exactly 1 <see cref="FramedTcpClient.ReceivedFrame"/> notification.
		/// </summary>
		[TestMethod]
		public void Emit_Single_Frame_LittleEndian()
		{
			IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Loopback, 19123);

			TestProbe receiver = CreateTestProbe("receiver");
			TestProbe tcpClient = CreateTestProbe("tcp-client");

			IActorRef tcpCodec = ActorOf(
				FramedTcpClient.LittleEndian(
					receiver,
					tcpClient,
					new IPEndPoint(IPAddress.Loopback, 19123)
				)
			);

			ByteString testFrame = BuildFrame(frameWriter =>
			{
				frameWriter.Write(true);
				frameWriter.Write("This is a message.");
				frameWriter.Write(123);
			});

			tcpCodec.Tell(
				new Tcp.Received(testFrame)
			);

			FramedTcpClient.ReceivedFrame receivedFrame =
				receiver.ExpectMsg<FramedTcpClient.ReceivedFrame>(
					duration: TimeSpan.FromMilliseconds(500)
				);

			TestContext.WriteLine("Received frame of {0} bytes from {1}.", receivedFrame.Frame.Count, remoteEndPoint);

			// Validate frame size.
			int expectedFrameSize = BitConverter.ToInt32(
				testFrame.Slice(
					from: 0,
					until: FramedTcpClient.FrameLengthPrefixSize
				).ToArray(),
				startIndex: 0
			);
			Assert.AreEqual(expectedFrameSize, receivedFrame.Frame.Count);

			// Validate frame data.
			ByteString expectedFrame = testFrame.Drop(FramedTcpClient.FrameLengthPrefixSize);
			Assert.IsTrue(
				// ByteString GetEnumerator and Equals are not currently implemented, so we have to resort to this ugly fuckery.
				expectedFrame.ToArray().SequenceEqual(
					receivedFrame.Frame.ToArray()
				),
				"Received frame's content does not match expected frame content."
			);
		}

		/// <summary>
		///		Perform clean-up after each test.
		/// </summary>
		[TestCleanup]
		public void TestCleanup()
		{
			Shutdown(); // Shut down the actor system.

			Sys.AwaitTermination();
		}

		/// <summary>
		///		Build a message frame.
		/// </summary>
		/// <param name="frameBuilder">
		///		A delegate that writes values to a <see cref="StreamWriter"/> in order to build the frame.
		/// </param>
		/// <param name="bigEndian">
		///		Is data big-endian?
		/// </param>
		/// <param name="stringEncoding">
		///		An optional string encoding to use (default is UTF-8).
		/// </param>
		/// <returns>
		///		A <see cref="ByteString"/> representing the byte data that comprises the frame (including length prefix).
		/// </returns>
		static ByteString BuildFrame(Action<BinaryWriter> frameBuilder, bool bigEndian = false, Encoding stringEncoding = null)
		{
			if (frameBuilder == null)
				throw new ArgumentNullException("frameBuilder");

			if (bigEndian)
				throw new NotSupportedException("Not supported yet - we need to implement a Big-Endian stream writer first.");

			byte[] frameData;
			using (MemoryStream memoryStream = new MemoryStream())
			using (BinaryWriter streamWriter = new BinaryWriter(memoryStream, stringEncoding ?? Encoding.UTF8))
			{
				frameBuilder(streamWriter);
				streamWriter.Flush();

				frameData = memoryStream.ToArray();
			}

			return
				ByteString.NewBuilder()
					.PutInt(frameData.Length, bigEndian ? ByteOrder.BigEndian : ByteOrder.LittleEndian)
					.PutBytes(frameData)
					.Result();
		}
	}
}
