package io.tintoy.actors

import java.net.InetSocketAddress
import java.time.Clock
import akka.actor.{ActorRef, Cancellable, Actor}
import akka.io.{IO, Tcp}
import akka.util.ByteString
import com.typesafe.config.{ConfigFactory, Config}

import scala.concurrent.duration.DurationInt

/**
 * An actor that sends the current time once every 5 seconds to a remote socket.
 */
class ClockSender extends Actor {
  import context.system

  /**
    * Connector-specific configuration.
    */
  val connectorConfig: Config =
    ConfigFactory.defaultApplication()
      .getConfig("connector")

  /**
   * The TCP connection manager.
   */
  val tcpManager = IO(Tcp)

  /**
   * The system clock; local time, not UTC.
   */
  val clock = Clock.systemDefaultZone()

  /**
   * The actor used as a TCP client.
   */
  var client: ActorRef = null

  /**
   * Cancellation for the scheduled [[Transmit]] message.
   */
  var tickCancellation:Cancellable = null

  /**
   * Called when the actor receives a message.
   */
  override def receive: Receive = {
    // Connected to remote host.
    case Tcp.Connected(remoteAddress, _) =>
      println(s"Connected to $remoteAddress.")

      // The sender is our TCP client
      client = sender()
      client ! Tcp.Register(self)

      tickCancellation = context.system.scheduler.schedule(
        initialDelay = 5.seconds,
        interval = 5.seconds,
        receiver = self,
        message = Transmit
      )(context.system.dispatcher)

    // Connect or Write failed.
    case Tcp.CommandFailed(command) =>
      println(s"Command failed: $command")

    // Disconnected
    case _ : Tcp.ConnectionClosed =>
      println("Connection closed")

      tickCancellation.cancel()
      tickCancellation = null

      client = null

    // Time to send the time
    case Transmit =>
      val now = clock.instant()
      val time = s"The time is now ${now.toString}"

      println("Sending: " + time)

      client ! Tcp.Write(
        ByteString(time, ByteString.UTF_8)
      )
  }

  /**
   * Called when the actor is started.
   */
  override def preStart(): Unit = {
    super.preStart()

    println("Connecting...")
    IO(Tcp) ! Tcp.Connect(
      new InetSocketAddress(
        connectorConfig.getString("clr.address"),
        connectorConfig.getInt("clr.port"))
    )
  }

  /**
   * Called when the actor is stopped.
   */
  override def postStop(): Unit = {
    super.postStop()

    if (tickCancellation != null) {
      tickCancellation.cancel()
      tickCancellation = null
    }
  }

  /**
   * Time to send the time.
   */
  case object Transmit
}
