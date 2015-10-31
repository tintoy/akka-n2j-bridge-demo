using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace Akka.N2J.Host.Actors
{
	/// <summary>
	///		Actor that handles incoming connections, associating them with <see cref="Printer"/> actors.
	/// </summary>
	/// <remarks>
	///		Also responsible for managing the lifetime of <see cref="Printer"/> actors that it creates.
	/// </remarks>
	public class Connector
		: ReceiveActor
	{
		/// <summary>
		///		Regular expression for matching IPV6 addresses in end-point descriptions.
		/// </summary>
		static readonly Regex		_ipV6Matcher = new Regex(@"^:[\w|:]*:");

		/// <summary>
		///		Actors that print input from remote actor systems.
		/// </summary>
		readonly List<IActorRef>	_printHandlers = new List<IActorRef>();

		/// <summary>
		///		The Akka I/O TCP manager.
		/// </summary>
		IActorRef					_tcpManager;

		/// <summary>
		///		The connector's listener end-point.
		/// </summary>
		IPEndPoint					_listenerEndPoint;

		/// <summary>
		///		Create a new <see cref="Connector"/> actor.
		/// </summary>
		public Connector()
		{
			Receive<Tcp.Bound>(bound =>
			{
				_listenerEndPoint = bound.LocalAddress as IPEndPoint;

				Log.Verbose("Connection manager is now listening on {ListenEndPoint}.", FormatEndPointIPv4(_listenerEndPoint));
            });

			// Remote client has connected. For now, we just assume they're JVM Akka (little-endian).
			Receive<Tcp.Connected>(connected =>
			{
				IPEndPoint remoteEndPoint = connected.RemoteAddress as IPEndPoint;
				string remoteEndPointIPv4 = FormatEndPointIPv4(remoteEndPoint);

				Log.Verbose("Connection manager is handling connection from {RemoteEndPoint}.", remoteEndPointIPv4);

				string handlerActorName = "printer-for-" + remoteEndPointIPv4;

				IActorRef printHandler = Context.ActorOf(
					Props.Create(
						() => new Printer(remoteEndPoint)
					),
					handlerActorName
				);
				_printHandlers.Add(printHandler);

				Context.Watch(printHandler);

				Sender.Tell(
					new Tcp.Register(printHandler)
				);
            });

			// Client has been disconnected.
			Receive<Disconnected>(disconnected =>
			{
				Log.Verbose(
					"Connection manager is handling disconnection from {RemoteEndPoint}.",
					FormatEndPointIPv4(disconnected.RemoteEndPoint)
				);

				IActorRef printer = Sender;
				if (!_printHandlers.Contains(printer))
				{
					Log.Warning(
						"Connection manager received disconnection notice for unknown end-point {RemoteEndPoint}.",
						FormatEndPointIPv4(disconnected.RemoteEndPoint)
					);

					return;
				}

				printer.Tell(PoisonPill.Instance); // Like tears in rain, time to die.
			});

			// A Printer actor has been terminated.
			Receive<Terminated>(terminated =>
			{
				IActorRef printer = _printHandlers.Find(handler => handler.Path == Sender.Path);
				if (printer == null)
				{
					Log.Warning(
						"Connection manager was notified of termination for unknown child actor '{TerminatedActorName}'.",
						terminated.ActorRef.Path.ToStringWithoutAddress()
					);

					Unhandled(terminated);

					return;
				}

				Log.Verbose(
					"Connection manager is handling termination of child actor '{TerminatedActorName}'.",
					terminated.ActorRef.Path.Name
				);
				_printHandlers.Remove(printer);
			});
		}

		/// <summary>
		///		Called when the actor is started.
		/// </summary>
		protected override void PreStart()
		{
			base.PreStart();

			int listenPort = ConfigurationFactory.Load().GetInt("connector.listen.port");

			_tcpManager = Context.System.Tcp();
			_tcpManager.Tell(
				new Tcp.Bind(
					handler: Self,
					localAddress: new IPEndPoint(
						IPAddress.Loopback, // local-only for now
						listenPort
					)
				)
			);
        }

		/// <summary>
		///		Format an end-point as IPv4 only (strip out the IPv6 address if required).
		/// </summary>
		/// <param name="endPoint">
		///		The end-point.
		/// </param>
		/// <returns>
		///		A string representation of the end-point, using IPv4 only.
		/// </returns>
		/// <remarks>
		///		For some reason <see cref="IPAddress.Loopback"/> still winds up being an IPv6 address. For the purposes of this demo, do not want.
		/// </remarks>
		static string FormatEndPointIPv4(IPEndPoint endPoint)
		{
			if (endPoint == null)
				throw new ArgumentNullException("endPoint");

			return String.Format("{0}:{1}",
				_ipV6Matcher.Replace(
					endPoint.Address.ToString(),
					String.Empty
				),
				endPoint.Port
			);
		}

		/// <summary>
		///		Message indicating that a remote connection was closed.
		/// </summary>
		public sealed class Disconnected
		{
			/// <summary>
			///		Create a new <see cref="Disconnected"/> message
			/// </summary>
			/// <param name="remoteEndPoint">
			///		The remote end-point that was disconnected.
			/// </param>
			public Disconnected(IPEndPoint remoteEndPoint)
			{
				if (remoteEndPoint == null)
					throw new ArgumentNullException("remoteEndPoint");

				RemoteEndPoint = remoteEndPoint;
			}

			/// <summary>
			///		The remote end-point that was disconnected.
			/// </summary>
			public IPEndPoint RemoteEndPoint { get; }
		}
	}
}
