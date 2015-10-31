using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;

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
		///		Actors that print input from remote actor systems (keyed by remote end-point).
		/// </summary>
		readonly Dictionary<IPEndPoint, IActorRef>	_printHandlers = new Dictionary<IPEndPoint, IActorRef>();

		/// <summary>
		///		The Akka I/O TCP manager.
		/// </summary>
		IActorRef									_tcpManager;

		/// <summary>
		///		The connector's listener end-point.
		/// </summary>
		IPEndPoint									_listenerEndPoint;

		/// <summary>
		///		Create a new <see cref="Connector"/> actor.
		/// </summary>
		public Connector()
		{
			Receive<Tcp.Bound>(bound =>
			{
				_listenerEndPoint = bound.LocalAddress as IPEndPoint;

				Log.Verbose("Connection manager is now listening on {ListenEndPoint}.", _listenerEndPoint);
            });

			// Remote client has connected. For now, we just assume they're JVM Akka (little-endian).
			Receive<Tcp.Connected>(connected =>
			{
				IPEndPoint remoteEndPoint = connected.RemoteAddress as IPEndPoint;
				Log.Verbose("Connection manager is handling connection from {RemoteEndPoint}.", remoteEndPoint);

				string handlerActorName = String.Format("{0}-{1}",
					remoteEndPoint.Address.ToString().Replace('.', '-'),
					remoteEndPoint.Port
				);
				IActorRef printHandler = Context.ActorOf<Printer>(handlerActorName);
				_printHandlers.Add(remoteEndPoint, printHandler);

				Sender.Tell(
					new Tcp.Register(printHandler)
				);
            });

			// TODO: Handle client disconnection.
		}

		/// <summary>
		///		Called when the actor is started.
		/// </summary>
		protected override void PreStart()
		{
			base.PreStart();

			// TODO: Start listening.
			Config configuration = ConfigurationFactory.Load();

			int listenPort = configuration.GetInt("connector.listen.port");

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
	}
}
