name := "akka-n2j-bridge"
organization := "io.tintoy"
version:= "0.0.1"

scalaVersion := "2.11.7"

libraryDependencies ++= {
  val scalaAsyncVersion   = "0.9.5"
  val akkaVersion         = "2.4.0"
  val akkaStreamsVersion  = "1.0"
  val scalaTestVersion    = "2.2.4"

  Seq(
    // Support for async / await. 
    "org.scala-lang.modules"  %% "scala-async"              % scalaAsyncVersion,
    
    // Akka
    "com.typesafe.akka"       %% "akka-actor"               % akkaVersion,
    "com.typesafe.akka"       %% "akka-testkit"             % akkaVersion       % "test",

    // Akka Streams
    "com.typesafe.akka"       %% "akka-stream-experimental" % akkaStreamsVersion,
    
    // ScalaTest
    "org.scalatest"           %% "scalatest"                % scalaTestVersion  % "test"
  )
}
