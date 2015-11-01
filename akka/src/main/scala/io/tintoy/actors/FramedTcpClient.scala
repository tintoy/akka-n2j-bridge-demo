package io.tintoy.actors

import akka.actor.{ActorRef, Actor}
import akka.io.Tcp
import akka.util.ByteString

import scodec.bits.BitVector

import FramedTcpClient._

/**
 * Actor that exchanges framed message data with a TCP client actor.
 * @param target The actor with which unframed messages are exchanged.
 * @param tcpClient The TCP client actor with which framed messages are exchanged.
 * @param maxFrameSize The maximum frame size (in bytes).
 */
class FramedTcpClient(
    target: ActorRef,
    tcpClient: ActorRef,
    maxFrameSize: Int,
    overflowBehavior: BufferOverflowBehavior
  )
  extends Actor {

  /**
   * Create a new framed TCP client.
   * @param target The actor with which unframed messages are exchanged.
   * @param tcpClient The TCP client actor with which framed messages are exchanged.
   */
  def this(target: ActorRef, tcpClient: ActorRef) =
    this(target, tcpClient, defaultMaxFrameSize, BufferOverflowBehavior.Fail)

  /**
   * Create a new framed TCP client.
   * @param target The actor with which unframed messages are exchanged.
   * @param tcpClient The TCP client actor with which framed messages are exchanged.
   * @param maxFrameSize The maximum frame size (in bytes).
   */
  def this(target: ActorRef, tcpClient: ActorRef, maxFrameSize: Int) =
    this(target, tcpClient, maxFrameSize, BufferOverflowBehavior.Fail)

  var buffer = ByteString.empty
  var currentFrameSize: Option[Int] = None

  def haveData = buffer.nonEmpty
  def haveFrameSize = currentFrameSize.isDefined
  def haveFrame = haveData && haveFrameSize && buffer.length >= currentFrameSize.get

  context.become(waitForPrefix)

  /**
   * Actor is waiting for a frame prefix.
   */
  def waitForPrefix: Receive = {
    // Data received from remote host
    case Tcp.Received(data) =>
      buffer ++= data

      readPrefix()
      while (haveFrame) {
        emitFrame()

        // Next prefix (if available).
        readPrefix()
      }

      if (haveData && !haveFrameSize)
        context.become(waitForRestOfFrame)
  }

  def waitForRestOfFrame: Receive = {
    // Data received from remote host
    case Tcp.Received(data) =>
      while (haveFrame) {
        emitFrame()

        // Next prefix (if available).
        readPrefix()
      }

      context.become(waitForPrefix)
  }

  def readPrefix(): Option[Int] = {
    assert(!haveFrameSize, "Cannot read prefix while there is a current frame")

    // Ok, my ignorance of the framework is showing here.
    // I'm assuming there's at least one class for doing this sort of bit-twiddling shit.
    if (buffer.size >= 4) {
      currentFrameSize = Some({
        val bits = BitVector(
          buffer.slice(0, 4).iterator
        )

        Codecs.BigEndian.frameSize.decode(bits).require.value
      })

      buffer = buffer.drop(4)
    }

    currentFrameSize
  }

  /**
   * Emit the current frame (if any) from the buffer.
   */
  def emitFrame(): Unit = {
    if (haveFrame) {
      val frameSize: Int = currentFrameSize.get
      val frameData = buffer.slice(0, frameSize)

      target ! FrameReceived(frameData)

      buffer = buffer.drop(frameSize)
      currentFrameSize = None // Ready for next frame
    }
  }

  /**
   * Called when the actor receives a message.
   *
   * @note Should be unused due to calls to [[akka.actor.ActorContext.become]].
   */
  def receive: Receive = {
    case message => unhandled(message)
  }
}

/**
 * Constants and messages for the framed TCP client.
 */
object FramedTcpClient {
  /**
   * The default maximum frame size used by the framed TCP client.
   */
  def defaultMaxFrameSize: Int = 512 * 1024

  /**
   * The behaviour of the TCP client when the maximum frame size is exceeded.
   */
  sealed trait BufferOverflowBehavior

  /**
   * The behaviour of the TCP client when the maximum frame size is exceeded.
   */
  object BufferOverflowBehavior {
    /**
     * The client will throw an exception if the maximum frame size is exceeded.
     */
    case object Fail extends BufferOverflowBehavior

    /**
     * The current frame will be dropped if it exceeds the maximum frame size.
     */
    case object Drop extends BufferOverflowBehavior
  }

  /**
   * Message representing a frame received by a [[FramedTcpClient]].
   * @param frame The frame data.
   */
  case class FrameReceived(frame: ByteString)

  /**
   * Binary formatting facilities.
   */
  object Codecs {
    import scodec.Codec

    /**
     * Big-endian binary-formatting facilities.
     */
    object BigEndian {
      /**
       * Big-endian formatting for frame-size prefix.
       */
      val frameSize: Codec[Int] = scodec.codecs.int32
    }

    /**
     * Little-endian binary-formatting facilities.
     */
    object LittleEndian {
      /**
       * Little-endian formatting for frame-size prefix.
       */
      val frameSize: Codec[Int] = scodec.codecs.int32L
    }
  }
}

