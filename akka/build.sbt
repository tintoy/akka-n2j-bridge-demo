name := "akka-n2j-bridge"
organization := "io.tintoy"
version:= "0.0.1"

scalaVersion := "2.11.7"

libraryDependencies ++= {
  val scalaVersion        = "2.11.7"
  val scalaAsyncVersion   = "0.9.5"
  val scodecVersion       = "1.8.2"
  val scalaXmlVersion     = "1.0.4"
  val akkaVersion         = "2.4.0"
  val akkaStreamsVersion  = "1.0"
  val scalaTestVersion    = "2.2.4"

  Seq(
    // Support for async / await. 
    "org.scala-lang.modules"  %% "scala-async"                      % scalaAsyncVersion,

    // Bit-twiddling and other binary encoding / decoding support.
    "org.scodec"              %% "scodec-core"                      % scodecVersion,

    // Akka
    "com.typesafe.akka"       %% "akka-actor"                       % akkaVersion,
    "com.typesafe.akka"       %% "akka-testkit"                     % akkaVersion         % "test",

    // Akka Streams
    "com.typesafe.akka"       %% "akka-stream-experimental"         % akkaStreamsVersion,
    "com.typesafe.akka"       %% "akka-stream-testkit-experimental" % akkaStreamsVersion  % "test",
    
    // ScalaTest
    "org.scalatest"           %% "scalatest"                        % scalaTestVersion  % "test",

    // Indirect dependencies that we resolve explicitly so we dont' get conflict warnings.
    "org.scala-lang"          %  "scala-reflect"                     % scalaVersion,
    "org.scala-lang.modules"  %% "scala-xml"                        % scalaXmlVersion
  )
}
