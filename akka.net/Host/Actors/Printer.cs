using Akka.Actor;
using Akka.IO;
using Serilog;
using System;
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
		public Printer()
		{
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
		}
	}
}
