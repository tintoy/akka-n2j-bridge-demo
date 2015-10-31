using Akka.Actor;
using Akka.IO;
using Serilog;
using System;
using System.Net;
using System.Text;

namespace Akka.N2J.Host.Actors
{
	/// <summary>
	///		Actor that prints messages (via Akka I/O TCP) from remote actor systems.
	/// </summary>
	public class Printer
		: ReceiveActor
	{
		/// <summary>
		///		Create a new <see cref="Printer"/> actor.
		/// </summary>
		/// <param name="remoteEndPoint">
		///		The remote end-point whose messages are being printed.
		/// </param>
		public Printer(IPEndPoint remoteEndPoint)
		{
			if (remoteEndPoint == null)
				throw new ArgumentNullException("remoteEndPoint");

			Receive<Tcp.Received>(received =>
			{
				string message = received.Data
					.DecodeString(Encoding.UTF8)
					.Replace(Environment.NewLine, "\\n");

				Log.Information(
					"Printer '{ActorName}' recieved message: '{Message}'.",
					Self.Path.Name,
					message
				);
			});

			// Connection closed.
			Receive<Tcp.ConnectionClosed>(connectionClosed =>
			{
				Log.Information(
					"Printer '{ActorName}' connection was closed: @{ConnectionClosed}.",
					Self.Path.Name,
					connectionClosed
				);

				// Tell the connection manager that we're done.
				Context.Parent.Tell(
					new Connector.Disconnected(remoteEndPoint)
                );
			});
			
			// Connection closed with error.
			Receive<Tcp.ErrorClosed>(closedWithError =>
			{
				Log.Information(
					"Printer '{ActorName}' connection was closed: {@ConnectionClosed}.",
					Self.Path.Name,
					closedWithError
				);

				// Tell the connection manager that we're done.
				Context.Parent.Tell(
					new Connector.Disconnected(remoteEndPoint)
				);
			});
		}
	}
}
