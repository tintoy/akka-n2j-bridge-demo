# akka-n2j-bridge-demo
A quick demo of connecting actors in JVM Akka to actors in Akka.NET over a TCP connection (using their respective I/O frameworks).

Eventually I'd like to turn this into paired frameworks for both Akka Akka.NET that can be used to bridge instances of both systems. 

Note that, at the moment, it doesn't try to handle messages that may turn up in pieces. The current plan for that is to use Akka Streams (at least on the Akka side) with Framing (length prefix) support.
