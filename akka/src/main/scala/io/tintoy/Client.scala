package io.tintoy

import akka.actor.{Props, ActorSystem}
import io.tintoy.actors.ClockSender

import scala.concurrent.Await
import scala.concurrent.duration.Duration
import scala.io.StdIn

/**
 * The Akka (JVM) client for the Akka-to-Akka.NET bridge demo.
 */
object Client extends App {
  println("Creating actor system..")

  implicit val system = ActorSystem("Akka-JVM")

  println("Initialising client...")
  system.actorOf(
    Props[ClockSender]
  )

  println("Running (press enter to terminate).")
  StdIn.readLine()

  system.terminate()
  Await.result(system.whenTerminated, Duration.Inf)

  println("Terminated.")
}
