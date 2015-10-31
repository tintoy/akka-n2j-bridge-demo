using Akka.Actor;
using Akka.N2J.Host.Actors;
using Serilog;
using System;
using System.Threading;

namespace Akka.N2J.Host
{
	/// <summary>
	///		Host for the Akka.NET side of the JVM / .NET Akka bridge.
	/// </summary>
	static class Program
	{
		/// <summary>
		///		The main program entry-point.
		/// </summary>
		static void Main()
		{
			SynchronizationContext.SetSynchronizationContext(
				new SynchronizationContext()
			);

			ConfigureLogging();
			try
			{
				using (ActorSystem actorSystem = ActorSystem.Create("Akka-CLR"))
				{
					// Create the actor that incoming connections and assigns them to printers.
					actorSystem.ActorOf<Connector>(
						name: "connections"
					);

					Log.Information("Actor system started (press enter to terminate).");
					Console.ReadLine();

					// Forehead goes here.

					actorSystem.Shutdown();
					actorSystem.AwaitTermination();
				}
				Log.Information("Shutdown complete.");
			}
			catch (Exception unexpectedError)
			{
				Log.Error(unexpectedError, "Unexpected error: {ErrorMessage}", unexpectedError.Message);
			}
        }

		/// <summary>
		///		Configure the global application logger.
		/// </summary>
		static void ConfigureLogging()
		{
			const string logTemplate = "[{Level}] {Message}{NewLine}{Exception}";

			Log.Logger =
				new LoggerConfiguration()
					.MinimumLevel.Information()
					.Enrich.WithProcessId()
					.Enrich.WithThreadId()
					.WriteTo.Trace(outputTemplate: logTemplate)
					.WriteTo.LiterateConsole(outputTemplate: logTemplate)
					.CreateLogger();
		}
	}
}
